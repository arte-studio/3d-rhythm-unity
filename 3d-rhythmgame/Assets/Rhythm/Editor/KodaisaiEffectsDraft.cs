using System;
using System.IO;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Render;
using Arte.Rhythm.Song;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace Arte.Rhythm.Editor
{
    /// <summary>
    /// 工大祭版（kodaisai-2025）の演出の下書きを Timeline に一度だけ並べる（初期の演出仕様書 docs/effects/original-spec.md から）．
    /// 並べたあとは普通の Timeline として手で直す．もう一度実行しても，Timeline が既にあれば何もしない（手で直したものを消さない）．
    /// 位置は小節で決めている（曲の区切りは小節の頭: 前奏 0〜8，A メロ 9〜16，B メロ 17〜26，サビ1 27〜40，サビ前 41，サビ2 42〜50，最後 51〜）．
    /// 仕様書との対応は docs/effects/kodaisai-2025.md．
    /// </summary>
    public static class KodaisaiEffectsDraft
    {
        const string SongPath = "Assets/Rhythm/Songs/kodaisai-2025/kodaisai-2025.asset";
        const string Dir = "Assets/Rhythm/Effects/kodaisai-2025";
        const string TimelinePath = Dir + "/kodaisai-2025.playable";

        static TempoMap tempo;
        static double songEnd;

        // 色（画面で見える値）
        static readonly Color Blue = new Color(0.05f, 0.3f, 1f);          // 青の洞窟
        static readonly Color DeepBlue = new Color(0.08f, 0.15f, 0.75f);
        static readonly Color DarkBlue = new Color(0.03f, 0.07f, 0.4f);
        static readonly Color DarkPurple = new Color(0.3f, 0.06f, 0.45f);
        static readonly Color Pink = new Color(0.85f, 0.25f, 0.6f);
        static readonly Color DimWhite = new Color(0.6f, 0.6f, 0.65f);
        static readonly Color White = Color.white;
        static readonly Color PaleBlue = new Color(0.7f, 0.8f, 1f);
        static readonly Color LightYellow = new Color(1f, 0.92f, 0.6f);

        [MenuItem("Tools/Arte Rhythm/Draft kodaisai-2025 Effects (once)")]
        public static void Build()
        {
            var song = AssetDatabase.LoadAssetAtPath<SongAsset>(SongPath);
            if (song?.Data == null)
            {
                Debug.LogError($"[KodaisaiEffectsDraft] {SongPath} が読めない");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath) != null)
            {
                Debug.LogWarning($"[KodaisaiEffectsDraft] {TimelinePath} は既にある（手で直したものを消さないよう何もしない）");
                return;
            }
            tempo = song.Data.TempoMap();
            songEnd = song.LengthSec;
            Directory.CreateDirectory(Dir);
            var tl = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(tl, TimelinePath);

            // 上のトラックから順に重なる
            var baseTrack = tl.CreateTrack<LedEffectTrack>("基調色");
            var wipeTrack = tl.CreateTrack<LedEffectTrack>("色の移り変わり");
            var waveTrack = tl.CreateTrack<LedEffectTrack>("波");
            var starTrack = tl.CreateTrack<LedEffectTrack>("星");
            var beatTrack = tl.CreateTrack<LedEffectTrack>("拍とアクセント");

            // ---- 前奏（小節 0〜8）----
            // ① グリッサンド（小節 0 = 1 小節目の前の 1 小節）: 螺旋階段を上るように下から点く．暗めの白 → 青，最後は全部青
            Clip(baseTrack, Mat("SpiralRise", "01_Glissando_SpiralRise", m =>
            {
                m.SetColor("_ColorA", DimWhite);
                m.SetColor("_ColorB", Blue);
                m.SetFloat("_Turns", 2);
            }), Bar(0), Bar(1), LedCanvas.Blend.Normal, 0, 0);
            // 青の基調色（ゆっくり色が変わる）．前奏の最後で夜明けの紫・ピンクへ
            Clip(baseTrack, Mat("Gradient", "02_BlueBase", m =>
            {
                m.SetColor("_ColorA", Blue);
                m.SetColor("_ColorB", DeepBlue);
                m.SetFloat("_Direction", 1);
                m.SetFloat("_Scroll", 0.01f);
            }), Bar(1), Bar(8), LedCanvas.Blend.Normal, 0, 0);
            // ③ 前奏の最後〜A メロ〜B メロ: 夜明けのような暗い紫とピンク（B メロで暗い青に塗り替わっていく）
            Clip(baseTrack, Mat("Gradient", "03_DawnBase", m =>
            {
                m.SetColor("_ColorA", DarkPurple);
                m.SetColor("_ColorB", Pink);
                m.SetFloat("_Direction", 1);
                m.SetFloat("_Scroll", 0.015f);
            }), Bar(7), Bar(27), LedCanvas.Blend.Normal, 0, 0);
            // ② 上からゆっくり白・薄黄色の星が落ちて星空になる
            Clip(starTrack, Mat("StarFall", "04_SlowStarFall", m =>
            {
                m.SetColor("_ColorHead", White);
                m.SetColor("_ColorTail", LightYellow);
                m.SetFloat("_Density", 0.25f);
                m.SetFloat("_Speed", 0.05f);
                m.SetFloat("_Tail", 0.12f);
            }), Bar(1), Bar(5), LedCanvas.Blend.Add, 0, Beats(4));
            // 星空（前奏〜A メロ）: 柔らかくまたたく
            Clip(starTrack, Mat("Starfield", "05_Starfield_Soft", m =>
            {
                m.SetFloat("_Density", 0.05f);
                m.SetFloat("_Every", 2);
                m.SetFloat("_Depth", 0.2f);
            }), Bar(4), Bar(17), LedCanvas.Blend.Normal, Beats(4), 0);
            // ③ 曲の拍に合わせて柔らかく点滅（明るさの差をあまりつけない）．前奏〜A メロ
            Clip(beatTrack, Mat("SoftPulse", "06_SoftPulse", m =>
            {
                m.SetFloat("_Every", 1);
                m.SetFloat("_Depth", 0.2f);
            }), Bar(1), Bar(17), LedCanvas.Blend.Multiply, 0, 0);

            // ---- A メロ（小節 9〜16）----
            // ② 音量で下に波々（強いピンク〜青）
            Clip(waveTrack, Mat("BottomWave", "07_BottomWave", m =>
            {
                m.SetColor("_ColorA", Pink);
                m.SetColor("_ColorB", Blue);
            }), Bar(9), Bar(17), LedCanvas.Blend.Normal, Beats(2), Beats(2));

            // ---- B メロ（小節 17〜26）: どんどん全体が暗い青に，反時計回りに変わっていく ----
            Clip(wipeTrack, Mat("RotateWipe", "08_DarkBlueWipe_CCW", m =>
            {
                m.SetColor("_Color", DarkBlue);
                m.SetFloat("_Clockwise", 0);
            }), Bar(17), Bar(27), LedCanvas.Blend.Normal, 0, 0);
            // 星は B メロで少し速くまたたく（サビに向かって）
            Clip(starTrack, Mat("Starfield", "09_Starfield_Faster", m =>
            {
                m.SetFloat("_Density", 0.06f);
                m.SetFloat("_Every", 1);
                m.SetFloat("_Depth", 0.35f);
            }), Bar(17), Bar(27), LedCanvas.Blend.Normal, 0, 0);

            // ---- サビ1（小節 27〜40）: 全体を白基調．星が一気に落ちていく ----
            Clip(baseTrack, Mat("Gradient", "10_WhiteBase", m =>
            {
                m.SetColor("_ColorA", White);
                m.SetColor("_ColorB", PaleBlue);
                m.SetFloat("_Direction", 0);
                m.SetFloat("_Scroll", 0.05f);
            }), Bar(27), Bar(41), LedCanvas.Blend.Normal, 0, 0);
            Clip(starTrack, Mat("StarFall", "11_StarFall_Chorus1", m =>
            {
                m.SetColor("_ColorHead", White);
                m.SetColor("_ColorTail", LightYellow);
                m.SetFloat("_Density", 0.5f);
                m.SetFloat("_Speed", 0.35f);
                m.SetFloat("_Tail", 0.3f);
            }), Bar(27), Bar(41), LedCanvas.Blend.Add, 0, 0);
            Clip(beatTrack, Mat("BeatPulse", "12_BeatPulse_Chorus1", m =>
            {
                m.SetColor("_Color", White);
                m.SetFloat("_Every", 2);
                m.SetFloat("_Decay", 6);
            }), Bar(27), Bar(41), LedCanvas.Blend.Add, 0, 0, 0.35f);

            // ---- サビ前（小節 41）: デクレッシェンドのあとクレッシェンド．下から上（色は作者も迷っている．仮に紫 → 白）----
            Clip(baseTrack, Mat("Solid", "13_Dark", m => m.SetColor("_Color", DarkBlue)), Bar(41), Bar(42), LedCanvas.Blend.Normal, 0, 0);
            Clip(wipeTrack, Mat("SpiralRise", "14_PreChorus_Rise", m =>
            {
                m.SetColor("_ColorA", new Color(0.6f, 0.2f, 0.65f));
                m.SetColor("_ColorB", White);
                m.SetFloat("_Turns", 0);
                m.SetFloat("_Change", 0.6f);
            }), Bar(41), Bar(42), LedCanvas.Blend.Normal, 0, 0);

            // ---- サビ2（小節 42〜50）: 1 回目より派手に ----
            Clip(baseTrack, Mat("Gradient", "15_WhiteBase2", m =>
            {
                m.SetColor("_ColorA", White);
                m.SetColor("_ColorB", LightYellow);
                m.SetFloat("_Direction", 1);
                m.SetFloat("_Scroll", 0.1f);
            }), Bar(42), Bar(51), LedCanvas.Blend.Normal, 0, 0);
            Clip(starTrack, Mat("StarFall", "16_StarFall_Chorus2", m =>
            {
                m.SetColor("_ColorHead", White);
                m.SetColor("_ColorTail", LightYellow);
                m.SetFloat("_Density", 0.8f);
                m.SetFloat("_Speed", 0.5f);
                m.SetFloat("_Tail", 0.4f);
            }), Bar(42), Bar(51), LedCanvas.Blend.Add, 0, 0);
            Clip(beatTrack, Mat("BeatPulse", "17_BeatPulse_Chorus2", m =>
            {
                m.SetColor("_Color", White);
                m.SetFloat("_Every", 1);
                m.SetFloat("_Decay", 5);
            }), Bar(42), Bar(51), LedCanvas.Blend.Add, 0, 0, 0.5f);

            // ---- 最後（小節 51〜）: 暗い青に白と薄い黄色の星．最後の音で星を明るく，伸びる音で徐々に暗く ----
            Clip(baseTrack, Mat("Gradient", "18_EndBase", m =>
            {
                m.SetColor("_ColorA", DarkBlue);
                m.SetColor("_ColorB", DeepBlue);
                m.SetFloat("_Direction", 1);
                m.SetFloat("_Scroll", 0.01f);
            }), Bar(51), songEnd, LedCanvas.Blend.Normal, 0, songEnd - Bar(52));
            Clip(starTrack, Mat("Starfield", "19_Starfield_End", m =>
            {
                m.SetFloat("_Density", 0.06f);
                m.SetFloat("_Every", 3);
                m.SetFloat("_Depth", 0.2f);
            }), Bar(51), songEnd, LedCanvas.Blend.Normal, 0, songEnd - Bar(52));
            Clip(beatTrack, Mat("Starfield", "20_Starfield_LastNoteBright", m =>
            {
                m.SetFloat("_Density", 0.06f);
                m.SetFloat("_Brightness", 1.5f);
                m.SetFloat("_Depth", 0);
            }), Bar(51), Bar(53), LedCanvas.Blend.Add, 0, Beats(6));

            song.timeline = tl;
            EditorUtility.SetDirty(song);
            SongTimelineSync.Sync(tl, song.Data, song.LengthSec);
            AssetDatabase.SaveAssets();
            Debug.Log($"[KodaisaiEffectsDraft] {TimelinePath} を作って {SongPath} の timeline に入れた");
        }

        static double Bar(int bar) => tempo.TimeOfBarBeat(bar, 1);
        static double Beats(double n) => n * 60.0 / tempo.Points[0].Bpm;

        static Material Mat(string template, string name, Action<Material> setup)
        {
            string path = $"{Dir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(EffectTemplatesBuilder.Template(template)) { name = name };
            setup(mat);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void Clip(TrackAsset track, Material material, double start, double end, LedCanvas.Blend blend, double easeIn, double easeOut, float opacity = 1f)
        {
            var clip = track.CreateClip<LedEffectClip>();
            clip.displayName = material.name;
            clip.start = start;
            clip.duration = end - start;
            clip.easeInDuration = Math.Min(easeIn, clip.duration / 2);
            clip.easeOutDuration = Math.Min(easeOut, clip.duration - clip.easeInDuration);
            var asset = (LedEffectClip)clip.asset;
            asset.material = material;
            asset.template.blend = blend;
            asset.template.opacity = opacity;
            EditorUtility.SetDirty(asset);
        }
    }
}
