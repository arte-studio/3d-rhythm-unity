using System;
using Arte.Rhythm.Render;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace Arte.Rhythm.Editor
{
    /// <summary>
    /// VR（Meta Quest 3 を Quest Link でつないだ PC VR）の設定．docs/architecture-v2.md の P6．
    /// - Set Up OpenXR: Windows 用に OpenXR を入れ，コントローラ（Quest 3 の Touch Plus と Touch）を使えるようにする．
    ///   手の認識（XR Hands）は切っておく（下の SetUp のコメント）．
    ///   起動と同時には VR を始めない（ViewerVR が -vr か下のチェックのときだけ始める）．描き方は両目を 1 回ずつ描く（MultiPass）．
    ///   LED の点の描き方（LedPoint.shader）は頂点番号から四角形を作っているので，両目をまとめて描く方式にはまだ対応していない
    /// - Play in VR: チェックを入れると，エディタの再生で VR が始まる（ヘッドセットに映る．Game 画面には片目が出る）
    /// コマンドライン: -executeMethod Arte.Rhythm.Editor.VrSetup.SetUp
    /// </summary>
    public static class VrSetup
    {
        const string GeneralSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        const string PlayInVrMenu = "Tools/Arte Rhythm/VR/Play in VR";

        [MenuItem("Tools/Arte Rhythm/VR/Set Up OpenXR (Quest Link)")]
        public static void SetUp()
        {
            const BuildTargetGroup group = BuildTargetGroup.Standalone;
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                System.IO.Directory.CreateDirectory("Assets/XR");
                perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(GeneralSettingsPath);
                if (perTarget == null)
                {
                    perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(perTarget, GeneralSettingsPath);
                }
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var general = perTarget.SettingsForBuildTarget(group);
            general.InitManagerOnStart = false;
            XRPackageMetadataStore.AssignLoader(general.Manager, typeof(OpenXRLoader).FullName, group);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(perTarget);

            // OpenXR の設定（無ければ作る．作る関数がパッケージの内部にあるのでリフレクションで呼ぶ）
            var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (openxr == null)
            {
                var type = Type.GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings, Unity.XR.OpenXR.Editor");
                type?.GetMethod("GetOrCreateInstance")?.Invoke(null, null);
                openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            }
            if (openxr == null)
            {
                Debug.LogError("[VrSetup] OpenXR の設定を作れなかった（Project Settings / XR Plug-in Management / OpenXR を一度開いてから実行し直す）");
                return;
            }
            // 新しく入れたパッケージの機能（XR Hands の HandTracking など）を設定に足す．これをしないと GetFeature が null になる
            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(group);
            openxr.renderMode = OpenXRSettings.RenderMode.MultiPass;
            foreach (var f in new UnityEngine.XR.OpenXR.Features.OpenXRFeature[]
                     {
                         openxr.GetFeature<MetaQuestTouchPlusControllerProfile>(),
                         openxr.GetFeature<OculusTouchControllerProfile>(),
                     })
            {
                if (f == null)
                {
                    Debug.LogWarning("[VrSetup] OpenXR の機能が見つからない（Project Settings / XR Plug-in Management / OpenXR で確かめる）");
                    continue;
                }
                f.enabled = true;
                EditorUtility.SetDirty(f);
            }
            // 手の認識（HandTracking）は切っておく．Quest Link で手の認識が使えない（hand provider failed to initialize）と，
            // エディタの再生でゲームが 2 フレームで止まり，ヘッドセットが真っ暗のままになった（2026-11-03 に確かめた．Play.exe では止まらない）．
            // 使うときは Meta Quest Link アプリの「ベータ → 開発者ランタイム機能」をオンにしてから，Tools / Arte Rhythm / VR / Enable Hand Tracking
            var hands = openxr.GetFeature<UnityEngine.XR.Hands.OpenXR.HandTracking>();
            if (hands != null)
            {
                hands.enabled = false;
                EditorUtility.SetDirty(hands);
            }
            EditorUtility.SetDirty(openxr);
            AssetDatabase.SaveAssets();
            Debug.Log("[VrSetup] OpenXR を設定した（Windows，起動時には始めない，MultiPass，Quest のコントローラ）");
        }

        /// <summary>
        /// エディタで Play シーンを VR で再生して 25 秒後に終わる（動作の確かめ用．GUI のエディタで -executeMethod から呼ぶ）．
        /// Play in VR のチェックはそのあと元に戻す．再生でスクリプトが読み込み直されても続くよう SessionState に覚える．
        /// </summary>
        public static void PlayInVrTest()
        {
            SessionState.SetBool("Arte.VrTest.Before", EditorPrefs.GetBool(ViewerVR.EditorPrefsKey, false));
            SessionState.SetBool("Arte.VrTest", true);
            EditorPrefs.SetBool(ViewerVR.EditorPrefsKey, true);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Rhythm/Scenes/Play.unity");
            EditorApplication.isPlaying = true;
        }

        /// <summary>Game ビューを開いて前に出す（無いと再生してもゲームが進まず VR が真っ暗になることがある）．</summary>
        public static void FocusGameView()
        {
            var type = Type.GetType("UnityEditor.GameView, UnityEditor");
            if (type != null) EditorWindow.GetWindow(type, false, null, true).Focus();
        }

        // Play in VR のときは再生を始めたら Game ビューを前に出す（片目の映像を PC でも見られるように）．
        // なお 2026-11-03 にエディタで真っ暗になった本当の原因は手の認識（SetUp のコメント）で，Game ビューではなかった
        [InitializeOnLoadMethod]
        static void WatchPlayMode()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredPlayMode && EditorPrefs.GetBool(ViewerVR.EditorPrefsKey, false)) FocusGameView();
            };
        }

        [InitializeOnLoadMethod]
        static void ResumeVrTest()
        {
            if (!SessionState.GetBool("Arte.VrTest", false)) return;
            EditorApplication.update += () =>
            {
                if (!SessionState.GetBool("Arte.VrTest", false) || !EditorApplication.isPlaying) return;
                float start = SessionState.GetFloat("Arte.VrTest.Start", 0);
                if (start == 0)
                {
                    SessionState.SetFloat("Arte.VrTest.Start", (float)EditorApplication.timeSinceStartup);
                    return;
                }
                if (EditorApplication.timeSinceStartup - start < 25) return;
                Debug.Log($"[VrSetup] 試験の終わり（{Time.frameCount} フレーム）");
                SessionState.SetBool("Arte.VrTest", false);
                EditorPrefs.SetBool(ViewerVR.EditorPrefsKey, SessionState.GetBool("Arte.VrTest.Before", false));
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += () => EditorApplication.Exit(0);
            };
        }

        /// <summary>手の認識を有効にする（Meta Quest Link の開発者ランタイム機能を入れてから．エディタで真っ暗になったら Set Up OpenXR で切る）．</summary>
        [MenuItem("Tools/Arte Rhythm/VR/Enable Hand Tracking")]
        static void EnableHandTracking()
        {
            var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            var hands = openxr != null ? openxr.GetFeature<UnityEngine.XR.Hands.OpenXR.HandTracking>() : null;
            if (hands == null) return;
            hands.enabled = true;
            EditorUtility.SetDirty(hands);
            AssetDatabase.SaveAssets();
            Debug.Log("[VrSetup] 手の認識を有効にした．エディタで VR が真っ暗になったら Set Up OpenXR を実行して切る");
        }

        [MenuItem(PlayInVrMenu)]
        static void TogglePlayInVr() => EditorPrefs.SetBool(ViewerVR.EditorPrefsKey, !EditorPrefs.GetBool(ViewerVR.EditorPrefsKey, false));

        [MenuItem(PlayInVrMenu, true)]
        static bool TogglePlayInVrValidate()
        {
            Menu.SetChecked(PlayInVrMenu, EditorPrefs.GetBool(ViewerVR.EditorPrefsKey, false));
            return true;
        }
    }
}
