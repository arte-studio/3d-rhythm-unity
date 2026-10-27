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
    /// 演出用 LED はキャンバス（LedCanvas，例として Sweep のシェーダー）から送り，同じ値をリングの内側から見た
    /// シミュレーション（LedPreview，HDR とブルーム）にも描く．ノーツ用 LED は NoteLedPainter（旧版と同じ動き）．Viewer は P6．
    /// コマンドライン: -executeMethod Arte.Rhythm.Editor.PlaySceneBuilder.Build
    /// </summary>
    public static class PlaySceneBuilder
    {
        const string ScenePath = "Assets/Rhythm/Scenes/Play.unity";
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
            root.AddComponent<SongVisualSync>().conductor = conductor;

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
            var painter = devices.AddComponent<NoteLedPainter>();
            painter.link = link;
            painter.session = session;
            painter.conductor = conductor;
            var effect = devices.AddComponent<LedCanvasEffect>();
            effect.canvas = canvas;
            effect.conductor = conductor;
            effect.material = MaterialAsset("Arte/LedCanvas/Sweep", "CanvasSweep");

            // 暗幕の部屋で，リングの中心に立った観客の目の高さから見る
            var view = new GameObject("LedPreview");
            var preview = view.AddComponent<LedPreview>();
            preview.link = link;
            preview.material = MaterialAsset("Arte/LedPreview/Point", "LedPoint");
            var light = Object.FindFirstObjectByType<Light>();
            if (light != null) Object.DestroyImmediate(light.gameObject);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.skybox = null;
            var cam = Camera.main;
            cam.transform.SetPositionAndRotation(new Vector3(0, 1.5f, 0), Quaternion.Euler(-10, 0, 0));
            cam.fieldOfView = 90;
            cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.allowHDR = true;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var volume = new GameObject("PostProcess").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = VolumeProfileAsset();

            System.IO.Directory.CreateDirectory("Assets/Rhythm/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[PlaySceneBuilder] {ScenePath}");
        }

        /// <summary>Play シーンだけのビルド（Builds/Play/Play.exe）．速度の計測（-probe）と見た目の確認（-screenshot）に使う．</summary>
        [MenuItem("Tools/Arte Rhythm/Build Play Player")]
        public static void BuildPlayer()
        {
            var report = UnityEditor.BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Play/Play.exe",
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
