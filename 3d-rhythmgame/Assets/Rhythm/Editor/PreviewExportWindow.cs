using System.IO;
using Arte.Rhythm.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Arte.Rhythm.Editor
{
    /// <summary>
    /// 演出のプレビュー動画を書き出すウィンドウ（Window / Arte Rhythm / Preview Export）．
    /// Play シーンを開いて Play モードに入り，PreviewRecorder が 1 フレームずつ撮って mp4 にする．終わると Play モードを抜ける．
    /// </summary>
    public sealed class PreviewExportWindow : EditorWindow
    {
        const string PlayScene = "Assets/Rhythm/Scenes/Play.unity";

        PreviewRecorder.Request req = new PreviewRecorder.Request { output = "Recordings/preview.mp4" };

        [MenuItem("Window/Arte Rhythm/Preview Export")]
        static void Open() => GetWindow<PreviewExportWindow>("プレビュー書き出し");

        void OnGUI()
        {
            EditorGUILayout.LabelField("LED 演出をリングの内側から見た動画（曲の音つき）を書き出す", EditorStyles.wordWrappedLabel);
            req.from = EditorGUILayout.FloatField("始める曲の時刻（秒）", req.from);
            req.length = EditorGUILayout.FloatField("長さ（秒）", req.length);
            req.fps = EditorGUILayout.IntSlider("フレームレート", req.fps, 10, 60);
            req.width = EditorGUILayout.IntField("幅", req.width);
            req.height = EditorGUILayout.IntField("高さ", req.height);
            using (new EditorGUILayout.HorizontalScope())
            {
                req.output = EditorGUILayout.TextField("書き出し先", req.output);
                if (GUILayout.Button("…", GUILayout.Width(28)))
                {
                    string p = EditorUtility.SaveFilePanel("書き出し先", Path.GetDirectoryName(Path.GetFullPath(req.output)), "preview", "mp4");
                    if (!string.IsNullOrEmpty(p)) req.output = p;
                }
            }
            req.ffmpeg = EditorGUILayout.TextField("ffmpeg（無ければ PNG の連番）", req.ffmpeg);
            EditorGUILayout.HelpBox("視点や床の種類は Play シーンの設定のまま（中心・大人の目の高さ）．書き出し中は画面が止まって見えるが進んでいる．", MessageType.None);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button("書き出す", GUILayout.Height(32))) Export();
            }
        }

        void Export()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(PlayScene);
            PlayerPrefs.SetString(PreviewRecorder.PrefsKey, JsonUtility.ToJson(req));
            PlayerPrefs.Save();
            EditorApplication.isPlaying = true;
        }
    }
}
