using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Arte.Rhythm.Song;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Arte.Rhythm.Diagnostics
{
    /// <summary>
    /// 演出のプレビュー動画（mp4，曲の音つき）を書き出す．Viewer の画面（LED のシミュレーション）をそのまま撮る．
    ///
    /// 時刻は実時間ではなく 1 フレームずつ進める（AudioClock.Manual）ので，PC が遅くてもコマ落ちしない．
    /// 画面は ffmpeg に渡して mp4 にする（ffmpeg が無ければ PNG の連番を書く）．音は曲の音声から同じ範囲を切り出して付ける
    /// （音声の Load Type が Decompress On Load でないと取り出せないので，そのときは音なし）．
    ///
    /// 使い方:
    /// - エディタ: Window / Arte Rhythm / Preview Export（Play シーンを開いて書き出す）
    /// - ビルド: Play.exe -record out.mp4 [-record-from 秒] [-record-length 秒] [-record-fps 30] [-record-size 1920x1080]
    ///          [-view child|adult|outside|top] [-floor ...] [-ffmpeg ffmpeg.exe のパス] [-effects 演出の名前] [-record-canvas]
    /// -record-canvas を付けると，LED に送る絵（キャンバス．列 × LED，画面で見える値）も 1 フレームずつ
    /// 「出力の名前.canvas.rgba」（RGBA 8bit を下の行から）と「.canvas.json」（大きさと時刻）に書く．演出の評価に使う（EffectEval~）．
    /// </summary>
    public sealed class PreviewRecorder : MonoBehaviour
    {
        /// <summary>エディタのウィンドウから頼むときの設定（PlayerPrefs に JSON で入れて Play モードに入る）．</summary>
        [Serializable]
        public sealed class Request
        {
            public string output = "preview.mp4";
            public float from;
            public float length = 30;
            public int fps = 30;
            public int width = 1920;
            public int height = 1080;
            public string ffmpeg = "ffmpeg";
            public bool canvas;
        }

        public const string PrefsKey = "Arte.PreviewRecorder.Request";

        Request req;
        RenderTexture target;
        Texture2D readback;
        Process ffmpeg;
        Stream stdin;
        string pngDir, wavPath;
        int frames;
        Texture2D canvasRead;
        FileStream canvasOut;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            var r = FromArgs() ?? FromPrefs();
            if (r == null) return;
            var go = new GameObject("[PreviewRecorder]");
            DontDestroyOnLoad(go);
            go.AddComponent<PreviewRecorder>().req = r;
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static Request FromArgs()
        {
            string output = Arg("-record");
            if (output == null) return null;
            var r = new Request { output = output };
            if (float.TryParse(Arg("-record-from"), out var f)) r.from = f;
            if (float.TryParse(Arg("-record-length"), out var l)) r.length = l;
            if (int.TryParse(Arg("-record-fps"), out var fps)) r.fps = fps;
            var size = Arg("-record-size")?.Split('x');
            if (size?.Length == 2 && int.TryParse(size[0], out var w) && int.TryParse(size[1], out var h))
            {
                r.width = w;
                r.height = h;
            }
            r.ffmpeg = Arg("-ffmpeg") ?? r.ffmpeg;
            r.canvas = Array.IndexOf(Environment.GetCommandLineArgs(), "-record-canvas") >= 0;
            return r;
        }

        static Request FromPrefs()
        {
            if (!Application.isEditor || !PlayerPrefs.HasKey(PrefsKey)) return null;
            var r = JsonUtility.FromJson<Request>(PlayerPrefs.GetString(PrefsKey));
            PlayerPrefs.DeleteKey(PrefsKey);
            return r;
        }

        IEnumerator Start()
        {
            yield return null;
            var conductor = FindFirstObjectByType<SongConductor>();
            var cam = Camera.main;
            if (conductor == null || cam == null)
            {
                Finish("SongConductor かカメラが無い");
                yield break;
            }

            target = new RenderTexture(req.width, req.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            target.Create();
            cam.targetTexture = target;
            readback = new Texture2D(req.width, req.height, TextureFormat.RGBA32, false);
            AudioListener.volume = 0;
            Time.captureFramerate = req.fps;

            double step = 1.0 / req.fps;
            AudioClock.Manual = AudioSettings.dspTime;
            conductor.Play(req.from);
            wavPath = WriteAudio(conductor, req.from, req.length);
            StartEncoder();
            Debug.Log($"[PreviewRecorder] {req.output}（{req.from}〜{req.from + req.length} 秒，{req.fps}fps，{req.width}×{req.height}）");

            double end = req.from + req.length;
            while (true)
            {
                yield return new WaitForEndOfFrame();
                double t = conductor.SongTime;
                if (t >= req.from - 1e-6)
                {
                    if (t >= end || !conductor.IsStarted) break;
                    Capture();
                }
                AudioClock.Manual += step;
            }
            Finish(null);
        }

        void Capture()
        {
            var prev = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, req.width, req.height), 0, 0, false);
            RenderTexture.active = prev;
            if (stdin != null)
            {
                var raw = readback.GetRawTextureData<byte>();
                stdin.Write(raw.ToArray(), 0, raw.Length);
            }
            else
            {
                // PNG は上下がそのまま（ReadPixels は下から読むので EncodeToPNG で正しい向きになる）
                File.WriteAllBytes(Path.Combine(pngDir, $"{frames:D6}.png"), readback.EncodeToPNG());
            }
            CaptureCanvas();
            frames++;
        }

        void CaptureCanvas()
        {
            var tex = req.canvas && Render.LedCanvas.Current != null ? Render.LedCanvas.Current.Texture : null;
            if (tex == null) return;
            if (canvasOut == null)
            {
                string basePath = Path.ChangeExtension(Path.GetFullPath(req.output), null);
                canvasOut = File.Create(basePath + ".canvas.rgba");
                canvasRead = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                File.WriteAllText(basePath + ".canvas.json",
                    $"{{\"width\": {tex.width}, \"height\": {tex.height}, \"fps\": {req.fps}, \"from\": {req.from.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
                    $"\"effects\": \"{Song.SongVisualSync.CurrentEffects}\"}}");
            }
            var prev = RenderTexture.active;
            RenderTexture.active = tex;
            canvasRead.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0, false);
            RenderTexture.active = prev;
            var raw = canvasRead.GetRawTextureData<byte>();
            canvasOut.Write(raw.ToArray(), 0, raw.Length);
        }

        void StartEncoder()
        {
            string output = Path.GetFullPath(req.output);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            string audio = wavPath != null ? $"-i \"{wavPath}\" -c:a aac -b:a 192k -shortest " : "";
            var psi = new ProcessStartInfo
            {
                FileName = req.ffmpeg,
                Arguments = $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {req.width}x{req.height} -r {req.fps} -i - {audio}" +
                            $"-vf vflip -c:v libx264 -pix_fmt yuv420p -crf 18 \"{output}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };
            try
            {
                ffmpeg = Process.Start(psi);
                stdin = ffmpeg.StandardInput.BaseStream;
            }
            catch (Exception e)
            {
                ffmpeg = null;
                stdin = null;
                pngDir = Path.ChangeExtension(output, null) + "_frames";
                Directory.CreateDirectory(pngDir);
                Debug.LogWarning($"[PreviewRecorder] ffmpeg を起動できないので PNG の連番を書く（{pngDir}）: {e.Message}");
            }
        }

        // 曲の音声から [from, from + length] を WAV にする（曲時刻 0 = 音声の AudioStartSec）
        static string WriteAudio(SongConductor conductor, double from, double length)
        {
            var clip = conductor.song != null ? conductor.song.audio : null;
            if (clip == null) return null;
            int ch = clip.channels, rate = clip.frequency;
            var all = new float[clip.samples * ch];
            if (!clip.GetData(all, 0))
            {
                Debug.LogWarning("[PreviewRecorder] 音声を取り出せない（Load Type を Decompress On Load にする）．音なしで書き出す");
                return null;
            }
            int n = (int)(length * rate);
            var pcm = new short[n * ch];
            long start = (long)Math.Round((conductor.Data.AudioStartSec + from) * rate);
            for (int i = 0; i < n; i++)
            {
                long s = start + i;
                if (s < 0 || s >= clip.samples) continue;
                for (int c = 0; c < ch; c++) pcm[i * ch + c] = (short)(Mathf.Clamp(all[s * ch + c], -1f, 1f) * 32767);
            }
            string path = Path.Combine(Application.temporaryCachePath, "arte-preview-audio.wav");
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = pcm.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + bytes);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)ch);
                w.Write(rate);
                w.Write(rate * ch * 2);
                w.Write((short)(ch * 2));
                w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(bytes);
                foreach (short v in pcm) w.Write(v);
            }
            return path;
        }

        void Finish(string error)
        {
            canvasOut?.Dispose();
            canvasOut = null;
            if (error != null) Debug.LogError($"[PreviewRecorder] {error}");
            if (stdin != null)
            {
                stdin.Close();
                ffmpeg.WaitForExit();
                Debug.Log($"[PreviewRecorder] 書き出した: {Path.GetFullPath(req.output)}（{frames} フレーム，ffmpeg の終了コード {ffmpeg.ExitCode}）");
            }
            else if (pngDir != null)
            {
                Debug.Log($"[PreviewRecorder] PNG {frames} 枚を書いた: {pngDir}");
            }
            AudioClock.Manual = null;
            Time.captureFramerate = 0;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
