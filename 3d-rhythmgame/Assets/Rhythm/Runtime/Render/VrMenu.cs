using System;
using System.Collections.Generic;
using Arte.Rhythm.Gameplay;
using Arte.Rhythm.Song;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Arte.Rhythm.Render
{
    /// <summary>
    /// VR の中のホーム画面とスコアの表示（ViewerVR が VR を始めたときに作る）．
    ///
    /// ホーム画面: 目の前に浮かぶ板．左の ≡ で出す / 消す（始めは出ている）．右手の光線で指してトリガーで押すか，手の先でつつく．
    /// ボタン: 曲を始める，一時停止 / 再開，演出の切り替え，子どもの目の高さ / 自分の目の高さ，リングの中心に戻る，床，ゲームを終了（Unity の ■）．
    /// スコアの表示: 正面の上に浮かび，向いた方へゆっくりついてくる．スコア，Perfect / Good / Miss の数，直前の判定と何 ms 早い / 遅いか．
    /// 日本語は Windows のフォント（Meiryo など）で書く．
    /// </summary>
    public sealed class VrMenu : MonoBehaviour
    {
        public ViewerVR vr;
        public Camera head;
        public Transform rig;
        public Material lineMaterial;

        SongConductor conductor;
        GameSession session;
        SongVisualSync visualSync;
        Font font;

        Canvas menu, hud;
        Text hudScore, hudJudge, hudInfo, effectsLabel, viewLabel, pauseLabel, startLabel;
        readonly List<(RectTransform rect, Image image, Action action)> buttons = new List<(RectTransform, Image, Action)>();
        LineRenderer laser;
        InputAction toggleAction, clickAction, aimPos, aimRot;
        readonly List<InputAction> actions = new List<InputAction>();
        int hovered = -1;
        float pokeCooldown;
        string lastJudge = "";
        Color lastJudgeColor = Color.white;
        float lastJudgeAt = -10;

        static readonly Color Panel = new Color(0.05f, 0.06f, 0.1f, 0.92f);
        static readonly Color ButtonColor = new Color(0.16f, 0.2f, 0.32f, 1f);
        static readonly Color ButtonHover = new Color(0.3f, 0.42f, 0.7f, 1f);
        static readonly Color TextColor = new Color(0.85f, 0.85f, 0.9f, 1f);

        /// <summary>手の先の点（VrTouchInput が毎フレーム入れる．ワールド座標）．つついて押すのに使う．</summary>
        public static readonly List<Vector3> PokePoints = new List<Vector3>();

        public bool MenuVisible => menu != null && menu.gameObject.activeSelf;

        void Start()
        {
            conductor = FindFirstObjectByType<SongConductor>();
            session = FindFirstObjectByType<GameSession>();
            visualSync = FindFirstObjectByType<SongVisualSync>();
            font = Font.CreateDynamicFontFromOSFont(new[] { "Meiryo", "Yu Gothic UI", "MS Gothic", "Arial" }, 40);
            BuildMenu();
            BuildHud();
            laser = new GameObject("光線").AddComponent<LineRenderer>();
            laser.positionCount = 2;
            laser.startWidth = laser.endWidth = 0.003f;
            if (lineMaterial != null) laser.sharedMaterial = lineMaterial;
            laser.startColor = laser.endColor = new Color(0.6f, 0.8f, 1f);
            toggleAction = NewAction("<XRController>{LeftHand}/menu");
            clickAction = NewAction("<XRController>{RightHand}/triggerPressed");
            aimPos = NewAction("<XRController>{RightHand}/pointerPosition", "Vector3");
            aimRot = NewAction("<XRController>{RightHand}/pointerRotation", "Quaternion");
            if (session != null) session.Judged += OnJudged;
            ShowMenu(true);
        }

        InputAction NewAction(string binding, string control = null)
        {
            var a = new InputAction(type: control == null ? InputActionType.Button : InputActionType.Value, binding: binding, expectedControlType: control);
            a.Enable();
            actions.Add(a);
            return a;
        }

        void OnDestroy()
        {
            if (session != null) session.Judged -= OnJudged;
            foreach (var a in actions) a.Dispose();
        }

        // ---------------- 作る ----------------

        void BuildMenu()
        {
            menu = NewCanvas("ホーム画面", new Vector2(1000, 760));
            var root = menu.transform;
            Label(root, "3D リズムゲーム", new Vector2(0, 330), 54, FontStyle.Bold);
            Label(root, "光ったむぎゅっとに，点滅が「点く」瞬間に触れる（3 秒前から 0.5 秒ごとに青く点滅．7 回目が点く瞬間が Perfect）",
                  new Vector2(0, 270), 24, FontStyle.Normal, new Vector2(940, 70));
            startLabel = Button(root, "曲を始める（最初から）", new Vector2(-245, 180), () => { conductor?.Play(); ShowMenu(false); });
            pauseLabel = Button(root, "一時停止", new Vector2(245, 180), TogglePause);
            effectsLabel = Button(root, "演出を切り替える", new Vector2(-245, 80), () => visualSync?.NextEffects());
            viewLabel = Button(root, "子どもの目の高さにする", new Vector2(245, 80), () => vr?.SetChildView(!vr.ChildView));
            Button(root, "リングの中心に戻る", new Vector2(-245, -20), () => vr?.Recenter());
            Button(root, "床を変える", new Vector2(245, -20), () => vr?.NextFloor());
            var quit = Button(root, "ゲームを終了する", new Vector2(0, -120), Quit);
            quit.color = new Color(1f, 0.7f, 0.7f);
            Label(root,
                "左の ≡: この画面を出す / 消す　　右の光線を向けてトリガー，または手でつついて押す\n" +
                "A: 子どもの目の高さ　B: 自分の目の高さ　X: 中心に戻る　Y: 床\n" +
                "左スティック: 歩く　右スティック: 向きを変える　右スティックを押す: 一時停止　左スティックを押す: 演出を切り替える",
                new Vector2(0, -260), 22, FontStyle.Normal, new Vector2(960, 150));
        }

        void BuildHud()
        {
            hud = NewCanvas("スコア", new Vector2(700, 230), new Color(0, 0, 0, 0.45f));
            hudJudge = Label(hud.transform, "", new Vector2(0, 55), 64, FontStyle.Bold, new Vector2(680, 90));
            hudScore = Label(hud.transform, "", new Vector2(0, -20), 30, FontStyle.Normal, new Vector2(680, 50));
            hudInfo = Label(hud.transform, "", new Vector2(0, -75), 22, FontStyle.Normal, new Vector2(680, 40));
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

        Text Button(Transform parent, string text, Vector2 pos, Action action)
        {
            var img = new GameObject("ボタン", typeof(Image)).GetComponent<Image>();
            img.transform.SetParent(parent, false);
            var rt = (RectTransform)img.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(460, 80);
            img.color = ButtonColor;
            buttons.Add((rt, img, action));
            return Label(img.transform, text, Vector2.zero, 30, FontStyle.Normal, new Vector2(440, 76));
        }

        // ---------------- 動かす ----------------

        void ShowMenu(bool show)
        {
            menu.gameObject.SetActive(show);
            if (!show || head == null) return;
            // 目の前 0.9m，目より少し下に，こちらを向けて置く
            var fwd = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            menu.transform.position = head.transform.position + fwd * 0.9f + Vector3.down * 0.15f;
            menu.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }

        void TogglePause()
        {
            if (conductor == null || !conductor.IsStarted) return;
            if (conductor.IsPaused) conductor.Resume();
            else conductor.Pause();
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnJudged(JudgeEvent e)
        {
            int ms = (int)Math.Round(e.Offset * 1000);
            string timing = e.Result == JudgeResult.Miss ? "" : ms == 0 ? "（ぴったり）" : ms < 0 ? $"（{-ms}ms 早い）" : $"（{ms}ms 遅い）";
            lastJudge = e.Result + timing;
            lastJudgeColor = e.Result == JudgeResult.Perfect ? new Color(0.4f, 1f, 0.5f) : e.Result == JudgeResult.Good ? new Color(1f, 0.9f, 0.3f) : new Color(1f, 0.4f, 0.4f);
            lastJudgeAt = Time.unscaledTime;
        }

        void Update()
        {
            if (head == null) return;
            if (toggleAction.WasPressedThisFrame()) ShowMenu(!MenuVisible);
            UpdateHud();
            if (MenuVisible) UpdateMenu();
            else laser.enabled = false;
        }

        void UpdateHud()
        {
            // 正面の上に浮かび，向いた方へゆっくりついてくる
            var fwd = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude > 1e-4f)
            {
                var target = head.transform.position + fwd * 1.6f + Vector3.up * 0.45f;
                var t = hud.transform;
                t.position = Vector3.Lerp(t.position, target, 1 - Mathf.Exp(-3f * Time.unscaledDeltaTime));
                var look = t.position - head.transform.position;
                look.y = 0;
                if (look.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }
            var judge = session != null ? session.Judge : null;
            hudScore.text = judge != null ? $"スコア {judge.Score}　　Perfect {judge.PerfectCount}　Good {judge.GoodCount}　Miss {judge.MissCount}" : "";
            float age = Time.unscaledTime - lastJudgeAt;
            hudJudge.text = age < 1.5f ? lastJudge : "";
            hudJudge.color = new Color(lastJudgeColor.r, lastJudgeColor.g, lastJudgeColor.b, Mathf.Clamp01(1.5f - age) + 0.2f);
            string section = conductor != null && conductor.IsStarted ? conductor.Data?.SectionAt(conductor.SongTime) : null;
            hudInfo.text = conductor != null && conductor.IsStarted
                ? $"{conductor.SongTime:F0} 秒　{section}　演出: {SongVisualSync.CurrentEffects}{(conductor.IsPaused ? "　（一時停止中）" : "")}"
                : "左の ≡ でホーム画面";
        }

        void UpdateMenu()
        {
            pauseLabel.text = conductor != null && conductor.IsPaused ? "再開" : "一時停止";
            effectsLabel.text = $"演出を切り替える（今: {SongVisualSync.CurrentEffects}）";
            viewLabel.text = vr != null && vr.ChildView ? "自分の目の高さにする" : "子どもの目の高さにする";
            startLabel.text = conductor != null && conductor.IsStarted ? "曲を最初から" : "曲を始める";

            // 右手の光線
            hovered = -1;
            var origin = rig != null ? rig.TransformPoint(aimPos.ReadValue<Vector3>()) : Vector3.zero;
            var q = aimRot.ReadValue<Quaternion>();
            if (q.x == 0 && q.y == 0 && q.z == 0 && q.w == 0) q = Quaternion.identity;
            var dir = (rig != null ? rig.rotation : Quaternion.identity) * q * Vector3.forward;
            var plane = new Plane(-menu.transform.forward, menu.transform.position);
            Vector3 end = origin + dir * 3f;
            if (rig != null && plane.Raycast(new Ray(origin, dir), out float dist) && dist < 3f)
            {
                end = origin + dir * dist;
                hovered = ButtonAt(end);
            }
            laser.enabled = rig != null;
            laser.SetPosition(0, origin);
            laser.SetPosition(1, end);
            for (int i = 0; i < buttons.Count; i++) buttons[i].image.color = i == hovered ? ButtonHover : ButtonColor;
            if (hovered >= 0 && clickAction.WasPressedThisFrame()) buttons[hovered].action();

            // 手の先でつつく（板から 2cm 以内に入ったら押す．続けて押さないよう 0.5 秒あける）
            pokeCooldown -= Time.unscaledDeltaTime;
            if (pokeCooldown > 0) return;
            foreach (var p in PokePoints)
            {
                float d = plane.GetDistanceToPoint(p);
                if (Mathf.Abs(d) > 0.02f) continue;
                int b = ButtonAt(p);
                if (b < 0) continue;
                buttons[b].action();
                pokeCooldown = 0.5f;
                break;
            }
        }

        int ButtonAt(Vector3 world)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                var rt = buttons[i].rect;
                Vector2 local = rt.InverseTransformPoint(world);
                if (rt.rect.Contains(local)) return i;
            }
            return -1;
        }
    }
}
