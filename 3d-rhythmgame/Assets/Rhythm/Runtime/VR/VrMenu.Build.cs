using System;
using System.Collections.Generic;
using Arte.Rhythm.Gameplay;
using Arte.Rhythm.Song;
using Arte.Rhythm.Led;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Arte.Rhythm.VR
{
    // ホーム画面とスコアの板を作る（VrMenu の一部．VrMenu.cs と同じクラス）
    partial class VrMenu
    {
        // ---------------- 作る ----------------

        void BuildMenu()
        {
            menu = NewCanvas("ホーム画面", new Vector2(1000, 860));
            var root = menu.transform;
            Label(root, "3D リズムゲーム", new Vector2(0, 380), 54, FontStyle.Bold);
            Label(root, "テープを流れる光が次のノーツの場所を示す（両側から流れ込む）．むぎゅっとの丸が曲の拍ごとに 紫の丸ごと → 青の 2/3 → 水色の 1/3 と欠けていく（3，2，1）．水色の光の粒が着き，むぎゅっと全体がパッと水色に光る瞬間に触れる",
                  new Vector2(0, 320), 24, FontStyle.Normal, new Vector2(940, 70));
            startLabel = Button(root, "曲を始める（最初から）", new Vector2(-245, 230), () => { StartSong(false); ShowMenu(false); });
            Button(root, "テストプレイ（正面で 3，2，1，はい）", new Vector2(245, 230), () => { StartSong(true); ShowMenu(false); });
            pauseLabel = Button(root, "一時停止", new Vector2(-245, 130), TogglePause);
            effectsLabel = Button(root, "演出を切り替える", new Vector2(245, 130), () => visualSync?.NextEffects());
            viewLabel = Button(root, "子どもの目の高さにする", new Vector2(-245, 30), () => vr?.SetChildView(!vr.ChildView));
            Button(root, "リングの中心に戻る", new Vector2(245, 30), () => vr?.Recenter());
            Button(root, "床を変える", new Vector2(-245, -70), () => vr?.NextFloor());
            playersLabel = Button(root, "人数", new Vector2(245, -70), () =>
            {
                PlaySettings.NextPlayers();
            });
            Button(root, "−10", new Vector2(-430, -170), () => VrLatency.Ms -= 10, 90);
            latencyLabel = Label(root, "", new Vector2(-245, -170), 22, FontStyle.Normal, new Vector2(270, 80));
            Button(root, "+10", new Vector2(-60, -170), () => VrLatency.Ms += 10, 90);
            Button(root, "テストプレイのずれに合わせる", new Vector2(-245, -250), () =>
            {
                int m = Median(testOffsets);
                if (testOffsets.Count == 0) return;
                VrLatency.Ms += m;
                testOffsets.Clear();
            });
            countLabel = Button(root, "数え方", new Vector2(245, -250), () =>
            {
                PlaySettings.NextCountBeats();
            });
            var quit = Button(root, "ゲームを終了する", new Vector2(245, -170), Quit);
            quit.color = new Color(1f, 0.7f, 0.7f);
            Label(root,
                "左の ≡: この画面を出す / 消す　　光線を向けてトリガー，手でつつく，または 1.5 秒見つめて押す\n" +
                "A: 子どもの目の高さ　B: 自分の目の高さ　X: 中心に戻る　Y: 床\n" +
                "左スティック: 歩く　右スティック: 向きを変える　右スティックを押す: 一時停止　左スティックを押す: 演出を切り替える",
                new Vector2(0, -350), 22, FontStyle.Normal, new Vector2(960, 110));
        }

        void StartSong(bool test)
        {
            if (test) testOffsets.Clear();
            if (session != null) session.StartSong(test);
            else conductor?.Play();
        }

        void BuildHud()
        {
            hud = NewCanvas("スコア", new Vector2(700, 230), new Color(0, 0, 0, 0.45f));
            hudJudge = Label(hud.transform, "", new Vector2(0, 55), 64, FontStyle.Bold, new Vector2(680, 90));
            hudScore = Label(hud.transform, "", new Vector2(0, -20), 30, FontStyle.Normal, new Vector2(680, 50));
            hudInfo = Label(hud.transform, "", new Vector2(0, -75), 22, FontStyle.Normal, new Vector2(680, 40));
            hudDiag = Label(hud.transform, "", new Vector2(0, -105), 16, FontStyle.Normal, new Vector2(680, 26));
            hudDiag.color = new Color(0.6f, 0.6f, 0.65f);
        }

        Canvas NewCanvas(string name, Vector2 size, Color? background = null)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            go.transform.localScale = Vector3.one * 0.001f;     // 1 = 1mm
            var bg = new GameObject("背景", typeof(Image)).GetComponent<Image>();
            bg.transform.SetParent(go.transform, false);
            ((RectTransform)bg.transform).sizeDelta = size;
            bg.color = background ?? Panel;
            return c;
        }

        Text Label(Transform parent, string text, Vector2 pos, int size, FontStyle style, Vector2? box = null)
        {
            var t = new GameObject("文字", typeof(Text)).GetComponent<Text>();
            t.transform.SetParent(parent, false);
            var rt = (RectTransform)t.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box ?? new Vector2(900, 70);
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = TextColor;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static int Median(List<int> v)
        {
            if (v.Count == 0) return 0;
            var s = new List<int>(v);
            s.Sort();
            return s[s.Count / 2];
        }

        Text Button(Transform parent, string text, Vector2 pos, Action action, float width = 460)
        {
            var img = new GameObject("ボタン", typeof(Image)).GetComponent<Image>();
            img.transform.SetParent(parent, false);
            var rt = (RectTransform)img.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, 80);
            img.color = ButtonColor;
            buttons.Add((rt, img, action));
            return Label(img.transform, text, Vector2.zero, 30, FontStyle.Normal, new Vector2(width - 20, 76));
        }
    }
}
