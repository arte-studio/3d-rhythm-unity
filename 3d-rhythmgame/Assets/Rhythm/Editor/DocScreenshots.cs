using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arte.Rhythm.Render;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Arte.Rhythm.Editor
{
    /// <summary>
    /// 手引き（docs/authoring/led-effects.md）用のスクリーンショットを撮る．エディタを画面付きで起動して使う:
    ///   Unity.exe -projectPath 3d-rhythmgame -executeMethod Arte.Rhythm.Editor.DocScreenshots.Run
    /// ウィンドウを 1 つずつ開いて撮り，docs/authoring/images/ に保存して終了する（その間はマウスを触らない）．
    /// </summary>
    public static class DocScreenshots
    {
        const string OutDir = "../docs/authoring/images";

        sealed class Step
        {
            public string Name;
            public Func<EditorWindow> Prepare;   // 撮るウィンドウを返す（null ならエディタ全体）
            public float Wait = 2f;
            public bool Keep;                    // 次の手順でも閉じない
            public Action<EditorWindow> Mid;     // 開いて 1 秒後にすること
        }

        static readonly List<EditorWindow> opened = new List<EditorWindow>();

        static List<Step> steps;
        static int index;
        static double nextTime, startTime;
        static EditorWindow current;
        static bool prepared, midDone;

        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            // 前に撮ったときに残った浮いたウィンドウを閉じる
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                string n = w.GetType().Name;
                if (!w.docked && (n == "ProjectBrowser" || n == "InspectorWindow" || n == "SceneHierarchyWindow" || n == "TimelineWindow" || n == "PreviewExportWindow"))
                    w.Close();
            }
            TimelinePreferences.instance.timeFormat = TimeFormat.Seconds;
            EditorSceneManager.OpenScene("Assets/Rhythm/Scenes/Play.unity");
            steps = new List<Step>
            {
                new Step { Name = "00-editor", Prepare = WholeEditor, Wait = 3f, Mid = _ =>
                {
                    FrameAll(Resources.FindObjectsOfTypeAll<TimelineEditorWindow>().First());
                    Selection.activeGameObject = Rhythm();
                } },
                new Step { Name = "01-project-templates", Prepare = ProjectTemplates },
                new Step { Name = "02-hierarchy-rhythm", Prepare = HierarchyRhythm },
                new Step { Name = "03-director-inspector", Prepare = DirectorInspector },
                new Step { Name = "04-timeline", Prepare = TimelineWindow, Wait = 3f, Keep = true, Mid = FrameAll },
                new Step { Name = "05-clip-inspector", Prepare = ClipInspector, Wait = 2.5f },
                new Step { Name = "06-material-inspector", Prepare = MaterialInspector },
                new Step { Name = "07-song-asset", Prepare = SongAssetInspector },
                new Step { Name = "08-preview-export", Prepare = PreviewExport },
                new Step { Name = "10-chart-editor", Prepare = ChartEditor, Wait = 3f },
                new Step { Name = "11-kodaisai-timeline", Prepare = KodaisaiTimeline, Wait = 3f, Mid = FrameAll },
            };
            // 起動引数 -docshot-steps 名前,名前 で一部だけ撮る
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-docshot-steps");
            if (ai >= 0 && ai + 1 < args.Length)
            {
                var only = new HashSet<string>(args[ai + 1].Split(','));
                steps = steps.Where(x => only.Contains(x.Name)).ToList();
            }
            index = 0;
            prepared = false;
            nextTime = EditorApplication.timeSinceStartup + 5;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextTime) return;
            if (index >= steps.Count)
            {
                EditorApplication.update -= Tick;
                CloseOpened();
                // 撮るために変えたシーン（director の Timeline など）は保存しない．保存するか聞かれて止まらないよう，開き直す
                EditorSceneManager.OpenScene("Assets/Rhythm/Scenes/Play.unity");
                Debug.Log("[DocScreenshots] 終わり");
                EditorApplication.Exit(0);
                return;
            }
            var step = steps[index];
            try
            {
                if (!prepared)
                {
                    if (index == 0 || !steps[index - 1].Keep) CloseOpened();
                    current = step.Prepare();
                    if (current != null)
                    {
                        current.Focus();
                        current.Repaint();
                    }
                    prepared = true;
                    midDone = step.Mid == null;
                    startTime = EditorApplication.timeSinceStartup;
                    nextTime = startTime + Math.Min(1, step.Wait);
                    return;
                }
                if (!midDone)
                {
                    step.Mid(current);
                    if (current != null) current.Repaint();
                    midDone = true;
                    nextTime = startTime + step.Wait;
                    return;
                }
                if (EditorApplication.timeSinceStartup < startTime + step.Wait)
                {
                    nextTime = startTime + step.Wait;
                    return;
                }
                if (current == null) WriteMainWindowRects(Path.Combine(OutDir, step.Name + ".json"));
                Capture(current, Path.Combine(OutDir, step.Name + ".png"));
            }
            catch (Exception e)
            {
                Debug.LogError($"[DocScreenshots] {step.Name}: {e}");
            }
            prepared = false;
            index++;
            nextTime = EditorApplication.timeSinceStartup + 0.5;
        }

        // エディタ全体の画像に枠と名前を書くため，主なウィンドウの位置（画像のピクセル）を書き出す
        static void WriteMainWindowRects(string path)
        {
            float s = EditorGUIUtility.pixelsPerPoint;
            var main = EditorGUIUtility.GetMainWindowPosition();
            var names = new Dictionary<string, string>
            {
                { "SceneHierarchyWindow", "Hierarchy（シーンの中身）" },
                { "ProjectBrowser", "Project（ファイル）" },
                { "InspectorWindow", "Inspector（選んだものの設定）" },
                { "GameView", "Game（再生した画面）" },
                { "TimelineWindow", "Timeline（演出を並べる）" },
            };
            var lines = new List<string>();
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (!w.docked || !names.TryGetValue(w.GetType().Name, out var label) || !w.hasFocus && !IsVisible(w)) continue;
                var r = w.position;
                lines.Add($"{{\"label\":\"{label}\",\"x\":{Mathf.RoundToInt((r.x - main.x) * s)},\"y\":{Mathf.RoundToInt((r.y - main.y) * s)}," +
                          $"\"w\":{Mathf.RoundToInt(r.width * s)},\"h\":{Mathf.RoundToInt(r.height * s)}}}");
                names.Remove(w.GetType().Name);
            }
            File.WriteAllText(path, "[\n" + string.Join(",\n", lines) + "\n]\n");
        }

        // タブの裏に隠れていないか
        static bool IsVisible(EditorWindow w)
        {
            var p = typeof(EditorWindow).GetProperty("hasFocus");
            var parent = typeof(EditorWindow).GetField("m_Parent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(w);
            var actual = parent?.GetType().GetProperty("actualView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(parent);
            return actual == null || ReferenceEquals(actual, w);
        }

        static void FrameAll(EditorWindow w)
        {
            w.SendEvent(new Event { type = EventType.ExecuteCommand, commandName = "Timeline/FrameAll" });
        }

        static void CloseOpened()
        {
            foreach (var w in opened)
                if (w != null) w.Close();
            opened.Clear();
        }

        static void Capture(EditorWindow w, string path)
        {
            float s = EditorGUIUtility.pixelsPerPoint;
            var r = w != null ? w.position : EditorGUIUtility.GetMainWindowPosition();
            int x = Mathf.RoundToInt(r.x * s), y = Mathf.RoundToInt(r.y * s);
            int width = Mathf.RoundToInt(r.width * s), height = Mathf.RoundToInt(r.height * s);
            var pixels = InternalEditorUtility.ReadScreenPixel(new Vector2(x, y), width, height);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log($"[DocScreenshots] {path}（{width}×{height}）");
        }

        static EditorWindow Float(string typeName, Rect rect)
        {
            var type = typeof(EditorWindow).Assembly.GetType(typeName);
            var w = (EditorWindow)ScriptableObject.CreateInstance(type);
            w.Show();
            w.position = rect;
            opened.Add(w);
            return w;
        }

        static readonly Rect Small = new Rect(80, 80, 520, 640);
        static readonly Rect Wide = new Rect(80, 80, 1200, 520);

        static EditorWindow WholeEditor()
        {
            TimelineWindow();
            return null;
        }

        // 普段の Project ウィンドウでテンプレートのフォルダを開いて見せる
        static EditorWindow ProjectTemplates()
        {
            EditorApplication.ExecuteMenuItem("Window/General/Project");
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
            var w = (EditorWindow)Resources.FindObjectsOfTypeAll(type).First();
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{EffectTemplatesBuilder.TemplateDir}/Ripple3D.mat");
            Selection.activeObject = mat;
            EditorGUIUtility.PingObject(mat);
            return w;
        }

        static GameObject Rhythm() => GameObject.Find("Rhythm");

        static EditorWindow HierarchyRhythm()
        {
            Selection.activeGameObject = Rhythm();
            var w = Float("UnityEditor.SceneHierarchyWindow", new Rect(80, 80, 420, 360));
            return w;
        }

        // Rhythm の Inspector．PlayableDirector と SongVisualSync 以外は畳む
        static EditorWindow DirectorInspector()
        {
            var go = Rhythm();
            foreach (var c in go.GetComponents<Component>())
                InternalEditorUtility.SetIsInspectorExpanded(c, c is PlayableDirector || c is Song.SongVisualSync);
            Selection.activeGameObject = go;
            return Float("UnityEditor.InspectorWindow", new Rect(80, 80, 520, 720));
        }

        // 普段の（ドッキングされた）Timeline ウィンドウで，見本の全体が見えるようにする
        static EditorWindow TimelineWindow()
        {
            var director = Rhythm().GetComponent<PlayableDirector>();
            Selection.activeGameObject = director.gameObject;
            EditorApplication.ExecuteMenuItem("Window/Sequencing/Timeline");
            var w = Resources.FindObjectsOfTypeAll<TimelineEditorWindow>().First();
            w.SetTimeline(director);
            return w;
        }

        // 曲の Timeline（古代祭版）のクリップ．曲に入れた Timeline なので「小節・拍」の欄が出る
        static EditorWindow ClipInspector()
        {
            var song = AssetDatabase.LoadAssetAtPath<Song.SongAsset>("Assets/Rhythm/Songs/kodaisai-2025/kodaisai-2025.asset");
            var director = Rhythm().GetComponent<PlayableDirector>();
            director.playableAsset = song.timeline;
            var tw = Resources.FindObjectsOfTypeAll<TimelineEditorWindow>().First();
            tw.SetTimeline(director);
            var clip = song.timeline.GetOutputTracks().OfType<LedEffectTrack>().SelectMany(t => t.GetClips()).First(c => c.displayName.StartsWith("11_"));
            var so = new SerializedObject(clip.asset);
            so.FindProperty("template").isExpanded = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            TimelineEditor.selectedClip = clip;
            return Float("UnityEditor.InspectorWindow", new Rect(80, 80, 520, 640));
        }

        static EditorWindow MaterialInspector()
        {
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Material>($"{EffectTemplatesBuilder.TemplateDir}/Ripple3D.mat");
            return Float("UnityEditor.InspectorWindow", Small);
        }

        static EditorWindow SongAssetInspector()
        {
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Rhythm/Songs/kodaisai-2025/kodaisai-2025.asset");
            return Float("UnityEditor.InspectorWindow", new Rect(80, 80, 520, 380));
        }

        static EditorWindow ChartEditor()
        {
            AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Rhythm/Songs/kodaisai-2025/kodaisai-2025.asset"));
            var w = Resources.FindObjectsOfTypeAll<ChartEditorWindow>().First();
            // 曲全体が見えるように縮める
            var so = new SerializedObject(w);
            so.FindProperty("pxPerSec").floatValue = 14.5f;
            so.FindProperty("scrollSec").doubleValue = -1;
            so.ApplyModifiedPropertiesWithoutUndo();
            if (!w.docked)
            {
                w.position = new Rect(80, 80, 1400, 520);
                opened.Add(w);
            }
            return w;
        }

        // 曲の Timeline（古代祭版の演出）．一番上に小節と区切りの目印
        static EditorWindow KodaisaiTimeline()
        {
            var song = AssetDatabase.LoadAssetAtPath<Song.SongAsset>("Assets/Rhythm/Songs/kodaisai-2025/kodaisai-2025.asset");
            var director = Rhythm().GetComponent<PlayableDirector>();
            director.playableAsset = song.timeline;
            Selection.activeGameObject = director.gameObject;
            EditorApplication.ExecuteMenuItem("Window/Sequencing/Timeline");
            var w = Resources.FindObjectsOfTypeAll<TimelineEditorWindow>().First();
            w.SetTimeline(director);
            return w;
        }

        static EditorWindow PreviewExport()
        {
            var w = EditorWindow.GetWindow<PreviewExportWindow>(true, "プレビュー書き出し", true);
            w.position = new Rect(80, 80, 680, 330);
            opened.Add(w);
            return w;
        }
    }
}
