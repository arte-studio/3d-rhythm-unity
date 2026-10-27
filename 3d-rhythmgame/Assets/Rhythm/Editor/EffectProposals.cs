using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Render;
using Arte.Rhythm.Song;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace Arte.Rhythm.Editor
{
    /// <summary>
    /// AI（Claude）が作った古代祭版の演出案 A・B・C を Timeline に組み立てる（docs/effects/ai-proposals.md）．
    /// 曲の特徴（Effects/kodaisai-2025-ai/music.json．EffectEval~/music_features.py が書く）のアクセントを読んで，
    /// 光らせるものを選んで置く．実行するたびに案のフォルダを作り直す（AI が組み直すためのもの）．
    /// 手で直したい案は，フォルダごと複製してから直す．
    /// 作った案は曲の SongAsset.effectVariants に入るので，Play 中に F9 で切り替えて比べられる．
    /// </summary>
    public static class EffectProposals
    {
        const string SongPath = "Assets/Rhythm/Songs/kodaisai-2025/kodaisai-2025.asset";
        const string Root = "Assets/Rhythm/Effects/kodaisai-2025-ai";

        // ---- 曲の特徴（music.json）----
        sealed class Accent
        {
            public double t, strength, salience, beat;
            public int bar;
            public string band;
        }

        sealed class Music
        {
            public List<Accent> accents = new List<Accent>();
        }

        // ---- 色（画面で見える値）．初期の演出仕様書の色 ----
        static readonly Color Blue = new Color(0.05f, 0.3f, 1f);
        static readonly Color DeepBlue = new Color(0.06f, 0.14f, 0.6f);
        static readonly Color DarkBlue = new Color(0.02f, 0.05f, 0.3f);
        static readonly Color Night = new Color(0.01f, 0.02f, 0.12f);
        static readonly Color DarkPurple = new Color(0.25f, 0.05f, 0.4f);
        static readonly Color Purple = new Color(0.5f, 0.2f, 0.75f);
        static readonly Color Pink = new Color(0.85f, 0.25f, 0.6f);
        static readonly Color DimWhite = new Color(0.55f, 0.55f, 0.6f);
        static readonly Color White = Color.white;
        static readonly Color PaleBlue = new Color(0.65f, 0.75f, 1f);
        static readonly Color LightYellow = new Color(1f, 0.92f, 0.6f);
        static readonly Color Warm = new Color(1f, 0.75f, 0.45f);
        static readonly Color Black = Color.black;

        // 区切り（小節）: 前奏 0〜8，A メロ 9〜16，B メロ 17〜26，サビ1 27〜40，サビ前 41〜42，サビ2 43〜50，最後 51〜
        const int Intro = 1, AMelo = 9, BMelo = 17, Chorus1 = 27, Pre = 41, Chorus2 = 43, End = 51;

        static TempoMap tempo;
        static double songEnd;
        static Music music;
        static string dir;
        static int matCount;

        [MenuItem("Tools/Arte Rhythm/Build AI Effect Proposals (kodaisai-2025)")]
        public static void Build()
        {
            var song = AssetDatabase.LoadAssetAtPath<SongAsset>(SongPath);
            if (song?.Data == null)
            {
                Debug.LogError($"[EffectProposals] {SongPath} が読めない");
                return;
            }
            EffectTemplatesBuilder.Build();
            tempo = song.Data.TempoMap();
            songEnd = song.LengthSec;
            string musicPath = Root + "/music.json";
            music = File.Exists(musicPath) ? JsonConvert.DeserializeObject<Music>(File.ReadAllText(musicPath)) : new Music();

            var built = new List<TimelineAsset>
            {
                Make("A", "AI案A_星座", BuildA),
                Make("B", "AI案B_夜明け", BuildB),
                Make("C", "AI案C_鼓動", BuildC),
            };
            song.effectVariants = built.ToArray();
            EditorUtility.SetDirty(song);
            foreach (var tl in built) SongTimelineSync.Sync(tl, song.Data, song.LengthSec);
            AssetDatabase.SaveAssets();
            Debug.Log($"[EffectProposals] {string.Join("，", built.Select(t => t.name))} を作った（アクセント {music.accents.Count} 個から選んだ）");
        }

        static TimelineAsset Make(string id, string name, Action<TimelineAsset> build)
        {
            dir = $"{Root}/{id}";
            if (AssetDatabase.IsValidFolder(dir)) AssetDatabase.DeleteAsset(dir);
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
            matCount = 0;
            var tl = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(tl, $"{dir}/{name}.playable");
            build(tl);
            EditorUtility.SetDirty(tl);
            return tl;
        }

        // ================= 案 A: 星座（初期の演出仕様書に忠実に，質を上げる） =================
        // 仕様書の順番と色をそのまま守り，下書きで分かった問題を直す:
        // サビの全面の白を眩しすぎない明るさにし模様を残す，星を本物の星空のように少なく明るさをばらつかせる，
        // 「音のアクセントで全体が点滅」を曲の際立つアクセントだけで控えめに入れる，曲名から星座を描く場面を足す．
        static void BuildA(TimelineAsset tl)
        {
            var sky = tl.CreateTrack<LedEffectTrack>("空の色");
            var change = tl.CreateTrack<LedEffectTrack>("色の移り変わり");
            var wave = tl.CreateTrack<LedEffectTrack>("波");
            var stars = tl.CreateTrack<LedEffectTrack>("星");
            var figure = tl.CreateTrack<LedEffectTrack>("星座");
            var pulse = tl.CreateTrack<LedEffectTrack>("拍");
            var accent = tl.CreateTrack<LedEffectTrack>("アクセント");

            // 前奏① グリッサンド: 螺旋階段を上るように，暗めの白 → 青
            Clip(sky, "SpiralRise", "グリッサンド", 0.4, B(Intro), LedCanvas.Blend.Normal, 1, 0, 0, m =>
            {
                m.SetColor("_ColorA", DimWhite);
                m.SetColor("_ColorB", Blue);
                m.SetFloat("_Turns", 2);
                m.SetFloat("_Change", 0.35f);
            });
            // 前奏: 青の基調色（暗め．一周方向にゆっくり流れる）
            Clip(sky, "Gradient", "青の空", B(Intro), B(AMelo), LedCanvas.Blend.Normal, 1, 0, Beats(8), m =>
            {
                m.SetColor("_ColorA", Mul(Blue, 0.55f));
                m.SetColor("_ColorB", DeepBlue);
                m.SetFloat("_Direction", 1);
                m.SetFloat("_Scroll", 0.008f);
            });
            // 前奏② 上からゆっくり星が落ちて星空になる
            Clip(stars, "StarFall", "ゆっくり落ちる星", B(Intro), B(6), LedCanvas.Blend.Add, 1, 0, Beats(4), m =>
            {
                m.SetColor("_ColorHead", White);
                m.SetColor("_ColorTail", LightYellow);
                m.SetFloat("_Density", 0.12f);
                m.SetFloat("_Speed", 0.04f);
                m.SetFloat("_Tail", 0.15f);
            });
            Clip(stars, "Starfield", "星空", B(4), B(BMelo), LedCanvas.Blend.Add, 1, Beats(8), 0, m => Stars(m, 0.03f, 1.2f, 2, 0.35f));
            // 前奏③ 拍で柔らかく点滅（明るさの差は小さく）
            Clip(pulse, "SoftPulse", "柔らかい点滅", B(Intro), B(BMelo), LedCanvas.Blend.Multiply, 1, 0, 0, m =>
            {
                m.SetFloat("_Every", 1);
                m.SetFloat("_Depth", 0.15f);
            });
            // 前奏の最後〜A メロ: 夜明けのような暗い紫とピンク
            Clip(change, "Gradient", "夜明けの紫とピンク", B(7), B(BMelo + 2), LedCanvas.Blend.Normal, 1, Beats(8), Beats(8), m =>
            {
                m.SetColor("_ColorA", DarkPurple);
                m.SetColor("_ColorB", Mul(Pink, 0.7f));
                m.SetFloat("_Direction", 1);
                m.SetFloat("_Scroll", 0.012f);
            });
            // A メロ② 下に波々（音量で高さが変わる）
            Clip(wave, "BottomWave", "下の波", B(AMelo), B(BMelo), LedCanvas.Blend.Add, 0.8f, Beats(2), Beats(4), m =>
            {
                m.SetColor("_ColorA", Pink);
                m.SetColor("_ColorB", Blue);
                m.SetFloat("_Base", 0.03f);
                m.SetFloat("_Amp", 0.3f);
            });
            // 星座（曲名「星座になれたら」）: A メロの後半で 1 つ描く
            Clip(figure, "Constellation", "星座", B(13), B(BMelo + 1), LedCanvas.Blend.Add, 1, 0, Beats(4), m =>
            {
                m.SetFloat("_Seed", 3);
                m.SetFloat("_Stars", 6);
                m.SetFloat("_Copies", 3);
                m.SetFloat("_Draw", 0.75f);
                m.SetColor("_LineColor", PaleBlue);
            });
            // B メロ: 反時計回りに暗い青へ．星は少し速くまたたく
            Clip(sky, "Solid", "暗い青", B(BMelo), B(Chorus1), LedCanvas.Blend.Normal, 1, 0, 0, m => m.SetColor("_Color", Mul(Pink, 0.45f)));
            Clip(change, "RotateWipe", "反時計回りに暗い青へ", B(BMelo + 2), B(Chorus1), LedCanvas.Blend.Normal, 1, 0, 0, m =>
            {
                m.SetColor("_Color", DarkBlue);
                m.SetFloat("_Clockwise", 0);
            });
            Clip(stars, "Starfield", "またたく星", B(BMelo), B(Chorus1), LedCanvas.Blend.Add, 1, 0, 0, m => Stars(m, 0.035f, 1.3f, 1, 0.5f));
            // サビ1: 白基調（眩しすぎない明るさで上下に模様）．星が一気に落ちる（しだれ桜）
            Clip(sky, "Gradient", "白の空", B(Chorus1), B(Pre), LedCanvas.Blend.Normal, 1, 0, 0, m =>
            {
                m.SetColor("_ColorA", new Color(0.75f, 0.78f, 0.85f));
                m.SetColor("_ColorB", new Color(0.35f, 0.45f, 0.8f));
                m.SetFloat("_Direction", 0);
                m.SetFloat("_Scroll", 0.06f);
            });
            Clip(stars, "StarFall", "流れ星（しだれ桜）", B(Chorus1), B(Pre), LedCanvas.Blend.Add, 1, 0, 0, m =>
            {
                m.SetColor("_ColorHead", White);
                m.SetColor("_ColorTail", LightYellow);
                m.SetFloat("_Density", 0.45f);
                m.SetFloat("_Speed", 0.3f);
                m.SetFloat("_Tail", 0.3f);
            });
            Clip(pulse, "BeatPulse", "拍の光", B(Chorus1), B(Pre), LedCanvas.Blend.Add, 0.18f, 0, 0, m =>
            {
                m.SetColor("_Color", White);
                m.SetFloat("_Every", 2);
                m.SetFloat("_Decay", 6);
            });
            // サビ前: 暗くして，下から上へ（仮に紫 → 白）
            Clip(sky, "Solid", "サビ前の暗さ", B(Pre), B(Chorus2), LedCanvas.Blend.Normal, 1, 0, 0, m => m.SetColor("_Color", DarkBlue));
            Clip(change, "SpiralRise", "下から上へ", B(Pre), B(Chorus2), LedCanvas.Blend.Normal, 1, 0, 0, m =>
            {
                m.SetColor("_ColorA", Purple);
                m.SetColor("_ColorB", new Color(0.85f, 0.85f, 0.9f));
                m.SetFloat("_Turns", 0);
                m.SetFloat("_Change", 0.6f);
            });
            // サビ2: 1 回目より派手に（流れ星を多く速く，毎拍の光）
            Clip(sky, "Gradient", "白と薄黄の空", B(Chorus2), B(End), LedCanvas.Blend.Normal, 1, 0, 0, m =>
            {
                // 薄い黄色を暗くするとオリーブ色に見えた（v3）．暗くせず白に近い暖色にする
                m.SetColor("_ColorA", new Color(0.8f, 0.8f, 0.82f));
                m.SetColor("_ColorB", new Color(0.95f, 0.88f, 0.7f));
                m.SetFloat("_Direction", 1);
                m.SetFloat("_Scroll", 0.08f);
            });
            Clip(stars, "StarFall", "たくさんの流れ星", B(Chorus2), B(End), LedCanvas.Blend.Add, 1, 0, 0, m =>
            {
                m.SetColor("_ColorHead", White);
                m.SetColor("_ColorTail", LightYellow);
                m.SetFloat("_Density", 0.7f);
                m.SetFloat("_Speed", 0.45f);
                m.SetFloat("_Tail", 0.35f);
            });
            // 毎拍だと全体の点滅が 1 秒に 2.5 回になり，アクセントと重なると 3 回を超えた（v1）．2 拍ごとにして，代わりに強めに
            Clip(pulse, "BeatPulse", "2 拍ごとの光", B(Chorus2), B(End), LedCanvas.Blend.Add, 0.28f, 0, 0, m =>
            {
                m.SetColor("_Color", White);
                m.SetFloat("_Every", 2);
                m.SetFloat("_Decay", 6);
            });
            // 最後: 暗い青に星．最後の音で星を明るく，伸びる音でだんだん暗く
            Clip(sky, "Gradient", "夜の青", B(End), songEnd, LedCanvas.Blend.Normal, 1, 0, songEnd - B(End + 2), m =>
            {
                m.SetColor("_ColorA", DarkBlue);
                m.SetColor("_ColorB", Mul(DeepBlue, 0.7f));
                m.SetFloat("_Direction", 1);
                m.SetFloat("_Scroll", 0.006f);
            });
            Clip(stars, "Starfield", "最後の星空", B(End), songEnd, LedCanvas.Blend.Add, 1, 0, songEnd - B(End + 2), m => Stars(m, 0.04f, 1.6f, 3, 0.25f));
            Clip(figure, "Constellation", "最後の星座", B(End), songEnd, LedCanvas.Blend.Add, 1, Beats(2), songEnd - B(End + 2), m =>
            {
                m.SetFloat("_Seed", 3);
                m.SetFloat("_Stars", 6);
                m.SetFloat("_Copies", 3);
                m.SetFloat("_Draw", 0.05f);
                m.SetFloat("_Brightness", 1.3f);
                m.SetColor("_LineColor", PaleBlue);
            });
            // 音のアクセントで全体が点滅（仕様書でも「??」．際立つアクセントだけ，控えめに）
            // 拍で光らせているサビには置かない（拍の光と重なると全体の点滅が 1 秒に 3 回になった．v2）
            Accents(accent, B(AMelo), B(End + 2), minSalience: 3.5, minGapBeats: 8, max: 10, skip: new[] { (B(Chorus1), B(Pre)), (B(Chorus2), B(End)) }, make: a => ("Hit", m =>
            {
                m.SetColor("_Color", White);
                m.SetFloat("_Shape", 0);
                m.SetFloat("_Decay", 0.35f);
            }, 0.3f));
        }

        // ================= 案 B: 夜明け（物語として．静かな夜 → 夜明け → 日の出 → また夜） =================
        // 曲を 1 つの夜の物語にする．A・B メロは我慢して暗く，サビで初めて大きく明るくする（対比で盛り上げる）．
        // 拍で光らせるのは小節の頭だけにして，揺れるオーロラと星座で「見入る」演出にする．サビの直前は 1 拍の暗転．
        static void BuildB(TimelineAsset tl)
        {
            var sky = tl.CreateTrack<LedEffectTrack>("空");
            var aurora = tl.CreateTrack<LedEffectTrack>("オーロラ");
            var stars = tl.CreateTrack<LedEffectTrack>("星");
            var figure = tl.CreateTrack<LedEffectTrack>("星座");
            var motion = tl.CreateTrack<LedEffectTrack>("光の動き");
            var dark = tl.CreateTrack<LedEffectTrack>("暗転");

            // 前奏: 真っ暗から，グリッサンドで細い光が螺旋に駆け上がり，星がひとつずつ灯る
            Clip(motion, "SpiralRise", "グリッサンド", 0.4, B(Intro), LedCanvas.Blend.Add, 0.8f, 0, Beats(2), m =>
            {
                m.SetColor("_ColorA", White);
                m.SetColor("_ColorB", Mul(Blue, 0.25f));
                m.SetFloat("_Turns", 3);
                m.SetFloat("_Change", 0.15f);
                m.SetFloat("_Edge", 0.01f);
            });
            Clip(sky, "Gradient", "夜空", B(Intro), B(BMelo), LedCanvas.Blend.Normal, 1, Beats(4), 0, m =>
            {
                m.SetColor("_ColorA", Mul(DeepBlue, 0.35f));
                m.SetColor("_ColorB", Mul(Blue, 0.45f));
                m.SetFloat("_Direction", 0);
            });
            // v5 では 26 小節がまるごと真っ暗になった（1.6 秒）．星は暗転の直前まで残す
            Clip(stars, "Starfield", "星が灯る", B(Intro), B(Chorus1 - 1, 4), LedCanvas.Blend.Add, 1, Beats(16), Beats(3), m => Stars(m, 0.05f, 1.6f, 3, 0.4f));
            // A メロ: 紫とピンクのオーロラがゆっくり揺れる（夜明け前）
            // v1 では暗すぎて見えなかった（前奏から A メロの光の量 0.02）．もっと見えるように
            Clip(aurora, "Aurora", "夜明け前のオーロラ", B(5), B(BMelo), LedCanvas.Blend.Add, 1f, Beats(16), Beats(4), m =>
            {
                m.SetColor("_ColorA", Pink);
                m.SetColor("_ColorB", Purple);
                m.SetFloat("_Brightness", 1.4f);
                m.SetFloat("_Low", 0.05f);
                m.SetFloat("_High", 0.75f);
                m.SetFloat("_Folds", 4);
                m.SetFloat("_Speed", 0.008f);
                m.SetFloat("_Level", 0.5f);
            });
            // B メロ: オーロラが青く高くなり，星をつないで星座を描く（曲名）
            // v4 では青が暗く見えにくかった（青は同じ値でも暗く見える）．明るい青にして強める
            Clip(aurora, "Aurora", "青いオーロラ", B(BMelo), B(25), LedCanvas.Blend.Add, 1f, Beats(4), Beats(4), m =>
            {
                m.SetColor("_ColorA", new Color(0.2f, 0.5f, 1f));
                m.SetColor("_ColorB", Purple);
                m.SetFloat("_Brightness", 1.6f);
                m.SetFloat("_Low", 0.1f);
                m.SetFloat("_High", 1f);
                m.SetFloat("_Folds", 6);
                m.SetFloat("_Speed", -0.015f);
                m.SetFloat("_Level", 0.6f);
            });
            Clip(figure, "Constellation", "星座を描く", B(19), B(25), LedCanvas.Blend.Add, 1, 0, Beats(4), m =>
            {
                m.SetFloat("_Seed", 7);
                m.SetFloat("_Stars", 7);
                m.SetFloat("_Copies", 2);
                m.SetFloat("_Size", 0.3f);
                m.SetFloat("_Draw", 0.8f);
                m.SetFloat("_LineWidth", 0.08f);
                m.SetFloat("_Brightness", 1.6f);
                m.SetColor("_LineColor", PaleBlue);
            });
            // 25〜26 小節: 光がすっと引いて，サビの直前（26 小節の 4 拍目）は暗転
            Clip(dark, "Solid", "暗転", B(26, 4), B(Chorus1), LedCanvas.Blend.Normal, 1, Beats(0.5), 0, m => m.SetColor("_Color", Black));
            // サビ1: 日の出．下が暖かく上が白い空．光の面が下から上へ（1 小節ごと），流れ星
            Clip(sky, "Gradient", "日の出の空", B(Chorus1), B(Pre), LedCanvas.Blend.Normal, 1, 0, 0, m =>
            {
                m.SetColor("_ColorA", Mul(Warm, 0.75f));
                m.SetColor("_ColorB", new Color(0.55f, 0.65f, 0.9f));
                m.SetFloat("_Direction", 0);
                m.SetFloat("_Mirror", 0);
            });
            Clip(motion, "Hit", "日の出の光", B(Chorus1), B(Chorus1 + 2), LedCanvas.Blend.Add, 0.9f, 0, 0, m =>
            {
                m.SetColor("_Color", White);
                m.SetFloat("_Shape", 1);
                m.SetFloat("_Speed", 0.9f);
                m.SetFloat("_Width", 0.3f);
                m.SetFloat("_Decay", 2.5f);
            });
            Clip(motion, "Plane3D", "昇る光", B(Chorus1 + 2), B(Pre), LedCanvas.Blend.Add, 0.4f, 0, 0, m =>
            {
                m.SetColor("_Color", LightYellow);
                m.SetVector("_Normal", new Vector4(0, 1, 0, 0));
                m.SetFloat("_Every", 4);
                m.SetFloat("_From", -0.3f);
                m.SetFloat("_To", 2.4f);
                m.SetFloat("_Width", 0.35f);
            });
            Clip(stars, "StarFall", "流れ星", B(Chorus1 + 4), B(Pre), LedCanvas.Blend.Add, 0.8f, Beats(8), Beats(4), m =>
            {
                m.SetColor("_ColorHead", White);
                m.SetColor("_ColorTail", LightYellow);
                m.SetFloat("_Density", 0.35f);
                m.SetFloat("_Speed", 0.3f);
                m.SetFloat("_Tail", 0.3f);
            });
            Clip(motion, "SoftPulse", "小節の頭", B(Chorus1), B(Pre), LedCanvas.Blend.Multiply, 1, 0, 0, m =>
            {
                m.SetFloat("_Every", 4);
                m.SetFloat("_Depth", 0.25f);
            });
            // サビ前: 夕暮れ．紫に沈み，下から光がせり上がる（クレッシェンド）
            Clip(sky, "Solid", "夕暮れ", B(Pre), B(Chorus2), LedCanvas.Blend.Normal, 1, Beats(2), 0, m => m.SetColor("_Color", Mul(DarkPurple, 0.6f)));
            Clip(motion, "SpiralRise", "せり上がる光", B(Pre, 3), B(Chorus2), LedCanvas.Blend.Add, 1, 0, 0, m =>
            {
                m.SetColor("_ColorA", Pink);
                m.SetColor("_ColorB", White);
                m.SetFloat("_Turns", 1);
                m.SetFloat("_Change", 0.5f);
                m.SetFloat("_Edge", 0.08f);
            });
            // サビ2: 2 度目の日の出はもっと大きく．空いっぱいのオーロラと流れ星，中心から広がる光
            Clip(sky, "Gradient", "朝の空", B(Chorus2), B(End), LedCanvas.Blend.Normal, 1, 0, 0, m =>
            {
                m.SetColor("_ColorA", Mul(Warm, 0.8f));
                m.SetColor("_ColorB", new Color(0.6f, 0.7f, 0.95f));
                m.SetFloat("_Direction", 0);
                m.SetFloat("_Mirror", 0);
            });
            // サビ1 との違い（繰り返しと変化）: 始まりに全体がぱっと明るくなり，2 小節ごとに光が左右へ広がる
            Clip(motion, "Hit", "2 度目の日の出", B(Chorus2), B(Chorus2 + 2), LedCanvas.Blend.Add, 0.8f, 0, 0, m =>
            {
                m.SetColor("_Color", LightYellow);
                m.SetFloat("_Shape", 0);
                m.SetFloat("_Decay", 2.0f);
            });
            for (int bar = Chorus2 + 2; bar < End; bar += 2)
            {
                int b = bar;
                Clip(motion, "Hit", $"広がる光 {b}", B(b), B(b + 1), LedCanvas.Blend.Add, 0.45f, 0, 0, m =>
                {
                    m.SetColor("_Color", White);
                    m.SetFloat("_Shape", 3);
                    m.SetFloat("_Start", (b * 0.29f) % 1f);
                    m.SetFloat("_Speed", 1.2f);
                    m.SetFloat("_Width", 0.15f);
                    m.SetFloat("_Decay", 1.2f);
                });
            }
            Clip(aurora, "Aurora", "朝のオーロラ", B(Chorus2), B(End), LedCanvas.Blend.Add, 0.6f, 0, Beats(4), m =>
            {
                m.SetColor("_ColorA", Pink);
                m.SetColor("_ColorB", White);
                m.SetFloat("_Low", 0.3f);
                m.SetFloat("_High", 1f);
                m.SetFloat("_Folds", 7);
                m.SetFloat("_Speed", 0.03f);
                m.SetFloat("_Level", 0.4f);
            });
            Clip(stars, "StarFall", "たくさんの流れ星", B(Chorus2), B(End), LedCanvas.Blend.Add, 0.9f, 0, 0, m =>
            {
                m.SetColor("_ColorHead", White);
                m.SetColor("_ColorTail", LightYellow);
                m.SetFloat("_Density", 0.55f);
                m.SetFloat("_Speed", 0.4f);
                m.SetFloat("_Tail", 0.35f);
            });
            Clip(motion, "Ripple3D", "広がる光", B(Chorus2), B(End), LedCanvas.Blend.Add, 0.3f, 0, 0, m =>
            {
                m.SetColor("_Color", White);
                m.SetVector("_Center", new Vector4(0, 1, 0, 0));
                m.SetFloat("_Every", 4);
                m.SetFloat("_Speed", 0.6f);
                m.SetFloat("_Width", 0.3f);
            });
            // 最後: また夜．B メロの星座が空に残り，最後の音で明るく光ってから消えていく
            Clip(sky, "Gradient", "夜に戻る", B(End), songEnd, LedCanvas.Blend.Normal, 1, Beats(4), songEnd - B(End + 2), m =>
            {
                m.SetColor("_ColorA", Night);
                m.SetColor("_ColorB", DarkBlue);
                m.SetFloat("_Direction", 0);
            });
            Clip(stars, "Starfield", "夜の星", B(End), songEnd, LedCanvas.Blend.Add, 1, Beats(4), songEnd - B(End + 3), m => Stars(m, 0.04f, 1.3f, 3, 0.35f));
            Clip(figure, "Constellation", "残った星座", B(End), songEnd, LedCanvas.Blend.Add, 1, Beats(4), songEnd - B(End + 2), m =>
            {
                m.SetFloat("_Seed", 7);
                m.SetFloat("_Stars", 7);
                m.SetFloat("_Copies", 2);
                m.SetFloat("_Size", 0.3f);
                m.SetFloat("_Draw", 0.05f);
                m.SetFloat("_Brightness", 1.4f);
                m.SetColor("_LineColor", PaleBlue);
            });
        }

        // ================= 案 C: 鼓動（リズムと音のアクセント．子どもの周りを光が回る） =================
        // 曲のリズムを体で感じる演出．光の帯が子どもの周りを回り，速さは場面で変わる（B メロで加速，サビで最大）．
        // 曲の際立つアクセントにヒットを置く（アルゴリズムが拾った候補から選ぶ）．全体の点滅は 1 秒に 3 回まで．
        static void BuildC(TimelineAsset tl)
        {
            var sky = tl.CreateTrack<LedEffectTrack>("土台の色");
            var stars = tl.CreateTrack<LedEffectTrack>("星");
            var beam = tl.CreateTrack<LedEffectTrack>("回る光");
            var floor = tl.CreateTrack<LedEffectTrack>("床からの光");
            var beat = tl.CreateTrack<LedEffectTrack>("拍");
            var accent = tl.CreateTrack<LedEffectTrack>("アクセント");

            Clip(beam, "SpiralRise", "グリッサンド", 0.4, B(Intro), LedCanvas.Blend.Normal, 1, 0, 0, m =>
            {
                m.SetColor("_ColorA", White);
                m.SetColor("_ColorB", Mul(Blue, 0.6f));
                m.SetFloat("_Turns", 2);
                m.SetFloat("_Change", 0.3f);
            });
            Clip(sky, "Gradient", "青の土台", B(Intro), B(AMelo), LedCanvas.Blend.Normal, 1, 0, Beats(4), m =>
            {
                m.SetColor("_ColorA", Mul(Blue, 0.35f));
                m.SetColor("_ColorB", Mul(DeepBlue, 0.6f));
                m.SetFloat("_Direction", 1);
                m.SetFloat("_Scroll", 0.02f);
            });
            Clip(stars, "Starfield", "星", B(Intro), B(Chorus1), LedCanvas.Blend.Add, 1, Beats(8), 0, m => Stars(m, 0.03f, 1.2f, 1, 0.5f));
            // 前奏: 青い光が 1 本，4 小節で一周
            Clip(beam, "Beam", "回る光（ゆっくり）", B(Intro), B(AMelo), LedCanvas.Blend.Add, 0.7f, Beats(4), 0, m =>
            {
                m.SetColor("_Color", PaleBlue);
                m.SetFloat("_Count", 1);
                m.SetFloat("_Width", 0.12f);
                m.SetFloat("_Speed", 1f / 16);
                m.SetFloat("_Trail", 0.4f);
            });
            // A メロ: ピンクの土台，床から 2 拍ごとに光の輪（足元から体に響く）
            Clip(sky, "Gradient", "ピンクの土台", B(AMelo), B(BMelo), LedCanvas.Blend.Normal, 1, Beats(4), 0, m =>
            {
                m.SetColor("_ColorA", Mul(DarkPurple, 0.8f));
                m.SetColor("_ColorB", Mul(Pink, 0.45f));
                m.SetFloat("_Direction", 1);
                m.SetFloat("_Scroll", 0.02f);
            });
            Clip(floor, "Ripple3D", "床からの輪", B(AMelo), B(BMelo), LedCanvas.Blend.Add, 0.5f, 0, 0, m =>
            {
                m.SetColor("_Color", Pink);
                m.SetVector("_Center", new Vector4(0, -0.5f, 0, 0));
                m.SetFloat("_Every", 2);
                m.SetFloat("_Speed", 1.2f);
                m.SetFloat("_Width", 0.25f);
            });
            Clip(floor, "BottomWave", "下の波", B(AMelo), B(BMelo), LedCanvas.Blend.Add, 0.6f, 0, 0, m =>
            {
                m.SetColor("_ColorA", Pink);
                m.SetColor("_ColorB", Blue);
                m.SetFloat("_Base", 0.02f);
                m.SetFloat("_Amp", 0.25f);
            });
            // B メロ: 暗い青へ．回る光が 3 本になり，加速していく
            Clip(sky, "Gradient", "暗い青の土台", B(BMelo), B(Chorus1), LedCanvas.Blend.Normal, 1, Beats(4), 0, m =>
            {
                m.SetColor("_ColorA", DarkBlue);
                m.SetColor("_ColorB", Mul(DeepBlue, 0.7f));
                m.SetFloat("_Direction", 0);
            });
            Clip(beam, "Beam", "回る光（3 本）", B(BMelo), B(22), LedCanvas.Blend.Add, 0.6f, Beats(2), 0, m =>
            {
                m.SetColor("_Color", Blue);
                m.SetFloat("_Count", 3);
                m.SetFloat("_Width", 0.1f);
                m.SetFloat("_Speed", -1f / 24);
                m.SetFloat("_Trail", 0.35f);
            });
            Clip(beam, "Beam", "回る光（速く）", B(22), B(26, 3), LedCanvas.Blend.Add, 0.75f, 0, Beats(1), m =>
            {
                m.SetColor("_Color", PaleBlue);
                m.SetFloat("_Count", 3);
                m.SetFloat("_Width", 0.12f);
                m.SetFloat("_Speed", -1f / 10);
                m.SetFloat("_Twist", 0.15f);
                m.SetFloat("_Trail", 0.4f);
            });
            // サビ1: 白と青．2 本の光が逆向きに回り，2・4 拍目で光る．流れ星
            // v3 では土台が明るい薄紫で，回る光が埋もれた．土台を暗い青にして光を目立たせる
            Clip(sky, "Gradient", "サビの土台", B(Chorus1), B(Pre), LedCanvas.Blend.Normal, 1, 0, 0, m =>
            {
                m.SetColor("_ColorA", new Color(0.12f, 0.2f, 0.55f));
                m.SetColor("_ColorB", new Color(0.04f, 0.06f, 0.3f));
                m.SetFloat("_Direction", 1);
                m.SetFloat("_Scroll", 0.05f);
            });
            Clip(beam, "Beam", "回る光（白）", B(Chorus1), B(Pre), LedCanvas.Blend.Add, 1f, 0, 0, m =>
            {
                m.SetColor("_Color", White);
                m.SetFloat("_Count", 2);
                m.SetFloat("_Width", 0.15f);
                m.SetFloat("_Speed", 1f / 8);
                m.SetFloat("_Twist", 0.25f);
                m.SetFloat("_Trail", 0.3f);
            });
            Clip(stars, "StarFall", "流れ星", B(Chorus1), B(Pre), LedCanvas.Blend.Add, 0.7f, 0, 0, m =>
            {
                m.SetColor("_ColorHead", White);
                m.SetColor("_ColorTail", LightYellow);
                m.SetFloat("_Density", 0.3f);
                m.SetFloat("_Speed", 0.35f);
                m.SetFloat("_Tail", 0.25f);
            });
            Backbeat(beat, B(Chorus1), B(Pre), 0.3f, PaleBlue);
            // サビ前: 光が下から積み上がる（1 拍ごとに 1 段．クレッシェンド）
            Clip(sky, "Solid", "サビ前の暗さ", B(Pre), B(Chorus2), LedCanvas.Blend.Normal, 1, 0, 0, m => m.SetColor("_Color", Mul(DarkBlue, 0.6f)));
            for (int k = 0; k < 8; k++)
            {
                double s = B(Pre) + Beats(k);
                // v1 は細くて速すぎ，ほとんど見えなかった．下から 1 段ずつ高く届く帯にする
                Clip(beam, "Hit", $"積み上がる光 {k + 1}", s, B(Chorus2), LedCanvas.Blend.Add, 0.5f + 0.06f * k, 0, 0, m =>
                {
                    m.SetColor("_Color", Color.Lerp(Purple, White, k / 7f));
                    m.SetFloat("_Shape", 1);
                    m.SetFloat("_Speed", 0.4f + 0.1f * k);
                    m.SetFloat("_Width", 0.12f);
                    m.SetFloat("_Decay", 6f);
                });
            }
            // サビ2: いちばん派手に．4 本の光が回り，中心から毎拍の輪，2・4 拍目の光
            // サビ1 との違い: 土台を紫に，白とピンクの光が逆向きに回って交差する
            Clip(sky, "Gradient", "サビ2の土台", B(Chorus2), B(End), LedCanvas.Blend.Normal, 1, 0, 0, m =>
            {
                m.SetColor("_ColorA", new Color(0.3f, 0.08f, 0.45f));
                m.SetColor("_ColorB", new Color(0.08f, 0.08f, 0.35f));
                m.SetFloat("_Direction", 1);
                m.SetFloat("_Scroll", -0.06f);
            });
            Clip(beam, "Beam", "回る光（白 3 本）", B(Chorus2), B(End), LedCanvas.Blend.Add, 1f, 0, 0, m =>
            {
                m.SetColor("_Color", White);
                m.SetFloat("_Count", 3);
                m.SetFloat("_Width", 0.14f);
                m.SetFloat("_Speed", 1f / 6);
                m.SetFloat("_Twist", -0.3f);
                m.SetFloat("_Trail", 0.3f);
            });
            Clip(beam, "Beam", "回る光（ピンク 3 本．逆回り）", B(Chorus2), B(End), LedCanvas.Blend.Add, 0.9f, 0, 0, m =>
            {
                m.SetColor("_Color", Pink);
                m.SetFloat("_Count", 3);
                m.SetFloat("_Width", 0.12f);
                m.SetFloat("_Speed", -1f / 8);
                m.SetFloat("_Twist", 0.3f);
                m.SetFloat("_Trail", 0.3f);
            });
            Clip(floor, "Ripple3D", "中心からの輪", B(Chorus2), B(End), LedCanvas.Blend.Add, 0.35f, 0, 0, m =>
            {
                m.SetColor("_Color", Pink);
                m.SetVector("_Center", new Vector4(0, 1, 0, 0));
                m.SetFloat("_Every", 1);
                m.SetFloat("_Speed", 1.4f);
                m.SetFloat("_Width", 0.2f);
            });
            Clip(stars, "StarFall", "流れ星（多く）", B(Chorus2), B(End), LedCanvas.Blend.Add, 0.8f, 0, 0, m =>
            {
                m.SetColor("_ColorHead", White);
                m.SetColor("_ColorTail", LightYellow);
                m.SetFloat("_Density", 0.45f);
                m.SetFloat("_Speed", 0.45f);
                m.SetFloat("_Tail", 0.3f);
            });
            Backbeat(beat, B(Chorus2), B(End), 0.35f, White);
            // 最後: 光がゆっくりになって止まり，星空に
            Clip(sky, "Gradient", "夜の土台", B(End), songEnd, LedCanvas.Blend.Normal, 1, 0, songEnd - B(End + 2), m =>
            {
                m.SetColor("_ColorA", DarkBlue);
                m.SetColor("_ColorB", Night);
                m.SetFloat("_Direction", 0);
            });
            Clip(beam, "Beam", "回る光（止まっていく）", B(End), B(End + 4), LedCanvas.Blend.Add, 0.5f, 0, Beats(12), m =>
            {
                m.SetColor("_Color", PaleBlue);
                m.SetFloat("_Count", 1);
                m.SetFloat("_Width", 0.15f);
                m.SetFloat("_Speed", 1f / 24);
                m.SetFloat("_Trail", 0.5f);
            });
            Clip(stars, "Starfield", "最後の星", B(End), songEnd, LedCanvas.Blend.Add, 1, Beats(4), songEnd - B(End + 3), m => Stars(m, 0.035f, 1.4f, 3, 0.3f));
            // 際立つアクセント: 形を順番に変える（左右に広がる / 下から上 / 上から下）
            int n = 0;
            Accents(accent, 0, B(End + 3), minSalience: 2.8, minGapBeats: 4, max: 24, skip: new[] { (B(Chorus1), B(Pre)), (B(Chorus2), B(End)) }, make: a =>
            {
                int shape = new[] { 3, 1, 2 }[n++ % 3];
                float start = (float)((a.bar * 0.37) % 1.0);
                return ("Hit", m =>
                {
                    m.SetColor("_Color", a.band == "low" ? Pink : White);
                    m.SetFloat("_Shape", shape);
                    m.SetFloat("_Speed", shape == 3 ? 2.5f : 2.2f);
                    m.SetFloat("_Width", 0.2f);
                    m.SetFloat("_Start", start);
                    m.SetFloat("_Decay", 0.5f);
                }, 0.8f);
            });
        }

        // ---------------- 道具 ----------------

        /// <summary>小節（と拍．1 始まり）の時刻．</summary>
        static double B(int bar, double beat = 1) => tempo.TimeOfBarBeat(bar, beat);

        static double Beats(double n) => n * 60.0 / tempo.Points[0].Bpm;

        static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1);

        static void Stars(Material m, float density, float brightness, float every, float depth)
        {
            m.SetColor("_ColorA", White);
            m.SetColor("_ColorB", LightYellow);
            m.SetFloat("_Density", density);
            m.SetFloat("_Brightness", brightness);
            m.SetFloat("_Every", every);
            m.SetFloat("_Depth", depth);
            m.SetFloat("_Variety", 1);
        }

        static void Clip(TrackAsset track, string template, string name, double start, double end, LedCanvas.Blend blend, float opacity,
                         double easeIn, double easeOut, Action<Material> setup)
        {
            var mat = new Material(EffectTemplatesBuilder.Template(template)) { name = name };
            setup(mat);
            AssetDatabase.CreateAsset(mat, $"{dir}/{++matCount:D2}_{Safe(name)}.mat");
            var clip = track.CreateClip<LedEffectClip>();
            clip.displayName = name;
            clip.start = start;
            clip.duration = Math.Max(1e-3, end - start);
            clip.easeInDuration = Math.Min(easeIn, clip.duration / 2);
            clip.easeOutDuration = Math.Min(easeOut, clip.duration - clip.easeInDuration);
            var asset = (LedEffectClip)clip.asset;
            asset.material = mat;
            asset.template.blend = blend;
            asset.template.opacity = opacity;
            EditorUtility.SetDirty(asset);
        }

        static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));

        /// <summary>2・4 拍目に短い光（全体の点滅は 1 秒に 1.3 回）．</summary>
        static void Backbeat(TrackAsset track, double from, double to, float opacity, Color color)
        {
            Clip(track, "BeatPulse", "2・4 拍目", from - Beats(1), to, LedCanvas.Blend.Add, opacity, Beats(1), 0, m =>
            {
                m.SetColor("_Color", color);
                m.SetFloat("_Every", 2);
                m.SetFloat("_Decay", 7);
            });
        }

        /// <summary>
        /// 曲の際立つアクセント（music.json）から選んでヒットを置く．際立ち（同じ拍の位置のいつもの音に対する強さ）が
        /// minSalience 以上のものを，強い順に，間を minGapBeats 拍以上あけて max 個まで．
        /// </summary>
        static void Accents(TrackAsset track, double from, double to, double minSalience, double minGapBeats, int max,
                            (double from, double to)[] skip, Func<Accent, (string template, Action<Material> setup, float opacity)> make)
        {
            var picked = new List<Accent>();
            foreach (var a in music.accents.Where(a => a.t >= from && a.t < to && a.salience >= minSalience && !skip.Any(r => a.t >= r.from - Beats(1) && a.t < r.to))
                                           .OrderByDescending(a => a.salience))
            {
                if (picked.Count >= max) break;
                if (picked.Any(p => Math.Abs(p.t - a.t) < Beats(minGapBeats))) continue;
                picked.Add(a);
            }
            foreach (var a in picked.OrderBy(a => a.t))
            {
                var (template, setup, opacity) = make(a);
                Clip(track, template, $"アクセント {a.bar}.{a.beat:0.#}", a.t, Math.Min(a.t + Beats(2), songEnd), LedCanvas.Blend.Add, opacity, 0, 0, setup);
            }
        }
    }
}
