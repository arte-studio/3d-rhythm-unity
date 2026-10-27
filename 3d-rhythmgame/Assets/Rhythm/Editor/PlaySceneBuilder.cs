using Arte.Rhythm.Gameplay;
using Arte.Rhythm.Net;
using Arte.Rhythm.Render;
using Arte.Rhythm.Song;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Arte.Rhythm.Editor
{
    /// <summary>
    /// 新しいランタイムを試すためのシーン（Assets/Rhythm/Scenes/Play.unity）を作る．
    /// 曲の再生・判定・確認用の画面表示と，実機との通信（DeviceLink，F2 で接続状態）が入っている．
    /// 演出用 LED はキャンバス（LedCanvas）から送る．演出は Timeline の LED 演出トラック（曲に Timeline が無ければ見本）．同じ値をリングの内側から見た
    /// シミュレーション（LedPreview，HDR とブルーム）にも描く．ノーツ用 LED は NoteLedPainter（旧版と同じ動き）．Viewer は P6．
    /// コマンドライン: -executeMethod Arte.Rhythm.Editor.PlaySceneBuilder.Build
    /// </summary>
    public static class PlaySceneBuilder
    {
        const string ScenePath = "Assets/Rhythm/Scenes/Play.unity";
        const string ViewerScenePath = "Assets/Rhythm/Scenes/Viewer.unity";
        const string RenderDir = "Assets/Rhythm/Runtime/Render";
        const string DefaultSong = "Assets/Rhythm/Songs/kodaisai-2025/kodaisai-2025.asset";
        const string HardwareJson = "Assets/Rhythm/Data/Hardware/hardware.json";
        const string DefaultSetup = "Assets/Rhythm/Data/Hardware/setups/kodaisai-1101.json";

        [MenuItem("Tools/Arte Rhythm/Create Play Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var root = new GameObject("Rhythm");
            var conductor = root.AddComponent<SongConductor>();
            conductor.song = AssetDatabase.LoadAssetAtPath<SongAsset>(DefaultSong);
            var session = root.AddComponent<GameSession>();
            session.conductor = conductor;
            root.AddComponent<KeyboardTouchInput>().session = session;
            var hud = root.AddComponent<DebugHud>();
            hud.conductor = conductor;
            hud.session = session;
            var sync = root.AddComponent<SongVisualSync>();
            sync.conductor = conductor;
            // 演出の Timeline（曲に Timeline が無ければ見本を流す）
            EffectTemplatesBuilder.Build();
            var director = root.AddComponent<UnityEngine.Playables.PlayableDirector>();
            director.playOnAwake = false;
            director.playableAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Timeline.TimelineAsset>(EffectTemplatesBuilder.ShowcasePath);
            sync.director = director;

            var devices = new GameObject("Devices");
            var link = devices.AddComponent<DeviceLink>();
            link.hardwareJson = AssetDatabase.LoadAssetAtPath<TextAsset>(HardwareJson);
            link.setupJson = AssetDatabase.LoadAssetAtPath<TextAsset>(DefaultSetup);
            link.session = session;
            var pattern = devices.AddComponent<LedTestPattern>();
            pattern.link = link;
            var monitor = devices.AddComponent<DeviceMonitorHud>();
            monitor.link = link;
            monitor.testPattern = pattern;

            var canvas = devices.AddComponent<LedCanvas>();
            canvas.link = link;
            canvas.conductor = conductor;
            canvas.compositeMaterial = MaterialAsset("Arte/LedCanvas/Composite", "Composite");
            var painter = devices.AddComponent<NoteLedPainter>();
            painter.link = link;
            painter.session = session;
            painter.conductor = conductor;

            var preview = new GameObject("LedPreview").AddComponent<LedPreview>();
            preview.source = LedPreview.Source.Local;
            preview.link = link;
            AddViewer(preview);

            System.IO.Directory.CreateDirectory("Assets/Rhythm/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[PlaySceneBuilder] {ScenePath}");
        }

        /// <summary>
        /// 別の PC で本番の LED を見るためのシーン（Assets/Rhythm/Scenes/Viewer.unity）．送る側は DeviceLink.mirrorTo か
        /// 起動引数 -mirror にこの PC の IP を入れる．ゲームは動かさず，受け取った LED を描くだけ．
        /// </summary>
        [MenuItem("Tools/Arte Rhythm/Create Viewer Scene")]
        public static void BuildViewerScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var preview = new GameObject("LedPreview").AddComponent<LedPreview>();
            preview.source = LedPreview.Source.Mirror;
            preview.hardwareJson = AssetDatabase.LoadAssetAtPath<TextAsset>(HardwareJson);
            preview.setupJson = AssetDatabase.LoadAssetAtPath<TextAsset>(DefaultSetup);
            AddViewer(preview);
            var hud = preview.gameObject.AddComponent<ViewerHud>();
            hud.preview = preview;
            hud.room = preview.GetComponent<ViewerRoom>();
            System.IO.Directory.CreateDirectory("Assets/Rhythm/Scenes");
            EditorSceneManager.SaveScene(scene, ViewerScenePath);
            Debug.Log($"[PlaySceneBuilder] {ViewerScenePath}");
        }

        // 暗幕の部屋（床・暗幕・テープ本体を LED の光で照らす）と，リングの中心に立った観客の目の高さから見るカメラ
        static void AddViewer(LedPreview preview)
        {
            preview.material = MaterialAsset("Arte/LedPreview/Point", "LedPoint");
            var room = preview.gameObject.AddComponent<ViewerRoom>();
            room.preview = preview;
            room.litMaterial = MaterialAsset("Arte/Viewer/LitByLeds", "LitByLeds");
            var light = Object.FindFirstObjectByType<Light>();
            if (light != null) Object.DestroyImmediate(light.gameObject);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.skybox = null;
            var cam = Camera.main;
            cam.fieldOfView = 90;
            cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.allowHDR = true;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cam.gameObject.AddComponent<ViewerCamera>();
            var volume = new GameObject("PostProcess").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = VolumeProfileAsset();
        }

        /// <summary>Viewer シーンだけのビルド（Builds/Viewer/Viewer.exe）．</summary>
        [MenuItem("Tools/Arte Rhythm/Build Viewer Player")]
        public static void BuildViewerPlayer() => Build(ViewerScenePath, "Builds/Viewer/Viewer.exe");

        /// <summary>Play シーンだけのビルド（Builds/Play/Play.exe）．速度の計測（-probe）と見た目の確認（-screenshot）に使う．</summary>
        [MenuItem("Tools/Arte Rhythm/Build Play Player")]
        public static void BuildPlayer() => Build(ScenePath, "Builds/Play/Play.exe");

        static void Build(string scenePath, string exe)
        {
            var report = UnityEditor.BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log($"[PlaySceneBuilder] ビルド {report.summary.result}（{report.summary.totalErrors} エラー）");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        static Material MaterialAsset(string shader, string name)
        {
            string path = $"{RenderDir}/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            System.IO.Directory.CreateDirectory($"{RenderDir}/Materials");
            mat = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // LED の光のにじみ（ブルーム）と，明るい光が白く飛びすぎないトーンマップ
        static VolumeProfile VolumeProfileAsset()
        {
            string path = $"{RenderDir}/LedPreviewVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.8f);
            bloom.intensity.Override(1.5f);
            bloom.scatter.Override(0.75f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            AssetDatabase.CreateAsset(profile, path);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
