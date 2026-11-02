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
    ///   手の認識（XR Hands．Quest Link では Meta Quest Link アプリの「ベータ → 開発者ランタイム機能」が要ることがある）．
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
                         openxr.GetFeature<UnityEngine.XR.Hands.OpenXR.HandTracking>(),     // 手の認識（コントローラなしで遊ぶ）
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
            EditorUtility.SetDirty(openxr);
            AssetDatabase.SaveAssets();
            Debug.Log("[VrSetup] OpenXR を設定した（Windows，起動時には始めない，MultiPass，Quest のコントローラ）");
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
