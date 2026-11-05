using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace Arte.Rhythm.Editor
{
    /// <summary>
    /// Quest 3 単体で動くアプリ（Android の APK）を作る（2026-11-04 ユーザー: 家で作って，外では Quest だけで見せたい）．
    /// - Set Up Quest (Android): Android 用に OpenXR（Meta Quest），コントローラ，手の認識，Vulkan，IL2CPP / ARM64 を設定する．
    ///   起動と同時に VR を始めるのは ViewerVR（Android では -vr が無くても始める）．描き方は PC と同じく MultiPass
    ///   （LedPoint.shader が両目をまとめて描く方式にまだ対応していない．VrSetup のコメント）
    /// - Build Quest APK: Play シーンだけを入れて Builds/Quest/arte-rhythm.apk を作る
    /// コマンドライン: Unity -batchmode -quit -buildTarget Android -projectPath ... -executeMethod Arte.Rhythm.Editor.QuestBuild.BuildFromCommandLine
    /// 入れる: adb install -r Builds/Quest/arte-rhythm.apk（Quest はアプリ → 提供元不明のアプリ に出る）
    /// </summary>
    public static class QuestBuild
    {
        public const string ApkPath = "Builds/Quest/arte-rhythm.apk";
        public const string PackageName = "com.arte.rhythm3d";
        static readonly string[] Scenes = { "Assets/Rhythm/Scenes/Play.unity" };

        [MenuItem("Tools/Arte Rhythm/VR/Set Up Quest (Android)")]
        public static void SetUp()
        {
            const BuildTargetGroup group = BuildTargetGroup.Android;
            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, PackageName);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.forceInternetPermission = true;   // 本物のむぎゅっと（DeviceLink の UDP）とつなぐとき用
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                Debug.LogError("[QuestBuild] XR の設定が無い（先に Tools / Arte Rhythm / VR / Set Up OpenXR (Quest Link) を実行する）");
                return;
            }
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var general = perTarget.SettingsForBuildTarget(group);
            general.InitManagerOnStart = false;
            XRPackageMetadataStore.AssignLoader(general.Manager, typeof(OpenXRLoader).FullName, group);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(perTarget);

            var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (openxr == null)
            {
                Debug.LogError("[QuestBuild] Android の OpenXR の設定が無い（Project Settings / XR Plug-in Management / OpenXR の Android を一度開く）");
                return;
            }
            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(group);
            openxr.renderMode = OpenXRSettings.RenderMode.MultiPass;
            // MetaQuestFeature は別のアセンブリにあるので型の名前で探す
            Enable(openxr.GetFeatures().FirstOrDefault(f => f.GetType().Name == "MetaQuestFeature"), "Meta Quest");
            Enable(openxr.GetFeature<OculusTouchControllerProfile>(), "Oculus Touch");
            Enable(openxr.GetFeature<MetaQuestTouchPlusControllerProfile>(), "Touch Plus");
#if ARTE_XR_HANDS
            // Quest 単体では手の認識がそのまま使える（PC の Quest Link では止まるので切っている．VrSetup）
            Enable(openxr.GetFeature<UnityEngine.XR.Hands.OpenXR.HandTracking>(), "手の認識");
            Enable(openxr.GetFeature<UnityEngine.XR.Hands.OpenXR.MetaHandTrackingAim>(), "手の向き（Meta）");
#endif
            EditorUtility.SetDirty(openxr);
            AssetDatabase.SaveAssets();
            Debug.Log("[QuestBuild] Quest 単体（Android）の設定をした");
        }

        static void Enable(UnityEngine.XR.OpenXR.Features.OpenXRFeature f, string name)
        {
            if (f == null)
            {
                Debug.LogWarning($"[QuestBuild] OpenXR の機能が見つからない: {name}");
                return;
            }
            f.enabled = true;
            EditorUtility.SetDirty(f);
        }

        [MenuItem("Tools/Arte Rhythm/VR/Build Quest APK")]
        public static bool Build()
        {
            SetUp();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ApkPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"[QuestBuild] {s.result}: {ApkPath}（{s.totalSize / 1e6:F1} MB，{s.totalTime.TotalMinutes:F1} 分，エラー {s.totalErrors}）");
            foreach (var m in report.steps.SelectMany(st => st.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                Debug.LogError($"[QuestBuild] {m.content}");
            return s.result == BuildResult.Succeeded;
        }

        public static void BuildFromCommandLine()
        {
            bool ok;
            try { ok = Build(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                ok = false;
            }
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
