using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Gameplay;
using Arte.Rhythm.Hardware;
using Arte.Rhythm.Song;
using UnityEditor;
using UnityEngine;

namespace Arte.Rhythm.Editor
{
    // 描く（ChartEditorWindow の一部．ChartEditorWindow.cs と同じクラス）
    partial class ChartEditorWindow
    {
        // ---- 描画と入力 ----

        void OnGUI()
        {
            if (pendingPopup != null && Event.current.type == EventType.Repaint)
            {
                var show = pendingPopup;
                pendingPopup = null;
                show();
            }
            DrawToolbar();
            if (data == null)
            {
                EditorGUILayout.HelpBox("SongAsset を選ぶか，Project ビューで SongAsset をダブルクリックしてください．\n" +
                                        "旧版の譜面からは Tools/Arte Rhythm/Import Legacy Chart で作れます．", MessageType.Info);
                return;
            }

            area = new Rect(0, ToolbarH, position.width, position.height - ToolbarH - 18);
            HandleInput();

            // 再生中は再生位置が画面の 1/3 に来るようにスクロールする
            double ph = PlayheadSec;
            if (!double.IsNaN(ph))
            {
                double view = (area.width - Gutter) / pxPerSec;
                if (ph < scrollSec || ph > scrollSec + view * 0.8) scrollSec = ph - view / 3;
            }

            EditorGUI.DrawRect(area, new Color(0.13f, 0.13f, 0.14f));
            DrawLanes();
            DrawGrid();
            DrawWaveform();
            DrawSections();
            DrawJudgeWindows();
            DrawNotes();
            DrawCursor(cursorSec, new Color(1f, 1f, 1f, 0.6f));
            if (!double.IsNaN(ph)) DrawCursor(ph, new Color(1f, 0.35f, 0.25f));
            DrawStatus();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var s = (SongAsset)EditorGUILayout.ObjectField(song, typeof(SongAsset), false, GUILayout.Width(200));
                if (s != song) SetSong(s);

                using (new EditorGUI.DisabledScope(!EditorAudio.Available || song?.audio == null))
                {
                    if (GUILayout.Button(EditorAudio.IsPlaying ? "■ 停止" : "▶ 再生", EditorStyles.toolbarButton, GUILayout.Width(60))) TogglePlay();
                }
                GUILayout.Label("グリッド", GUILayout.Width(44));
                divisionIndex = EditorGUILayout.Popup(divisionIndex, DivisionLabels, EditorStyles.toolbarPopup, GUILayout.Width(52));
                snap = GUILayout.Toggle(snap, "スナップ", EditorStyles.toolbarButton, GUILayout.Width(56));
                bool m = GUILayout.Toggle(metronome, new GUIContent("メトロノーム", "再生中に拍でクリック音を鳴らす（M キー）．小節の頭は高い音"), EditorStyles.toolbarButton, GUILayout.Width(80));
                if (m != metronome) SetMetronome(m);
                GUILayout.Label("設営", GUILayout.Width(28));
                int si = EditorGUILayout.Popup(setupIndex, setupNames, EditorStyles.toolbarPopup, GUILayout.Width(150));
                if (si != setupIndex) { setupIndex = si; UpdateWiredLanes(); }
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(data == null))
                {
                    if (GUILayout.Button(dirty ? "保存 *" : "保存", EditorStyles.toolbarButton, GUILayout.Width(56))) Save();
                }
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (data == null) return;
                var p = data.Tempo.Count > 0 ? data.Tempo[0] : null;
                GUILayout.Label(p == null ? "テンポ未設定（120 BPM で表示中．再生中に B キーで拍をタップ，1 拍目は N）" : "テンポ", GUILayout.Width(p == null ? 330 : 40));
                EditorGUI.BeginChangeCheck();
                float labelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 32;
                double bpm = EditorGUILayout.DoubleField("BPM", p?.Bpm ?? 120, GUILayout.Width(100));
                EditorGUIUtility.labelWidth = 70;
                double first = EditorGUILayout.DoubleField("1小節目(秒)", p?.TimeSec ?? 0, GUILayout.Width(140));
                EditorGUIUtility.labelWidth = 32;
                int bpb = EditorGUILayout.IntField("拍子", p?.BeatsPerBar ?? 4, GUILayout.Width(70));
                EditorGUIUtility.labelWidth = labelWidth;
                if (EditorGUI.EndChangeCheck() && bpm > 0 && bpb > 0) SetTempo(bpm, first, bpb);
                if (data.Tempo.Count > 1) GUILayout.Label($"（ほかに {data.Tempo.Count - 1} 箇所でテンポが変わる）");
                if (GUILayout.Button(new GUIContent("テンポ表…", "途中でテンポや拍子が変わる曲のときに使う"), EditorStyles.toolbarButton, GUILayout.Width(70)))
                    TempoTableWindow.Open(this);
                GUILayout.FlexibleSpace();
                tapLatencyMs = EditorGUILayout.FloatField(new GUIContent("タップ補正(ms)", "キー入力で置くノーツを何ミリ秒早めるか"), tapLatencyMs, GUILayout.Width(160));
                pxPerSec = EditorGUILayout.Slider(pxPerSec, 20, 800, GUILayout.Width(160));
            }
        }

        void DrawSections()
        {
            if (sections.Count == 0) return;
            var r = new Rect(area.x + Gutter, area.y + RulerH, area.width - Gutter, WaveH);
            float lanesBottom = Mathf.Min(area.yMax, YOfLane(LaneCount));
            var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.white } };
            for (int i = 0; i < sections.Count; i++)
            {
                var s = sections[i];
                double end = i + 1 < sections.Count ? sections[i + 1].Time : SongLength;
                float x0 = Mathf.Max(r.x, XOf(s.Time)), x1 = Mathf.Min(r.xMax, XOf(end));
                if (x1 <= r.x || x0 >= r.xMax) continue;
                var c = SectionColors[i % SectionColors.Length];
                EditorGUI.DrawRect(new Rect(x0, r.y, x1 - x0, r.height), new Color(c.r, c.g, c.b, 0.16f));
                EditorGUI.DrawRect(new Rect(x0, LanesTop, x1 - x0, lanesBottom - LanesTop), new Color(c.r, c.g, c.b, 0.035f));
                float bx = XOf(s.Time);
                if (bx >= r.x)
                {
                    bool hot = i == draggingSection || (draggingSection < 0 && Mathf.Abs(Event.current.mousePosition.x - bx) < 6 && r.Contains(Event.current.mousePosition));
                    EditorGUI.DrawRect(new Rect(bx - 1, r.y, hot ? 3 : 2, lanesBottom - r.y), new Color(c.r, c.g, c.b, hot ? 1f : 0.7f));
                }
                var label = new Rect(x0 + 4, r.y + 1, Mathf.Max(0, x1 - x0 - 6), 16);
                EditorGUI.DrawRect(new Rect(label.x - 2, label.y, Mathf.Min(label.width + 4, style.CalcSize(new GUIContent(s.Name)).x + 4), 15), new Color(0, 0, 0, 0.55f));
                GUI.Label(label, s.Name, style);
            }
        }

        void DrawLanes()
        {
            int count = LaneCount;
            var label = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            for (int lane = 0; lane < count; lane++)
            {
                float y = YOfLane(lane);
                if (y > area.yMax) break;
                bool wired = wiredLanes == null || wiredLanes.Contains(lane);
                var bg = lane % 2 == 0 ? new Color(0.17f, 0.17f, 0.18f) : new Color(0.15f, 0.15f, 0.16f);
                if (!wired) bg = new Color(0.22f, 0.12f, 0.12f);
                EditorGUI.DrawRect(new Rect(area.x + Gutter, y, area.width - Gutter, LaneH - 1), bg);
                EditorGUI.DrawRect(new Rect(area.x, y, Gutter - 2, LaneH - 1), new Color(0.2f, 0.2f, 0.22f));
                // 画面上のレーン番号は1始まり．キーボードの数字と同じ
                GUI.Label(new Rect(area.x, y, Gutter - 2, LaneH - 1), wired ? $"{Chart.LaneLabel(lane)}" : $"{Chart.LaneLabel(lane)}×", label);
            }
        }

        void DrawGrid()
        {
            double from = TimeOf(area.x + Gutter), to = TimeOf(area.xMax);
            float top = area.y, bottom = Mathf.Min(area.yMax, YOfLane(LaneCount));
            var barLabel = EditorStyles.miniLabel;
            int division = pxPerSec * 60 / CurrentBpmAt(from) / Division < 6 ? 1 : Division; // 細かすぎるときは拍だけ
            foreach (var (sec, isBeat, isBar) in tempo.GridLines(from, to, division))
            {
                float x = XOf(sec);
                var c = isBar ? new Color(1, 1, 1, 0.35f) : isBeat ? new Color(1, 1, 1, 0.14f) : new Color(1, 1, 1, 0.05f);
                EditorGUI.DrawRect(new Rect(x, top, 1, bottom - top), c);
                if (isBar) GUI.Label(new Rect(x + 2, top, 60, RulerH), $"{tempo.BarBeatOf(sec + 1e-6).bar}", barLabel);
            }
            // 曲頭と曲の終わり
            EditorGUI.DrawRect(new Rect(XOf(0), top, 2, bottom - top), new Color(0.3f, 0.8f, 0.4f, 0.8f));
            EditorGUI.DrawRect(new Rect(XOf(SongLength), top, 2, bottom - top), new Color(0.8f, 0.3f, 0.3f, 0.8f));
            EditorGUI.DrawRect(new Rect(area.x, area.y + RulerH - 1, area.width, 1), new Color(0, 0, 0, 0.5f));
        }

        double CurrentBpmAt(double t)
        {
            var pts = tempo.Points;
            return (pts.LastOrDefault(x => x.TimeSec <= t) ?? pts[0]).Bpm;
        }

        void DrawWaveform()
        {
            var r = new Rect(area.x + Gutter, area.y + RulerH, area.width - Gutter, WaveH);
            EditorGUI.DrawRect(r, new Color(0.1f, 0.12f, 0.14f));
            if (waveMin == null) return;
            float mid = r.y + r.height / 2, half = r.height / 2 - 2;
            double start = data.AudioStartSec;
            var col = new Color(0.4f, 0.7f, 0.9f, 0.8f);
            for (float x = r.x; x < r.xMax; x += 1)
            {
                double t0 = TimeOf(x) + start, t1 = TimeOf(x + 1) + start;
                int b0 = Mathf.Max(0, (int)(t0 / waveSecPerBin)), b1 = Mathf.Min(waveMin.Length - 1, (int)(t1 / waveSecPerBin));
                if (b0 >= waveMin.Length || b1 < 0) continue;
                float lo = 0, hi = 0;
                for (int b = b0; b <= b1; b++) { lo = Mathf.Min(lo, waveMin[b]); hi = Mathf.Max(hi, waveMax[b]); }
                EditorGUI.DrawRect(new Rect(x, mid - hi * half, 1, Mathf.Max(1, (hi - lo) * half)), col);
            }
        }

        /// <summary>選んだノーツが1つなら，その判定範囲（Good / Perfect / Miss になる時刻）を薄く表示する．</summary>
        void DrawJudgeWindows()
        {
            if (selected.Count != 1) return;
            var n = notes[selected.First()];
            var rules = data.EffectiveRules;
            float y = YOfLane(n.Lane), h = LaneH - 1;
            void Band(double a, double b, Color c) => EditorGUI.DrawRect(new Rect(XOf(a), y, XOf(b) - XOf(a), h), c);
            var tempo = data.TempoMap();
            double good = rules.GoodAt(tempo, n.Time), perfect = rules.PerfectAt(tempo, n.Time);
            Band(n.Time - good, n.Time - perfect, new Color(1f, 1f, 1f, 0.10f));
            Band(n.Time - perfect, n.Time + rules.MissAfterAt(tempo, n.Time), new Color(1f, 0.3f, 1f, 0.16f));
        }

        void DrawNotes()
        {
            float left = area.x + Gutter;
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                float x = XOf(n.Time);
                if (x < left - 8 || x > area.xMax + 8) continue;
                float y = YOfLane(n.Lane);
                bool wired = wiredLanes == null || wiredLanes.Contains(n.Lane);
                var c = n.Type == NoteType.Connect ? new Color(0.3f, 0.8f, 1f) : new Color(1f, 0.85f, 0.25f);
                if (!wired) c = new Color(0.9f, 0.3f, 0.3f);
                if (selected.Contains(i)) EditorGUI.DrawRect(new Rect(x - 6, y + 1, 12, LaneH - 3), Color.white);
                EditorGUI.DrawRect(new Rect(x - 4, y + 3, 8, LaneH - 7), c);
            }
        }

        void DrawCursor(double t, Color c)
        {
            float x = XOf(t);
            if (x < area.x + Gutter || x > area.xMax) return;
            EditorGUI.DrawRect(new Rect(x - 1, area.y, 2, area.height), c);
        }

        void DrawStatus()
        {
            var r = new Rect(4, position.height - 18, position.width - 8, 18);
            double t = double.IsNaN(PlayheadSec) ? cursorSec : PlayheadSec;
            var (bar, beat) = tempo.BarBeatOf(t);
            int unwired = wiredLanes == null ? 0 : notes.Count(n => !wiredLanes.Contains(n.Lane));
            string sec = sections.LastOrDefault(x => x.Time <= t + 1e-6)?.Name;
            string msg = $"{t:F3} 秒（{bar} 小節 {beat:F2} 拍{(string.IsNullOrEmpty(sec) ? "" : $"，{sec}")}）  ノーツ {notes.Count}  選択 {selected.Count}";
            if (unwired > 0) msg += $"  ⚠ 配線の無いレーンのノーツ {unwired}（赤）";
            if (!EditorAudio.Available) msg += "  ※この Unity では再生できません";
            if (!string.IsNullOrEmpty(tapMessage)) msg += "  " + tapMessage;
            GUI.Label(r, msg, EditorStyles.miniLabel);
        }
    }
}
