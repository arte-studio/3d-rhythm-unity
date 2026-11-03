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
        Text hudScore, hudJudge, hudInfo, effectsLabel, viewLabel, pauseLabel, startLabel, playersLabel;
        readonly List<(RectTransform rect, Image image, Action action)> buttons = new List<(RectTransform, Image, Action)>();
        InputAction toggleAction;

        // 光線を出す手（右と左．どちらのトリガーでも押せる）
        sealed class Pointer
        {
            public InputAction aimPos, aimRot, gripPos, gripRot, trigger;
            public LineRenderer laser;
            public bool wasDown;
        }
        readonly List<Pointer> pointers = new List<Pointer>();
        bool placed, loggedHover;
        int gazeButton = -1;
        float gazeSince, diagAt;
        Text hudDiag;
        const float GazeSec = 1.5f;
        int lastFound = -1;
        float shownAt, settledAt = -1;
        readonly List<InputAction> actions = new List<InputAction>();
        int hovered = -1;
        float pokeCooldown;
        string lastJudge = "";
        Color lastJudgeColor = Color.white;
        float lastJudgeAt = -10;
        // むぎゅっとのすぐ近くに出す判定（Perfect / Good / Miss と，何 ms 早い・遅い）．2026-11-03 ユーザー
        LedPreview preview;
        readonly List<(Canvas canvas, Text text, Vector3 at, float born)> popups = new List<(Canvas, Text, Vector3, float)>();
        const float PopupSec = 1.2f;

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
            preview = FindFirstObjectByType<LedPreview>();
            font = Font.CreateDynamicFontFromOSFont(new[] { "Meiryo", "Yu Gothic UI", "MS Gothic", "Arial" }, 40);
            BuildMenu();
            BuildHud();
            toggleAction = NewAction("<XRController>{LeftHand}/menu");
            foreach (var hand in new[] { "RightHand", "LeftHand" })
            {
                var laser = new GameObject($"光線（{hand}）").AddComponent<LineRenderer>();
                laser.positionCount = 2;
                laser.startWidth = laser.endWidth = 0.004f;
                if (lineMaterial != null) laser.sharedMaterial = new Material(lineMaterial) { color = new Color(0.6f, 0.8f, 1f) };
                laser.enabled = false;
                pointers.Add(new Pointer
                {
                    aimPos = NewAction($"<XRController>{{{hand}}}/pointerPosition", "Vector3"),
                    aimRot = NewAction($"<XRController>{{{hand}}}/pointerRotation", "Quaternion"),
                    gripPos = NewAction($"<XRController>{{{hand}}}/devicePosition", "Vector3"),
                    gripRot = NewAction($"<XRController>{{{hand}}}/deviceRotation", "Quaternion"),
                    trigger = NewAction($"<XRController>{{{hand}}}/trigger", "Axis"),
                    laser = laser,
                });
            }
            if (session != null) session.Judged += OnJudged;
            menu.gameObject.SetActive(true);      // 置く場所は頭の位置が取れてから（UpdateMenu）
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
            Label(root, "テープを流れる光が次のノーツの場所を示す（両側から流れ込む）．むぎゅっとの丸が曲の拍ごとに 紫の丸ごと → 青の 2/3 → 水色の 1/3 と欠けていく（3，2，1）．水色の光の粒が着き，むぎゅっと全体がパッと水色に光る瞬間に触れる",
                  new Vector2(0, 270), 24, FontStyle.Normal, new Vector2(940, 70));
            startLabel = Button(root, "曲を始める（最初から）", new Vector2(-245, 180), () => { conductor?.Play(); ShowMenu(false); });
            pauseLabel = Button(root, "一時停止", new Vector2(245, 180), TogglePause);
            effectsLabel = Button(root, "演出を切り替える", new Vector2(-245, 80), () => visualSync?.NextEffects());
            viewLabel = Button(root, "子どもの目の高さにする", new Vector2(245, 80), () => vr?.SetChildView(!vr.ChildView));
            Button(root, "リングの中心に戻る", new Vector2(-245, -20), () => vr?.Recenter());
            Button(root, "床を変える", new Vector2(245, -20), () => vr?.NextFloor());
            playersLabel = Button(root, "人数", new Vector2(-245, -120), () =>
            {
                if (session != null) session.NextPlayers();
            });
            var quit = Button(root, "ゲームを終了する", new Vector2(245, -120), Quit);
            quit.color = new Color(1f, 0.7f, 0.7f);
            Label(root,
                "左の ≡: この画面を出す / 消す　　光線を向けてトリガー，手でつつく，または 1.5 秒見つめて押す\n" +
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

        /// <summary>出ていれば，今の頭の正面に置き直す（ViewerVR が中心に戻したとき・目の高さを変えたとき）．</summary>
        public void Replace()
        {
            if (MenuVisible && placed) ShowMenu(true);
        }

        void ShowMenu(bool show)
        {
            menu.gameObject.SetActive(show);
            shownAt = Time.unscaledTime;
            if (!show || head == null) return;
            placed = true;
            // 目の前 0.9m，目より少し下に，こちらを向けて置く
            var fwd = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            menu.transform.position = head.transform.position + fwd * 0.9f + Vector3.down * 0.15f;
            menu.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            Debug.Log($"[VrMenu] ホーム画面を置いた（頭 {head.transform.position}，画面 {menu.transform.position}）");
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
            ShowPopup(e.Lane, e.Result + (timing.Length > 0 ? "\n" + timing.Trim('（', '）') : ""), lastJudgeColor);
        }

        // むぎゅっとの手前（リングの中心の側）少し上に，こちらを向けて出す
        void ShowPopup(int lane, string text, Color color)
        {
            if (preview == null || preview.Notes == null || !preview.Notes.TryGetPlacement(lane, out var p)) return;
            var mugyu = preview.transform.TransformPoint(new Vector3(p.mugyu[0], p.mugyu[1], p.mugyu[2]));
            var toCenter = preview.transform.TransformPoint(new Vector3(0, p.mugyu[1], 0)) - mugyu;
            var at = mugyu + toCenter.normalized * 0.12f + Vector3.up * 0.1f;
            var c = NewCanvas("判定", new Vector2(360, 140), Color.clear);
            var t = Label(c.transform, text, Vector2.zero, 44, FontStyle.Bold, new Vector2(360, 140));
            t.color = color;
            popups.Add((c, t, at, Time.unscaledTime));
        }

        void UpdatePopups()
        {
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var (c, t, at, born) = popups[i];
                float age = Time.unscaledTime - born;
                if (age > PopupSec || c == null)
                {
                    if (c != null) Destroy(c.gameObject);
                    popups.RemoveAt(i);
                    continue;
                }
                c.transform.position = at + Vector3.up * (0.04f * age);     // 少しずつ上へ
                var look = c.transform.position - head.transform.position;
                if (look.sqrMagnitude > 1e-4f) c.transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
                var col = t.color;
                col.a = Mathf.Clamp01((PopupSec - age) / 0.4f);
                t.color = col;
            }
        }

        void Update()
        {
            if (head == null) return;
            if (toggleAction.WasPressedThisFrame()) ShowMenu(!MenuVisible);
            UpdateHud();
            UpdatePopups();
            UpdateDiagnostics();
            if (MenuVisible) UpdateMenu();
            else foreach (var p in pointers) p.laser.enabled = false;
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

        // 様子を 5 秒ごとにログに書き，スコアの下にも小さく出す（押せない・始まらないときに何が来ていないかを調べるため）
        void UpdateDiagnostics()
        {
            int controllers = 0;
            foreach (var d in InputSystem.devices)
                if (d is UnityEngine.InputSystem.XR.XRController) controllers++;
            string text = $"フレーム {Time.frameCount}　曲 {(conductor != null && conductor.IsStarted ? conductor.SongTime.ToString("F1") + " 秒" : "止まっている")}　" +
                          $"コントローラ {controllers}　手 {(ViewerVR.HandTrackingOn ? Gameplay.VrTouchInput.TrackedHands + " 本" : "使わない")}（{ViewerVR.RuntimeName}）　キーボード {(Keyboard.current != null ? "あり" : "なし")}　手前 {(Application.isFocused ? "はい" : "いいえ")}";
            hudDiag.text = text;
            if (Time.unscaledTime < diagAt) return;
            diagAt = Time.unscaledTime + 5f;
            var names = new List<string>();
            foreach (var d in InputSystem.devices) names.Add($"{d.layout}{(d.enabled ? "" : "（止まっている）")}");
            // その手前の XR の層（ランタイムから来ている機器）．Input System に出ないとき，ここに来ているかで原因を分ける
            var xr = new List<UnityEngine.XR.InputDevice>();
            UnityEngine.XR.InputDevices.GetDevices(xr);
            var xrNames = new List<string>();
            foreach (var d in xr) xrNames.Add($"{d.name}［{d.characteristics}］");
            Debug.Log($"[VrMenu] {text}　ホーム画面 {(MenuVisible ? "出ている" : "消えている")}　入力の機器: {string.Join("，", names)}　XR の機器: {string.Join("，", xrNames)}");
        }

        void UpdateMenu()
        {
            pauseLabel.text = conductor != null && conductor.IsPaused ? "再開" : "一時停止";
            effectsLabel.text = $"演出を切り替える（今: {SongVisualSync.CurrentEffects}）";
            viewLabel.text = vr != null && vr.ChildView ? "自分の目の高さにする" : "子どもの目の高さにする";
            startLabel.text = conductor != null && conductor.IsStarted ? "曲を最初から" : "曲を始める";
            playersLabel.text = session != null ? $"人数: {session.players} 人（曲を始めると変わる）" : "人数";

            // 頭の位置が取れて落ち着いてから目の前に置く．VR を始めた直後は頭の位置がまだ 0 だったり，
            // ViewerVR が中心に戻す・目の高さを合わせる前だったりして，最初のホーム画面だけ下の方に出ていた（2026-11-03）．
            // 左の ≡ で出したときと同じ置き方（ShowMenu）にそろえる
            if (!placed)
            {
                bool tracked = head.transform.localPosition.y > 0.3f;
                if (!tracked) settledAt = -1;
                else if (settledAt < 0) settledAt = Time.unscaledTime;
                bool settled = tracked && Time.unscaledTime - settledAt > 1f;
                if (!settled && Time.unscaledTime - shownAt < 6f) return;
                ShowMenu(true);
            }
            // 目の高さから大きく外れた（床の近くに出た・頭の位置があとから取れた）ら置き直す．
            // 起動直後に下の方に出て，上の段のボタン 2 つだけが見えていた（2026-11-03）
            else
            {
                var d = menu.transform.position - head.transform.position;
                float dy = d.y + 0.15f;
                d.y = 0;
                if (Mathf.Abs(dy) > 0.35f || d.magnitude > 2.5f) ShowMenu(true);
            }
            // コントローラが見つかった / 外れたらログに出す（押せないときに入力が来ているかを確かめるため）
            int found = 0;
            foreach (var p in pointers) found += p.aimPos.controls.Count + p.gripPos.controls.Count + p.trigger.controls.Count;
            if (found != lastFound)
            {
                lastFound = found;
                foreach (var p in pointers)
                    Debug.Log($"[VrMenu] 入力: 指す位置 {p.aimPos.controls.Count}，握る位置 {p.gripPos.controls.Count}，トリガー {p.trigger.controls.Count}（{p.trigger.bindings[0].path}）");
            }

            // 光線（左右どちらでも．指す向きが取れなければ握る位置から）．トリガーを半分くらい握ったら押す
            hovered = -1;
            var plane = new Plane(-menu.transform.forward, menu.transform.position);
            foreach (var p in pointers)
            {
                bool aim = p.aimPos.controls.Count > 0;
                var local = aim ? p.aimPos.ReadValue<Vector3>() : p.gripPos.ReadValue<Vector3>();
                var q = aim ? p.aimRot.ReadValue<Quaternion>() : p.gripRot.ReadValue<Quaternion>();
                if (q.x == 0 && q.y == 0 && q.z == 0 && q.w == 0)
                {
                    p.laser.enabled = false;
                    continue;
                }
                var origin = rig != null ? rig.TransformPoint(local) : local;
                var dir = (rig != null ? rig.rotation : Quaternion.identity) * q * Vector3.forward;
                Vector3 end = origin + dir * 2f;
                int hit = -1;
                if (plane.Raycast(new Ray(origin, dir), out float dist) && dist < 3f)
                {
                    end = origin + dir * dist;
                    hit = ButtonAt(end);
                }
                p.laser.enabled = true;
                p.laser.SetPosition(0, origin);
                p.laser.SetPosition(1, end);
                bool down = p.trigger.ReadValue<float>() > 0.5f;
                if (hit >= 0)
                {
                    hovered = hit;
                    if (!loggedHover)
                    {
                        loggedHover = true;
                        Debug.Log("[VrMenu] 光線がボタンに当たった");
                    }
                    if (down && !p.wasDown)
                    {
                        Debug.Log($"[VrMenu] ボタンを押した（{hit}）");
                        p.wasDown = true;
                        buttons[hit].action();
                        return;
                    }
                }
                p.wasDown = down;
            }
            for (int i = 0; i < buttons.Count; i++) buttons[i].image.color = i == hovered ? ButtonHover : ButtonColor;

            // 見つめて押す（コントローラも手も使えないとき．ボタンを 1.5 秒見つめると押す．押すまでの間はボタンの色が変わっていく）
            int gaze = -1;
            var eye = new Ray(head.transform.position, head.transform.forward);
            if (plane.Raycast(eye, out float gd) && gd < 3f) gaze = ButtonAt(eye.GetPoint(gd));
            if (gaze != gazeButton)
            {
                gazeButton = gaze;
                gazeSince = Time.unscaledTime;
            }
            if (gaze >= 0)
            {
                float t = (Time.unscaledTime - gazeSince) / GazeSec;
                if (gaze != hovered) buttons[gaze].image.color = Color.Lerp(ButtonColor, new Color(0.35f, 0.6f, 0.4f), Mathf.Clamp01(t));
                if (t >= 1)
                {
                    Debug.Log($"[VrMenu] 見つめて押した（{gaze}）");
                    gazeSince = Time.unscaledTime + 1f;     // 続けて押さない
                    buttons[gaze].action();
                    return;
                }
            }

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
