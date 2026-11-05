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
    // マウスとキーの操作，曲の区切りの編集（ChartEditorWindow の一部．ChartEditorWindow.cs と同じクラス）
    partial class ChartEditorWindow
    {
        void HandleInput()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown) HandleKey(e);
            if (!area.Contains(e.mousePosition) && !dragging && draggingSection < 0) return;

            switch (e.type)
            {
                case EventType.ScrollWheel:
                    if (e.control || e.command)
                    {
                        double at = TimeOf(e.mousePosition.x);
                        pxPerSec = Mathf.Clamp(pxPerSec * (e.delta.y > 0 ? 0.85f : 1.18f), 20, 800);
                        scrollSec = at - (e.mousePosition.x - area.x - Gutter) / pxPerSec;
                    }
                    else scrollSec += (e.delta.y + e.delta.x) * 30 / pxPerSec;
                    e.Use();
                    break;

                case EventType.MouseDown:
                    mouseDownPos = e.mousePosition;
                    if (e.mousePosition.y < area.y + RulerH + WaveH)
                    {
                        int near = SectionNear(e.mousePosition.x);
                        if (e.button == 1)
                        {
                            ShowSectionMenu(TimeOf(e.mousePosition.x), near);
                            e.Use();
                            break;
                        }
                        if (e.button == 0 && near >= 0 && e.mousePosition.y > area.y + RulerH)
                        {
                            draggingSection = near;
                            sectionDragBefore = Snapshot();
                            e.Use();
                            break;
                        }
                        cursorSec = Math.Max(0, SnapTime(TimeOf(e.mousePosition.x)));
                        if (EditorAudio.IsPlaying) PlayFrom(cursorSec);
                        e.Use();
                        break;
                    }
                    int hit = HitNote(e.mousePosition);
                    int lane = LaneOf(e.mousePosition.y);
                    if (e.button == 1)
                    {
                        if (hit >= 0) { selected.Clear(); selected.Add(hit); DeleteSelected(); }
                        e.Use();
                        break;
                    }
                    if (e.button != 0 || lane < 0 || e.mousePosition.x < area.x + Gutter) break;
                    if (hit < 0)
                    {
                        // Alt を押しながらなら一番近い拍に置く
                        double t = e.alt ? tempo.Snap(TimeOf(e.mousePosition.x), 1) : SnapTime(TimeOf(e.mousePosition.x));
                        if (t < 0) break;
                        hit = AddNote(t, lane);
                        selected.Clear();
                    }
                    else if (e.shift)
                    {
                        if (!selected.Remove(hit)) selected.Add(hit);
                        e.Use();
                        break;
                    }
                    else if (!selected.Contains(hit))
                    {
                        selected.Clear();
                    }
                    selected.Add(hit);
                    cursorSec = notes[hit].Time;
                    dragging = true;
                    dragStartSec = notes[hit].Time;
                    dragStartLane = notes[hit].Lane;
                    dragOriginal = new List<ChartNote>(notes);
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (draggingSection >= 0)
                    {
                        double t = Math.Max(0, snap ? tempo.Snap(TimeOf(e.mousePosition.x), 1) : Math.Round(TimeOf(e.mousePosition.x), 3));
                        sections[draggingSection].Time = t;
                        e.Use();
                        break;
                    }
                    if (!dragging) break;
                    {
                        double t = SnapTime(TimeOf(e.mousePosition.x));
                        double dt = t - dragStartSec;
                        int dl = Mathf.Max(0, LaneOf(e.mousePosition.y)) - dragStartLane;
                        if (selected.Any(i => dragOriginal[i].Lane + dl < 0 || dragOriginal[i].Time + dt < 0)) { e.Use(); break; }
                        foreach (int i in selected) notes[i] = new ChartNote(dragOriginal[i].Time + dt, dragOriginal[i].Lane + dl, dragOriginal[i].Type, dragOriginal[i].Flow);
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (draggingSection >= 0)
                    {
                        draggingSection = -1;
                        undo.Add(sectionDragBefore);
                        redo.Clear();
                        dirty = true;
                        sections.Sort((a, b) => a.Time.CompareTo(b.Time));
                        e.Use();
                        break;
                    }
                    if (dragging)
                    {
                        dragging = false;
                        bool moved = (e.mousePosition - mouseDownPos).sqrMagnitude > 4;
                        if (moved)
                        {
                            // ドラッグ前の状態を元に戻す履歴に入れる
                            var after = new List<ChartNote>(notes);
                            notes.Clear(); notes.AddRange(dragOriginal);
                            RecordUndo();
                            notes.Clear(); notes.AddRange(after);
                        }
                        e.Use();
                    }
                    break;
            }
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) Repaint();
        }

        void HandleKey(Event e)
        {
            bool ctrl = e.control || e.command;
            if (ctrl && e.keyCode == KeyCode.S) { Save(); e.Use(); return; }
            if (ctrl && e.keyCode == KeyCode.Z) { Undo(); e.Use(); return; }
            if (ctrl && e.keyCode == KeyCode.Y) { Redo(); e.Use(); return; }
            if (ctrl) return;

            switch (e.keyCode)
            {
                case KeyCode.Space:
                    if (EditorAudio.Available && song.audio != null) TogglePlay();
                    e.Use();
                    return;
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    DeleteSelected();
                    e.Use();
                    return;
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                    MoveSelected((e.keyCode == KeyCode.RightArrow ? 1 : -1) * 60.0 / CurrentBpm / Division, 0);
                    e.Use();
                    return;
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                    MoveSelected(0, e.keyCode == KeyCode.DownArrow ? 1 : -1);
                    e.Use();
                    return;
            }

            if (e.keyCode == KeyCode.M)
            {
                SetMetronome(!metronome);
                e.Use();
                return;
            }
            double now = PlayheadSec;
            bool live = !double.IsNaN(now);
            double at = live ? now - tapLatencyMs / 1000.0 : cursorSec;

            if ((e.keyCode == KeyCode.B || e.keyCode == KeyCode.N) && live)
            {
                Tap(at, e.keyCode == KeyCode.N);
                e.Use();
                return;
            }
            int lane = Array.IndexOf(LaneKeys, e.keyCode);
            if (lane >= 0)
            {
                int i = AddNote(Math.Max(0, SnapTime(at)), lane);
                selected.Clear();
                selected.Add(i);
                e.Use();
            }
        }

        // ---- 曲の区切り ----

        /// <summary>画面の x に近い（6px 以内の）区切りの番号．無ければ -1．</summary>
        int SectionNear(float x)
        {
            int best = -1;
            float bestDx = 6;
            for (int i = 0; i < sections.Count; i++)
            {
                float dx = Mathf.Abs(XOf(sections[i].Time) - x);
                if (dx < bestDx) { bestDx = dx; best = i; }
            }
            return best;
        }

        void ShowSectionMenu(double at, int near)
        {
            menuPos = Event.current.mousePosition;
            double t = Math.Max(0, snap ? tempo.Snap(at, 1) : Math.Round(at, 3));
            var menu = new GenericMenu();
            foreach (var name in SectionNames)
                menu.AddItem(new GUIContent($"ここから区切りを足す/{name}"), false, () => AddSection(t, name));
            menu.AddItem(new GUIContent("ここから区切りを足す/その他（名前を入れる）…"), false, () => AskName("", n => AddSection(t, n)));
            if (near >= 0)
            {
                var s = sections[near];
                menu.AddSeparator("");
                foreach (var name in SectionNames)
                    menu.AddItem(new GUIContent($"「{s.Name}」の名前を変える/{name}"), false, () => { RecordUndo(); s.Name = name; });
                menu.AddItem(new GUIContent($"「{s.Name}」の名前を変える/その他…"), false, () => AskName(s.Name, n => { RecordUndo(); s.Name = n; }));
                menu.AddItem(new GUIContent($"「{s.Name}」を消す"), false, () => { RecordUndo(); sections.Remove(s); });
            }
            menu.ShowAsContext();
        }

        void AddSection(double t, string name)
        {
            RecordUndo();
            sections.Add(new SongSection { Time = t, Name = name });
            sections.Sort((a, b) => a.Time.CompareTo(b.Time));
            Repaint();
        }

        void AskName(string current, Action<string> done)
        {
            pendingPopup = () => PopupWindow.Show(new Rect(menuPos, Vector2.one), new SectionNamePopup(current, n => { done(n); Repaint(); }));
            Repaint();
        }

        int HitNote(Vector2 p)
        {
            int lane = LaneOf(p.y);
            int best = -1;
            float bestDx = 7;
            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i].Lane != lane) continue;
                float dx = Mathf.Abs(XOf(notes[i].Time) - p.x);
                if (dx < bestDx) { bestDx = dx; best = i; }
            }
            return best;
        }
    }
}
