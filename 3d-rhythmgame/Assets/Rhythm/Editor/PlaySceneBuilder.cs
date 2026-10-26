using Arte.Rhythm.Gameplay;
using Arte.Rhythm.Net;
using Arte.Rhythm.Song;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Arte.Rhythm.Editor
{
    /// <summary>
    /// 新しいランタイムを試すためのシーン（Assets/Rhythm/Scenes/Play.unity）を作る．
    /// 曲の再生・判定・確認用の画面表示と，実機との通信（DeviceLink，F2 で接続状態）が入っている．
    /// LED の演出の描画（P5）と Viewer（P6）はあとで足す．それまでは LedTestPattern の表示だけが実機に出る．
    /// コマンドライン: -executeMethod Arte.Rhythm.Editor.PlaySceneBuilder.Build
    /// </summary>
    public static class PlaySceneBuilder
    {
        const string ScenePath = "Assets/Rhythm/Scenes/Play.unity";
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

            System.IO.Directory.CreateDirectory("Assets/Rhythm/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[PlaySceneBuilder] {ScenePath}");
        }
    }
}
