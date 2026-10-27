using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Arte.Rhythm.Hardware;
using Arte.Rhythm.Net;

// 実機（ファーム v2）とつないで，テスト表示を送りながら状態を表示する．
//
//   dotnet run --project 3d-rhythmgame/LinkTool~ -- <表示> [秒数] [--setup 名前] [--assign MAC 番号] [--brightness 演出 ノーツ] [--fps 送信fps] [--leds 1パケットのLED数] [--raw] [--adaptive] [--srgb] [--mirror IP[:ポート]]
//     表示: off | heads | canvas | notes | rainbow（既定 heads）
//     秒数: 0 なら Ctrl+C まで（既定 0）
//     --raw: 圧縮しない（毎回全ピンをそのまま送る．比較用）
//     --adaptive: 混み具合を見て送る頻度を下げる（LinkOptions.AdaptiveRate）
//     --srgb: 色を sRGB とみなして光量に直してから送る（設営データの outputCurve より優先）
//     --mirror: 同じデータを Viewer（Viewer.exe，ポート既定 8899）にも送る．実機が無くても Viewer で見られる
//   例: dotnet run --project 3d-rhythmgame/LinkTool~ -- canvas 30
//       dotnet run --project 3d-rhythmgame/LinkTool~ -- heads 10 --assign 1C:DB:D4:5B:65:28 5
static class Program
{
    static int Main(string[] args)
    {
        var mode = TestPatterns.Mode.StripHeads;
        double seconds = 0;
        string setupName = "kodaisai-1101";
        string assignMac = null;
        byte assignId = 0;
        int? brightPerf = null, brightNote = null;
        double fps = 60;
        int maxLeds = 0;
        bool raw = false, adaptive = false, srgb = false;
        var mirrors = new System.Collections.Generic.List<System.Net.IPEndPoint>();
        var positional = 0;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--setup": setupName = args[++i]; break;
                case "--assign": assignMac = args[++i]; assignId = byte.Parse(args[++i]); break;
                case "--brightness": brightPerf = int.Parse(args[++i]); brightNote = int.Parse(args[++i]); break;
                case "--fps": fps = double.Parse(args[++i]); break;
                case "--leds": maxLeds = int.Parse(args[++i]); break;
                case "--raw": raw = true; break;
                case "--adaptive": adaptive = true; break;
                case "--srgb": srgb = true; break;
                case "--mirror":
                    if (!MirrorReceiver.TryParseTarget(args[++i], out var ep)) throw new ArgumentException($"--mirror {args[i]}（IP か IP:ポート）");
                    mirrors.Add(ep);
                    break;
                default:
                    if (positional++ == 0) mode = ParseMode(args[i]);
                    else seconds = double.Parse(args[i]);
                    break;
            }
        }

        string dir = FindHardwareDir();
        var config = HardwareJson.LoadConfig(File.ReadAllText(Path.Combine(dir, "hardware.json")));
        var setup = HardwareJson.LoadSetup(File.ReadAllText(Path.Combine(dir, "setups", setupName + ".json")));
        var perf = new PerfLayout(config, setup);
        var notes = new NoteLayout(config, setup);
        var frame = LedFrame.FromLayout(perf, notes, config.devices.count);
        if (maxLeds > 0)
        {
            // 通信の試験用: 1 パケットの LED 数を減らす（パケット数は同じ）
            var counts = new ushort[config.devices.count * LedFrame.PinsPerDevice];
            for (int d = 0; d < config.devices.count; d++)
                for (int p = 0; p < LedFrame.PinsPerDevice; p++)
                    counts[d * LedFrame.PinsPerDevice + p] = (ushort)Math.Min(maxLeds, frame.LedCount(d, p));
            frame = new LedFrame(config.devices.count, counts, notes.NoteOutputPin);
        }

        var watch = Stopwatch.StartNew();
        ulong NowUs() => (ulong)(watch.Elapsed.Ticks / 10) + 1;
        var options = new LinkOptions
        {
            HostPort = config.devices.hostPort, DeviceCount = config.devices.count, Compact = !raw, AdaptiveRate = adaptive, MaxSendFps = fps,
            Curve = srgb ? LedOutputModel.OutputCurve.Srgb : LedOutputModel.ParseCurve(setup.outputCurve),
            MirrorTargets = mirrors,
        };
        options.Config.BrightnessPerf = (byte)(brightPerf ?? setup.brightnessPerf);
        options.Config.BrightnessNote = (byte)(brightNote ?? setup.brightnessNote);
        using var host = new LinkHost(options, NowUs);
        var touching = new bool[config.devices.count, notes.ModulesPerDevice];
        host.Log += msg => Console.WriteLine($"[{watch.Elapsed.TotalSeconds,7:F2}] {msg}");
        host.Touched += t =>
        {
            touching[t.Device, t.Sensor] = t.Down;
            int lane = notes.LaneOf(new ModuleAddress(t.Device, t.Sensor));
            Console.WriteLine($"[{watch.Elapsed.TotalSeconds,7:F2}] タッチ device {t.Device} sensor {t.Sensor} {(t.Down ? "ON " : "OFF")} " +
                              $"レーン {(lane >= 0 ? (lane + 1).ToString() : "-")}  時刻 {t.HostTimeUs / 1e6:F4}（{(t.Synced ? "時刻合わせ済" : "未")}）");
        };
        host.Start();
        Console.WriteLine($"ポート {host.LocalPort} で待受．表示 {mode}，設営 {setup.name}，明るさ {options.Config.BrightnessPerf}/{options.Config.BrightnessNote}，" +
                          $"{(raw ? "圧縮なし" : "圧縮あり")}{(adaptive ? "，頻度の自動調整あり" : "")}，曲線 {options.Curve}．Ctrl+C で終了");

        bool stop = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };
        byte[] canvas = null;
        double nextFrame = 0, nextPrint = 1;
        bool assigned = assignMac == null;
        while (!stop && (seconds <= 0 || watch.Elapsed.TotalSeconds < seconds))
        {
            double now = watch.Elapsed.TotalSeconds;
            host.Update();
            if (now >= nextFrame)
            {
                nextFrame = Math.Max(nextFrame + 1.0 / fps, now);
                TestPatterns.Render(mode, frame, perf, notes, now, touching, ref canvas);
                host.SendFrame(frame);
            }
            if (!assigned)
            {
                var target = host.Snapshot().FirstOrDefault(d => d.Mac.ToString().Equals(assignMac, StringComparison.OrdinalIgnoreCase));
                if (target != null)
                {
                    host.AssignId(target.Mac, assignId);
                    Console.WriteLine($"{target.Mac} に番号 {assignId} を送った（基板が再起動する）");
                    assigned = true;
                }
            }
            if (now >= nextPrint)
            {
                nextPrint += 1;
                PrintStatus(host, now);
            }
            Thread.Sleep(1);
        }
        PrintSummary(host, watch.Elapsed.TotalSeconds);
        // 終了時は消灯してから抜ける
        frame.Clear();
        host.SendFrame(frame);
        Thread.Sleep(50);
        return 0;
    }

    static TestPatterns.Mode ParseMode(string s) => s switch
    {
        "off" => TestPatterns.Mode.Off,
        "heads" => TestPatterns.Mode.StripHeads,
        "canvas" => TestPatterns.Mode.CanvasCheck,
        "notes" => TestPatterns.Mode.NoteModules,
        "rainbow" => TestPatterns.Mode.Rainbow,
        _ => throw new ArgumentException($"表示 {s}（off | heads | canvas | notes | rainbow）"),
    };

    static uint[] prevShown = new uint[256];
    static readonly System.Collections.Generic.List<double> rtts = new System.Collections.Generic.List<double>();

    static void PrintSummary(LinkHost host, double seconds)
    {
        Console.WriteLine($"--- {seconds:F0} 秒のまとめ ---");
        foreach (var d in host.Snapshot())
        {
            if (!d.HasStatus) continue;
            var s = d.Status;
            Console.WriteLine($"device {d.Id}: 表示 {s.FramesShown}，揃わず表示 {s.FramesForced}，捨て {s.FramesDropped}，受信 {s.PacketsRx}，不正 {s.PacketsBad}，" +
                              $"送ったフレーム {host.LastFrame}，送信量 そのままの {d.CompressionRatio * 100:F1}%，頻度を下げた回数 {d.CongestionCount}");
        }
        if (rtts.Count == 0) return;
        rtts.Sort();
        double P(double q) => rtts[Math.Min(rtts.Count - 1, (int)(q * rtts.Count))];
        Console.WriteLine($"往復時間（1 秒ごとの標本 {rtts.Count} 個） 中央 {P(0.5):F1}ms  90% {P(0.9):F1}ms  最大 {rtts[^1]:F1}ms");
    }

    static void PrintStatus(LinkHost host, double now)
    {
        var list = host.Snapshot();
        if (list.Count == 0)
        {
            Console.WriteLine($"[{now,7:F2}] デバイス待ち（HELLO が来ない: 同じ Wi-Fi か，ファイアウォールで UDP 9999 の受信が許可されているか）");
            return;
        }
        foreach (var d in list)
        {
            string head = d.Id == 255 ? $"番号未設定 {d.Mac}" : $"device {d.Id}";
            string line = $"[{now,7:F2}] {head} {(d.Connected ? "接続" : $"切断 {d.SecondsSinceSeen:F0}秒")} {d.EndPoint?.Address}";
            if (d.HasStatus)
            {
                var s = d.Status;
                uint shown = s.FramesShown + s.FramesForced;
                uint fps = d.Id < prevShown.Length ? shown - prevShown[d.Id] : 0;
                if (d.Id < prevShown.Length) prevShown[d.Id] = shown;
                line += $"  表示 {fps}fps（揃わず {s.FramesForced}，捨て {s.FramesDropped}）  RSSI {s.Rssi}  show {s.ShowUs}us  空き {s.FreeHeap / 1024}KB  明るさ {s.BrightnessPerf}/{s.BrightnessNote}";
            }
            if (d.Synced && d.Connected) rtts.Add(d.LastRttUs / 1000.0);
            line += $"  送信 {d.SentKbps:F0}kbps";
            if (!double.IsNaN(d.SendFps)) line += $"（上限 {d.SendFps:F0}fps）";
            if (d.Synced) line += $"  往復 {d.LastRttUs / 1000.0:F1}ms（最短 {d.RttUs / 1000.0:F1}）";
            if (!double.IsNaN(d.DisplayLatencyMs)) line += $"  表示遅れ {d.DisplayLatencyMs:F1}ms";
            Console.WriteLine(line);
        }
    }

    static string FindHardwareDir()
    {
        string rel = Path.Combine("Assets", "Rhythm", "Data", "Hardware");
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                foreach (var c in new[] { Path.Combine(dir.FullName, rel), Path.Combine(dir.FullName, "3d-rhythmgame", rel) })
                    if (Directory.Exists(c)) return c;
            }
        throw new DirectoryNotFoundException(rel);
    }
}
