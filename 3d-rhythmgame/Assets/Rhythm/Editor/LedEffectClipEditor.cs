using System.Collections.Generic;
using System.Linq;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Render;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEditor.Timeline.Actions;
using UnityEngine;
using UnityEngine.Timeline;

namespace Arte.Rhythm.Editor
{
    /// <summary>
    /// LED 演出のクリップの Inspector に「何小節目の何拍目から，何拍分」を出す（直接入れても動く）．
    /// テンポはこの Timeline を使っている曲（SongAsset.timeline）から取る．
    /// </summary>
    [CustomEditor(typeof(LedEffectClip))]
    sealed class LedEffectClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var clip = TimelineEditor.selectedClip;
            var timeline = TimelineEditor.inspectedAsset;
            var song = SongTimelineSync.SongOf(timeline);
            if (clip != null && clip.asset == target && song?.Data != null)
            {
                var tempo = song.Data.TempoMap();
                var (bar, beat) = tempo.BarBeatOf(clip.start + 1e-6);
                double beats = tempo.BeatOf(clip.end) - tempo.BeatOf(clip.start);
                string section = song.Data.SectionAt(clip.start + 1e-6);
                EditorGUILayout.LabelField("曲の中の位置", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                int newBar = EditorGUILayout.IntField(new GUIContent("始まり（小節）", section), bar);
                double newBeat = EditorGUILayout.DoubleField("始まり（拍．1 = 小節の頭）", System.Math.Round(beat, 3));
                double newBeats = EditorGUILayout.DoubleField("長さ（拍）", System.Math.Round(beats, 3));
                if (EditorGUI.EndChangeCheck() && newBeats > 0)
                {
                    Undo.RecordObject(clip.GetParentTrack(), "演出の位置");
                    double start = tempo.TimeOfBarBeat(newBar, newBeat);
                    clip.start = start;
                    clip.duration = tempo.TimeOf(tempo.BeatOf(start) + newBeats) - start;
                    TimelineEditor.Refresh(RefreshReason.ContentsModified);
                }
                if (!string.IsNullOrEmpty(section)) EditorGUILayout.LabelField("区切り", section);
                if (GUILayout.Button("始まりと終わりを近くの拍に合わせる"))
                {
                    LedEffectSnapToBeat.Snap(new[] { clip }, tempo);
                }
                EditorGUILayout.Space();
            }
            else if (clip != null && clip.asset == target)
            {
                EditorGUILayout.HelpBox("この Timeline を曲（SongAsset の timeline）に入れると，小節と拍で位置を表示・入力できる", MessageType.Info);
            }
            DrawDefaultInspector();
        }
    }

    /// <summary>Timeline のクリップの右クリック: 選んだクリップの始まりと終わりを近くの拍に合わせる．</summary>
    [MenuEntry("拍に合わせる（Arte Rhythm）")]
    sealed class LedEffectSnapToBeat : ClipAction
    {
        public override ActionValidity Validate(IEnumerable<TimelineClip> clips) =>
            SongTimelineSync.SongOf(TimelineEditor.inspectedAsset)?.Data != null && clips.Any() ? ActionValidity.Valid : ActionValidity.NotApplicable;

        public override bool Execute(IEnumerable<TimelineClip> clips)
        {
            var song = SongTimelineSync.SongOf(TimelineEditor.inspectedAsset);
            if (song?.Data == null) return false;
            Snap(clips, song.Data.TempoMap());
            return true;
        }

        public static void Snap(IEnumerable<TimelineClip> clips, TempoMap tempo)
        {
            foreach (var c in clips)
            {
                Undo.RecordObject(c.GetParentTrack(), "拍に合わせる");
                double s = tempo.Snap(c.start, 1), e = tempo.Snap(c.end, 1);
                if (e <= s) e = tempo.TimeOf(tempo.BeatOf(s) + 1);
                c.start = s;
                c.duration = e - s;
            }
            TimelineEditor.Refresh(RefreshReason.ContentsModified);
        }
    }
}
