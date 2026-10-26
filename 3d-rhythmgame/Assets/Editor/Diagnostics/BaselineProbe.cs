using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// リファクタ前後の比較に使う基準値（P0）を取るためのエディタスクリプト．
///
/// - MeasureModels: 構造物モデル（LEDテープs / tutu）の寸法と，ストリップごとの位置を
///   docs/hardware/derived/ に書き出す．ハード構成データ（hardware.json）の元資料．
/// - BuildPlayer: 計測用の開発ビルドを Builds/Baseline/ に作る．処理時間は，エディタではなく
///   この実行ファイルを -probe 付きで起動して測る（Arte.Rhythm.Diagnostics.FrameProbe）．
///   エディタ自体の負荷が混ざらず，同じ exe を本番用ノート PC に持っていけば同条件で比べられるため．
///
/// コマンドライン例:
///   Unity.exe -batchmode -quit -projectPath 3d-rhythmgame -executeMethod BaselineProbe.MeasureModels
///   Unity.exe -batchmode -quit -projectPath 3d-rhythmgame -executeMethod BaselineProbe.BuildPlayer
/// </summary>
public static class BaselineProbe
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";

    static string RepoPath(string relative) => Path.GetFullPath(Path.Combine(Application.dataPath, "../..", relative));

    /// <summary>
    /// エディタのプレイモードで FrameProbe を走らせる．FrameProbe は起動引数 -probe で有効になるので，
    /// エディタを -probe 付きで起動して呼ぶこと．計測が終わってプレイモードを抜けたらエディタを終了する．
    /// </summary>
    public static void RunEditorProbe()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.Exit(0);
        };
        // コンソールの Error Pause が有効だと，旧コードの起動時エラーでプレイが止まり計測が進まないので解除し続ける
        EditorApplication.pauseStateChanged += state =>
        {
            if (state == PauseState.Paused)
            {
                Debug.LogWarning("[BaselineProbe] プレイが一時停止されたので再開します（Error Pause など）");
                EditorApplication.isPaused = false;
            }
        };
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("Tools/Diagnostics/Build Baseline Player")]
    public static void BuildPlayer()
    {
        string exe = RepoPath("3d-rhythmgame/Builds/Baseline/Baseline.exe");
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = exe,
            target = BuildTarget.StandaloneWindows64,
            // Development にしないと ProfilerRecorder の描画カウンタ（バッチ数など）が取れない
            options = BuildOptions.Development,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[BaselineProbe] build {report.summary.result} → {exe} ({report.summary.totalTime})");
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    /// <summary>構造物モデルのレンダラー境界を測り，ストリップ長・高さ・半径を書き出す．</summary>
    [MenuItem("Tools/Diagnostics/Measure Structure Models")]
    public static void MeasureModels()
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
                foreach (var r in rends.Take(4))
                    sb.Append($"  {r.name}: center={r.bounds.center:F3} size={r.bounds.size:F3}\n");
            }
            if (path.Contains("LEDテープs")) DumpStripTable(inst);
            Object.DestroyImmediate(inst);
        }
        string dir = RepoPath("docs/hardware/derived");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "models.txt"), sb.ToString());
        Debug.Log($"[BaselineProbe] models\n{sb}");
    }

    /// <summary>
    /// 旧方式（newline コンポーネント）が持つストリップIDと，そのテープメッシュの実寸位置の対応を CSV に書く．
    /// newline はキャンバスの ID 列に描くので，ID はそのままキャンバス列番号になる．
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
        string dir = RepoPath("docs/hardware/derived");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "strips-from-prefab.csv"), csv.ToString());
    }
}
