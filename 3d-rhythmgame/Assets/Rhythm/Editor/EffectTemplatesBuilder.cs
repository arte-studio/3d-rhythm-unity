using System.IO;
using Arte.Rhythm.Render;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace Arte.Rhythm.Editor
{
    /// <summary>
    /// LED 演出のテンプレート（Assets/Rhythm/Effects/Templates/*.mat）と，それを並べた見本の Timeline
    /// （Assets/Rhythm/Effects/Samples/Showcase.playable）を作る．テンプレートは複製して使う（元は直さない）．
    /// コマンドライン: -executeMethod Arte.Rhythm.Editor.EffectTemplatesBuilder.Build
    /// </summary>
    public static class EffectTemplatesBuilder
    {
        public const string TemplateDir = "Assets/Rhythm/Effects/Templates";
        public const string ShowcasePath = "Assets/Rhythm/Effects/Samples/Showcase.playable";

        static readonly string[] Templates = { "Solid", "Gradient", "BeatPulse", "Ripple3D", "Plane3D", "Sparkle", "Texture" };

        [MenuItem("Tools/Arte Rhythm/Create LED Effect Templates")]
        public static void Build()
        {
            Directory.CreateDirectory(TemplateDir);
            foreach (var name in Templates) Template(name);
            BuildShowcase();
            AssetDatabase.SaveAssets();
            Debug.Log($"[EffectTemplatesBuilder] {TemplateDir}，{ShowcasePath}");
        }

        public static Material Template(string name)
        {
            string path = $"{TemplateDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find($"Arte/LED/{name}")) { name = name };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static Material Variant(string template, string name, System.Action<Material> setup)
        {
            string dir = Path.GetDirectoryName(ShowcasePath).Replace('\\', '/');
            string path = $"{dir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Template(template)) { name = name };
                setup(mat);
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        // 見本: 下に一周のグラデーションを流し，その上に 16 秒ずつテンプレートを重ねる（重なりでクロスフェード）
        static void BuildShowcase()
        {
            if (AssetDatabase.LoadAssetAtPath<TimelineAsset>(ShowcasePath) != null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ShowcasePath));
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, ShowcasePath);

            var baseTrack = timeline.CreateTrack<LedEffectTrack>("土台");
            Clip(baseTrack, Variant("Gradient", "Showcase_Base", m =>
            {
                m.SetColor("_ColorA", new Color(0.0f, 0.05f, 0.25f));
                m.SetColor("_ColorB", new Color(0.25f, 0.0f, 0.2f));
                m.SetFloat("_Direction", 1);
                m.SetFloat("_Scroll", 0.05f);
            }), 0, 100, LedCanvas.Blend.Normal);

            var top = timeline.CreateTrack<LedEffectTrack>("重ねる演出");
            double t = 4;
            Clip(top, Variant("BeatPulse", "Showcase_Pulse", m => m.SetColor("_Color", new Color(1f, 0.9f, 0.6f))), t, 16, LedCanvas.Blend.Add);
            Clip(top, Variant("Ripple3D", "Showcase_Ripple", m => { }), t += 14, 16, LedCanvas.Blend.Normal);
            Clip(top, Variant("Plane3D", "Showcase_Plane", m => { }), t += 14, 16, LedCanvas.Blend.Normal);
            Clip(top, Variant("Sparkle", "Showcase_Sparkle", m => m.SetFloat("_Density", 0.08f)), t += 14, 16, LedCanvas.Blend.Add);
            Clip(top, Variant("Solid", "Showcase_Flash", m => m.SetColor("_Color", Color.white)), t += 14, 2, LedCanvas.Blend.Add);
            EditorUtility.SetDirty(timeline);
        }

        static void Clip(TrackAsset track, Material material, double start, double duration, LedCanvas.Blend blend)
        {
            var clip = track.CreateClip<LedEffectClip>();
            clip.displayName = material.name;
            clip.start = start;
            clip.duration = duration;
            clip.easeInDuration = System.Math.Min(2, duration / 2);
            clip.easeOutDuration = System.Math.Min(2, duration / 2);
            var asset = (LedEffectClip)clip.asset;
            asset.material = material;
            asset.template.blend = blend;
            EditorUtility.SetDirty(asset);
        }
    }
}
