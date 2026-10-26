using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// リファクタ前後の比較に使う基準値（P0）を計測するエディタスクリプト．
///
/// 流れ:
/// 1. Run() でメインシーンを開き，構造物モデル（LEDテープs / tutu）の寸法をログに書く．
/// 2. プレイモードに入り，ウォームアップ後のフレームについて
///    フレーム時間・メインスレッド時間・GC割り当て・バッチ数などを ProfilerRecorder で集計する．
/// 3. 結果を リポジトリ直下の docs/perf/baseline-&lt;マシン名&gt;.json に書き出し，
///    コマンドラインから起動された場合はエディタを終了する．
///
/// コマンドライン例:
///   Unity.exe -projectPath 3d-rhythmgame -executeMethod BaselineProbe.RunFromCommandLine
/// メニュー: Tools/Diagnostics/Run Baseline Probe
/// </summary>
public static class BaselineProbe
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const float WarmupSeconds = 5f;   // シーン起動直後の初期化（LED生成・動画デコード開始）を除外する時間
    const int SampleFrames = 900;     // 集計するフレーム数（60fps なら約15秒）

    // プレイモードを跨いで状態を保持するためのキー（ドメインリロードで static は消える）
    const string KeyActive = "BaselineProbe.Active";
    const string KeyQuit = "BaselineProbe.QuitWhenDone";
    const string KeyModelInfo = "BaselineProbe.ModelInfo";

    static readonly List<double> frameMs = new();
    static readonly List<double> mainThreadMs = new();
    static readonly List<long> gcBytes = new();
    static readonly List<long> batches = new();
    static readonly List<long> setPass = new();
    static ProfilerRecorder recMain, recGc, recBatches, recSetPass;
    static double playStartTime;

    [MenuItem("Tools/Diagnostics/Run Baseline Probe")]
    public static void RunFromMenu() => Begin(false);

    public static void RunFromCommandLine() => Begin(true);

    static void Begin(bool quitWhenDone)
    {
        EditorSceneManager.OpenScene(ScenePath);
        SessionState.SetString(KeyModelInfo, MeasureModels());
        SessionState.SetBool(KeyActive, true);
        SessionState.SetBool(KeyQuit, quitWhenDone);
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Hook()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(KeyActive, false)) return;

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            frameMs.Clear(); mainThreadMs.Clear(); gcBytes.Clear(); batches.Clear(); setPass.Clear();
            playStartTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(KeyActive, false);
            if (SessionState.GetBool(KeyQuit, false)) EditorApplication.Exit(0);
        }
    }

    /// <summary>エディタの update ごとに呼ばれ，ウォームアップ後のフレームを記録する．</summary>
    static void Tick()
    {
        double elapsed = EditorApplication.timeSinceStartup - playStartTime;
        if (elapsed < WarmupSeconds) return;

        if (!recMain.Valid)
        {
            recMain = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            recGc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            recBatches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 1);
            recSetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1);
            return;
        }

        frameMs.Add(Time.unscaledDeltaTime * 1000.0);
        mainThreadMs.Add(recMain.LastValue / 1e6);
        gcBytes.Add(recGc.LastValue);
        batches.Add(recBatches.LastValue);
        setPass.Add(recSetPass.LastValue);

        if (frameMs.Count >= SampleFrames || elapsed > WarmupSeconds + 60)
        {
            EditorApplication.update -= Tick;
            WriteReport();
            recMain.Dispose(); recGc.Dispose(); recBatches.Dispose(); recSetPass.Dispose();
            EditorApplication.ExitPlaymode();
        }
    }

    /// <summary>構造物モデルのレンダラー境界を測り，ストリップ長・高さ・半径を推定する．</summary>
    static string MeasureModels()
    {
        var sb = new StringBuilder();
        foreach (var path in new[] { "Assets/LEDtape/LEDテープs.prefab", "Assets/movie/tutu.prefab" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { sb.Append($"{path}: not found\n"); continue; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var rends = inst.GetComponentsInChildren<Renderer>(true);
            sb.Append($"{path}: renderers={rends.Length}\n");
            if (rends.Length > 0)
            {
                var all = rends[0].bounds;
                foreach (var r in rends) all.Encapsulate(r.bounds);
                sb.Append($"  total bounds center={all.center:F3} size={all.size:F3}\n");
                // メッシュごとの寸法の分布（ストリップ1本の長さと高さを知るため）
                var groups = rends.GroupBy(r => Vector3Int.RoundToInt(r.bounds.size * 100f));
                foreach (var g in groups.OrderByDescending(g => g.Count()).Take(6))
                {
                    var ys = g.Select(r => r.bounds.min.y).ToArray();
                    var yt = g.Select(r => r.bounds.max.y).ToArray();
                    var rad = g.Select(r => new Vector2(r.bounds.center.x, r.bounds.center.z).magnitude).ToArray();
                    sb.Append($"  size(cm)={g.Key} count={g.Count()} bottomY={ys.Min():F3}..{ys.Max():F3} topY={yt.Min():F3}..{yt.Max():F3} radius={rad.Min():F3}..{rad.Max():F3} e.g. {g.First().name}\n");
                }
            }
            if (path.Contains("LEDテープs")) DumpStripTable(inst);
            UnityEngine.Object.DestroyImmediate(inst);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 旧方式（newline コンポーネント）が持つストリップIDと，そのテープメッシュの実寸位置の対応を CSV に書く．
    /// ハード構成データ（ストリップごとの半径・角度・上下端の高さ）の元資料になる．
    /// </summary>
    static void DumpStripTable(GameObject root)
    {
        var csv = new StringBuilder("id,radius_m,angle_deg,bottom_y_m,top_y_m,center_x,center_z,size_x,size_y,size_z\n");
        var rows = new List<(int id, string line)>();
        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "newline") continue;
            int id = new SerializedObject(mb).FindProperty("ID").intValue;
            var r = mb.GetComponent<Renderer>();
            if (r == null) continue;
            var b = r.bounds;
            float radius = new Vector2(b.center.x, b.center.z).magnitude;
            float angle = (Mathf.Atan2(b.center.x, b.center.z) * Mathf.Rad2Deg + 360f) % 360f;
            rows.Add((id, $"{id},{radius:F4},{angle:F2},{b.min.y:F4},{b.max.y:F4},{b.center.x:F4},{b.center.z:F4},{b.size.x:F4},{b.size.y:F4},{b.size.z:F4}"));
        }
        foreach (var row in rows.OrderBy(r => r.id)) csv.Append(row.line).Append('\n');
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/hardware/derived"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "strips-from-prefab.csv"), csv.ToString());
    }

    static string Stats(IEnumerable<double> src)
    {
        var a = src.OrderBy(v => v).ToArray();
        if (a.Length == 0) return "{}";
        double P(double q) => a[Math.Min(a.Length - 1, (int)(q * a.Length))];
        return $"{{\"mean\":{a.Average():F3},\"p50\":{P(0.5):F3},\"p95\":{P(0.95):F3},\"p99\":{P(0.99):F3},\"max\":{a[^1]:F3}}}";
    }

    static void WriteReport()
    {
        int goCount = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
        int rendCount = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length;
        int matCount = Resources.FindObjectsOfTypeAll<Material>().Length;

        var json = new StringBuilder();
        json.Append("{\n");
        json.Append($"  \"machine\": \"{SystemInfo.deviceModel} / {SystemInfo.processorType} / {SystemInfo.graphicsDeviceName}\",\n");
        json.Append($"  \"unity\": \"{Application.unityVersion}\",\n");
        json.Append($"  \"date\": \"{DateTime.Now:yyyy-MM-dd HH:mm}\",\n");
        json.Append($"  \"frames\": {frameMs.Count},\n");
        json.Append($"  \"frameMs\": {Stats(frameMs)},\n");
        json.Append($"  \"mainThreadMs\": {Stats(mainThreadMs)},\n");
        json.Append($"  \"gcBytesPerFrame\": {Stats(gcBytes.Select(v => (double)v))},\n");
        json.Append($"  \"batches\": {Stats(batches.Select(v => (double)v))},\n");
        json.Append($"  \"setPassCalls\": {Stats(setPass.Select(v => (double)v))},\n");
        json.Append($"  \"transforms\": {goCount},\n");
        json.Append($"  \"renderers\": {rendCount},\n");
        json.Append($"  \"materialsLoaded\": {matCount},\n");
        json.Append($"  \"models\": \"{SessionState.GetString(KeyModelInfo, "").Replace("\\", "/").Replace("\"", "'").Replace("\n", "\\n")}\"\n");
        json.Append("}\n");

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/perf"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"baseline-{Environment.MachineName}.json");
        File.WriteAllText(file, json.ToString());
        Debug.Log($"[BaselineProbe] wrote {file}\n{json}");
    }
}
