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
    /// <summary>
    /// VR の中のホーム画面とスコアの表示（ViewerVR が VR を始めたときに作る）．
    ///
    /// ホーム画面: 目の前に浮かぶ板．左の ≡ で出す / 消す（始めは出ている）．右手の光線で指してトリガーで押すか，手の先でつつく．
    /// ボタン: 曲を始める，一時停止 / 再開，演出の切り替え，子どもの目の高さ / 自分の目の高さ，リングの中心に戻る，床，ゲームを終了（Unity の ■）．
    /// スコアの表示: 正面の上に浮かび，向いた方へゆっくりついてくる．スコア，Perfect / Great / Good / Miss の数，直前の判定と何 ms 早い / 遅いか．
    /// 日本語は Windows のフォント（Meiryo など）で書く．
    /// </summary>
    public sealed partial class VrMenu : MonoBehaviour
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
        Text hudScore, hudJudge, hudInfo, effectsLabel, viewLabel, pauseLabel, startLabel, playersLabel, countLabel;
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
        // テストプレイで叩いた時刻のずれ（ms，Perfect / Great / Good）．遅れの補正を合わせるのに使う
        readonly List<int> testOffsets = new List<int>();
        Text latencyLabel;
        Color lastJudgeColor = Color.white;
        float lastJudgeAt = -10;
        // むぎゅっとのすぐ近くに出す判定（Perfect / Great / Good / Miss と，何 ms 早い・遅い）．2026-11-03 ユーザー
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
            if (session != null && session.TestPlay && e.Result != JudgeResult.Miss) testOffsets.Add(ms);
            lastJudgeColor = e.Result == JudgeResult.Perfect ? new Color(0.4f, 1f, 0.5f) : e.Result == JudgeResult.Great ? new Color(1f, 0.9f, 0.3f) : e.Result == JudgeResult.Good ? new Color(1f, 0.6f, 0.2f) : new Color(1f, 0.4f, 0.4f);
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
            hudScore.text = judge != null ? $"スコア {judge.Score}　　Perfect {judge.PerfectCount}　Great {judge.GreatCount}　Good {judge.GoodCount}　Miss {judge.MissCount}" : "";
            float age = Time.unscaledTime - lastJudgeAt;
            hudJudge.text = age < 1.5f ? lastJudge : "";
            hudJudge.color = new Color(lastJudgeColor.r, lastJudgeColor.g, lastJudgeColor.b, Mathf.Clamp01(1.5f - age) + 0.2f);
            string section = conductor != null && conductor.IsStarted ? conductor.Data?.SectionAt(conductor.SongTime) : null;
            hudInfo.text = conductor != null && conductor.IsStarted
                ? $"{(session != null && session.TestPlay ? "テストプレイ　" : "")}{conductor.SongTime:F0} 秒　{section}　演出: {SongVisualSync.CurrentEffects}{(conductor.IsPaused ? "　（一時停止中）" : "")}"
                : "左の ≡ でホーム画面";
        }

        // 様子を 5 秒ごとにログに書き，スコアの下にも小さく出す（押せない・始まらないときに何が来ていないかを調べるため）
        void UpdateDiagnostics()
        {
            int controllers = 0;
            foreach (var d in InputSystem.devices)
                if (d is UnityEngine.InputSystem.XR.XRController) controllers++;
            string text = $"フレーム {Time.frameCount}　曲 {(conductor != null && conductor.IsStarted ? conductor.SongTime.ToString("F1") + " 秒" : "止まっている")}　" +
                          $"コントローラ {controllers}　手 {(ViewerVR.HandTrackingOn ? VrTouchInput.TrackedHands + " 本" : "使わない")}（{ViewerVR.RuntimeName}）　キーボード {(Keyboard.current != null ? "あり" : "なし")}　手前 {(Application.isFocused ? "はい" : "いいえ")}";
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
    }
}
