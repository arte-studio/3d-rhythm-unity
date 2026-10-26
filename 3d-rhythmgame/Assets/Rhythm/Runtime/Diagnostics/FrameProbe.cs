using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace Arte.Rhythm.Diagnostics
{
    /// <summary>
    /// 処理時間の基準値を測る（P0）．
    ///
    /// 起動引数に -probe があるときだけ動く．シーン読み込み後に自動で生成され，
    /// ウォームアップの後，最大 SampleFrames フレームまたは SampleSeconds 秒のフレーム時間・メインスレッド時間・描画スレッド時間・
    /// GC 割り当て・バッチ数を記録して JSON に書く．
    ///
    /// - ビルド: 実行ファイルと同じフォルダの probe-&lt;マシン名&gt;.json に書いて終了する．
    ///   例: Baseline.exe -probe -screen-width 1280 -screen-height 720
    ///   ビルドはエディタの Tools/Diagnostics/Build Baseline Player．
    /// - エディタ: リポジトリの docs/perf/editor-&lt;マシン名&gt;.json に書いてプレイモードを抜ける．
    ///   旧コードはビルドで動かない（ObjectRelocation がファイルを直接読む）ので，旧版の基準値はこちらで取る．
    ///   起動は BaselineProbe.RunEditorProbe（-probe 付きでエディタを起動する）．
    /// </summary>
    public sealed class FrameProbe : MonoBehaviour
    {
        const float WarmupSeconds = 5f;   // LED 生成や動画デコード開始などの初期化を除外する時間
        const int SampleFrames = 1800;    // 60fps なら約30秒
        const float SampleSeconds = 30f;  // 極端に遅い場合（旧版のエディタ実行で数 FPS）でも打ち切る

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (!Environment.GetCommandLineArgs().Contains("-probe")) return;
            var go = new GameObject("[FrameProbe]");
            DontDestroyOnLoad(go);
            go.AddComponent<FrameProbe>();
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;          // 上限を外して余力を見る
            Application.targetFrameRate = -1;
            yield return new WaitForSecondsRealtime(WarmupSeconds);

            var recMain = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            var recRender = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 1);
            var recGc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            var recBatches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 1);
            var recSetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1);
            yield return null;

            var frame = new double[SampleFrames];
            var main = new double[SampleFrames];
            var render = new double[SampleFrames];
            var gc = new double[SampleFrames];
            var batches = new double[SampleFrames];
            var setPass = new double[SampleFrames];
            int n = 0;
            float until = Time.realtimeSinceStartup + SampleSeconds;
            for (; n < SampleFrames && Time.realtimeSinceStartup < until; n++)
            {
                int i = n;
                yield return null;
                frame[i] = Time.unscaledDeltaTime * 1000.0;
                main[i] = recMain.LastValue / 1e6;
                render[i] = recRender.LastValue / 1e6;
                gc[i] = recGc.LastValue;
                batches[i] = recBatches.LastValue;
                setPass[i] = recSetPass.LastValue;
            }
            Array.Resize(ref frame, n); Array.Resize(ref main, n); Array.Resize(ref render, n);
            Array.Resize(ref gc, n); Array.Resize(ref batches, n); Array.Resize(ref setPass, n);

            var json = new StringBuilder("{\n");
            json.Append($"  \"machine\": \"{SystemInfo.deviceModel} / {SystemInfo.processorType} / {SystemInfo.graphicsDeviceName}\",\n");
            json.Append($"  \"unity\": \"{Application.unityVersion}\",\n");
            json.Append($"  \"date\": \"{DateTime.Now:yyyy-MM-dd HH:mm}\",\n");
            json.Append($"  \"resolution\": \"{Screen.width}x{Screen.height}\",\n");
            json.Append($"  \"vsync\": \"off\",\n");
            json.Append($"  \"host\": \"{(Application.isEditor ? "editor" : "player")}\",\n");
            json.Append($"  \"frames\": {n},\n");
            json.Append($"  \"frameMs\": {Stats(frame)},\n");
            json.Append($"  \"mainThreadMs\": {Stats(main)},\n");
            json.Append($"  \"renderThreadMs\": {Stats(render)},\n");
            json.Append($"  \"gcBytesPerFrame\": {Stats(gc)},\n");
            json.Append($"  \"batches\": {Stats(batches)},\n");
            json.Append($"  \"setPassCalls\": {Stats(setPass)},\n");
            json.Append($"  \"gameObjects\": {FindObjectsByType<Transform>(FindObjectsSortMode.None).Length},\n");
            json.Append($"  \"renderers\": {FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length}\n");
            json.Append("}\n");

            string file = Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/perf", $"editor-{Environment.MachineName}.json"))
                : Path.Combine(Path.GetDirectoryName(Application.dataPath), $"probe-{Environment.MachineName}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, json.ToString());
            Debug.Log($"[FrameProbe] wrote {file}\n{json}");

            recMain.Dispose(); recRender.Dispose(); recGc.Dispose(); recBatches.Dispose(); recSetPass.Dispose();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        static string Stats(double[] src)
        {
            var a = src.OrderBy(v => v).ToArray();
            double P(double q) => a[Math.Min(a.Length - 1, (int)(q * a.Length))];
            return "{" + $"\"mean\":{a.Average():F3},\"p50\":{P(0.5):F3},\"p95\":{P(0.95):F3},\"p99\":{P(0.99):F3},\"max\":{a[a.Length - 1]:F3}" + "}";
        }
    }
}
