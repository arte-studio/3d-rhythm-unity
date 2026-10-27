using System.Collections.Generic;
using System.Linq;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Render;
using Arte.Rhythm.Song;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace Arte.Rhythm.Editor
{
    /// <summary>
    /// 曲の Timeline を，曲のテンポと区切り（song.json）に合わせる．
    /// - 一番上の「曲（小節と区切り）」トラックに，小節の頭と区切りの目印を作り直す（Sync）
    /// - テンポや 1 小節目を直したら，演出のクリップを「小節.拍」を保ったまま動かす（Retime）
    /// どちらも譜面エディタの保存から呼ばれる．Tools / Arte Rhythm / Sync Song Timeline でも呼べる．
    /// </summary>
    public static class SongTimelineSync
    {
        public const string GridTrackName = "曲（小節と区切り）";

        /// <summary>この Timeline を使っている曲（SongAsset.timeline）．無ければ null．</summary>
        public static SongAsset SongOf(TimelineAsset timeline)
        {
            if (timeline == null) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:SongAsset"))
            {
                var s = AssetDatabase.LoadAssetAtPath<SongAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null && s.timeline == timeline) return s;
            }
            return null;
        }

        [MenuItem("Tools/Arte Rhythm/Sync Song Timeline (selected SongAsset)")]
        static void SyncSelected()
        {
            if (Selection.activeObject is SongAsset song && song.timeline != null && song.Data != null) Sync(song.timeline, song.Data, song.LengthSec);
            else Debug.LogWarning("[SongTimelineSync] Timeline を設定した SongAsset を選ぶ");
        }

        /// <summary>Timeline を持つすべての曲で目印を作り直す．</summary>
        [MenuItem("Tools/Arte Rhythm/Sync All Song Timelines")]
        public static void SyncAll()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:SongAsset"))
            {
                var s = AssetDatabase.LoadAssetAtPath<SongAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null && s.timeline != null && s.Data != null) Sync(s.timeline, s.Data, s.LengthSec);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>目印が song.json と食い違っている曲だけ作り直す（song.json を外で書き換えたとき用）．作り直した数を返す．</summary>
        public static int SyncStale()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:SongAsset"))
            {
                var s = AssetDatabase.LoadAssetAtPath<SongAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (s == null || s.timeline == null || s.Data == null || !IsStale(s.timeline, s.Data, s.LengthSec)) continue;
                Sync(s.timeline, s.Data, s.LengthSec);
                Debug.Log($"[SongTimelineSync] {s.name}: 曲の小節と区切りの目印を作り直した");
                n++;
            }
            if (n > 0) AssetDatabase.SaveAssets();
            return n;
        }

        static bool IsStale(TimelineAsset timeline, SongData data, double lengthSec)
        {
            var track = timeline.GetOutputTracks().OfType<SongGridTrack>().FirstOrDefault();
            if (track == null) return true;
            var tempo = data.TempoMap();
            var want = tempo.GridLines(0, EndOf(data, lengthSec), 1).Where(g => g.isBarHead).Select(g => g.sec)
                .Concat(data.Sections.Select(s => s.Time)).OrderBy(t => t).ToList();
            var have = track.GetMarkers().Select(m => m.time).OrderBy(t => t).ToList();
            if (want.Count != have.Count) return true;
            for (int i = 0; i < want.Count; i++)
                if (System.Math.Abs(want[i] - have[i]) > 1e-3) return true;
            // 区切りの名前
            var names = track.GetMarkers().OfType<SongSectionMarker>().Select(m => m.sectionName).OrderBy(x => x);
            return !names.SequenceEqual(data.Sections.Select(s => s.Name).OrderBy(x => x));
        }

        static double EndOf(SongData data, double lengthSec) =>
            lengthSec > 0 ? lengthSec : Mathf.Max(60, (float)(data.Notes.Count > 0 ? data.Notes.Max(n => n.Time) + 5 : 60));

        /// <summary>小節の頭と区切りの目印を作り直す．</summary>
        public static void Sync(TimelineAsset timeline, SongData data, double lengthSec)
        {
            if (timeline == null || data == null) return;
            Undo.RecordObject(timeline, "曲の小節と区切り");
            var track = timeline.GetOutputTracks().OfType<SongGridTrack>().FirstOrDefault();
            if (track == null)
            {
                track = timeline.CreateTrack<SongGridTrack>(GridTrackName);
                // 一番上に移す
                var tracks = timeline.GetRootTracks().ToList();
                if (tracks.Count > 1)
                {
                    var field = typeof(TimelineAsset).GetField("m_Tracks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (field?.GetValue(timeline) is List<ScriptableObject> list)
                    {
                        list.Remove(track);
                        list.Insert(0, track);
                    }
                }
            }
            track.locked = false; // 以前はロックしていた（「Locked」の表示が区切りの名前に重なるのでやめた）
            foreach (var m in track.GetMarkers().ToList()) track.DeleteMarker(m);
            RemoveMissingMarkers(track);

            var tempo = data.TempoMap();
            foreach (var (sec, _, isBar) in tempo.GridLines(0, EndOf(data, lengthSec), 1))
            {
                if (!isBar) continue;
                var m = track.CreateMarker<BarMarker>(sec);
                m.bar = tempo.BarBeatOf(sec + 1e-6).bar;
            }
            foreach (var s in data.Sections)
            {
                var m = track.CreateMarker<SongSectionMarker>(s.Time);
                m.sectionName = s.Name;
            }
            EditorUtility.SetDirty(timeline);
            TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
        }

        /// <summary>
        /// 目印の一覧に残った「消えた目印」への参照を取り除く（エディタを閉じるときに目印だけ消えて一覧が残ることがあった．
        /// 残ると読み込むたびに Broken PPtr の警告が出る）．Timeline に公開の手段が無いのでリフレクションで触る．
        /// </summary>
        static void RemoveMissingMarkers(TrackAsset track)
        {
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var markerList = typeof(TrackAsset).GetField("m_Markers", F)?.GetValue(track);
            if (markerList?.GetType().GetField("m_Objects", F)?.GetValue(markerList) is List<ScriptableObject> objects)
            {
                int removed = objects.RemoveAll(o => o == null);
                if (removed > 0) Debug.Log($"[SongTimelineSync] 消えた目印への参照を {removed} 個取り除いた");
            }
        }

        /// <summary>演出のクリップ（とフェードの長さ）を「小節.拍」を保ったまま新しいテンポに移す．動かしたクリップの数を返す．</summary>
        public static int Retime(TimelineAsset timeline, TempoMap from, TempoMap to)
        {
            if (timeline == null) return 0;
            Undo.RecordObject(timeline, "テンポに合わせて演出を動かす");
            int n = 0;
            foreach (var track in timeline.GetOutputTracks())
            {
                if (track is SongGridTrack) continue;
                Undo.RecordObject(track, "テンポに合わせて演出を動かす");
                foreach (var clip in track.GetClips().OrderBy(c => c.start))
                {
                    double s0 = clip.start, e0 = clip.end;
                    double s1 = TempoMap.Retime(s0, from, to), e1 = TempoMap.Retime(e0, from, to);
                    double easeIn = TempoMap.Retime(s0 + clip.easeInDuration, from, to) - s1;
                    double easeOut = e1 - TempoMap.Retime(e0 - clip.easeOutDuration, from, to);
                    clip.start = s1;
                    clip.duration = System.Math.Max(1e-3, e1 - s1);
                    if (clip.clipCaps.HasFlag(ClipCaps.Blending))
                    {
                        clip.easeInDuration = System.Math.Max(0, easeIn);
                        clip.easeOutDuration = System.Math.Max(0, easeOut);
                    }
                    n++;
                }
                foreach (var m in track.GetMarkers()) m.time = TempoMap.Retime(m.time, from, to);
                EditorUtility.SetDirty(track);
            }
            EditorUtility.SetDirty(timeline);
            TimelineEditor.Refresh(RefreshReason.ContentsModified);
            return n;
        }
    }

    /// <summary>
    /// song.json を Unity の外で書き換えても目印が古いままにならないように，
    /// エディタの起動・スクリプトの再読み込み・song.json の読み込みのたびに食い違いを確かめて作り直す．
    /// </summary>
    [InitializeOnLoad]
    sealed class SongTimelineAutoSync : AssetPostprocessor
    {
        static SongTimelineAutoSync() => EditorApplication.delayCall += () => SongTimelineSync.SyncStale();

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Any(p => p.EndsWith("song.json")))
                EditorApplication.delayCall += () => SongTimelineSync.SyncStale();
        }
    }

    /// <summary>区切りの目印に名前を書く．</summary>
    [CustomTimelineEditor(typeof(SongSectionMarker))]
    sealed class SongSectionMarkerEditor : MarkerEditor
    {
        public override MarkerDrawOptions GetMarkerOptions(IMarker marker) =>
            new MarkerDrawOptions { tooltip = ((SongSectionMarker)marker).sectionName };

        public override void DrawOverlay(IMarker marker, MarkerUIStates uiState, MarkerOverlayRegion region)
        {
            var m = (SongSectionMarker)marker;
            var r = region.markerRegion;
            var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(1f, 0.85f, 0.4f) } };
            var size = style.CalcSize(new GUIContent(m.sectionName));
            var label = new Rect(r.xMax + 2, r.y, size.x + 4, r.height);
            EditorGUI.DrawRect(label, new Color(0, 0, 0, 0.6f));
            GUI.Label(label, m.sectionName, style);
            // 区切りの線をトラックの下まで引く
            EditorGUI.DrawRect(new Rect(r.center.x, region.timelineRegion.y, 1, region.timelineRegion.height), new Color(1f, 0.85f, 0.4f, 0.35f));
        }
    }

    /// <summary>小節の頭の目印に小節番号を書き，薄い線を引く．</summary>
    [CustomTimelineEditor(typeof(BarMarker))]
    sealed class BarMarkerEditor : MarkerEditor
    {
        public override MarkerDrawOptions GetMarkerOptions(IMarker marker) =>
            new MarkerDrawOptions { tooltip = $"{((BarMarker)marker).bar} 小節目" };

        public override void DrawOverlay(IMarker marker, MarkerUIStates uiState, MarkerOverlayRegion region)
        {
            var m = (BarMarker)marker;
            var r = region.markerRegion;
            GUI.Label(new Rect(r.xMax, r.y - 2, 40, r.height), m.bar.ToString(), EditorStyles.miniLabel);
            EditorGUI.DrawRect(new Rect(r.center.x, region.timelineRegion.y, 1, region.timelineRegion.height), new Color(1, 1, 1, 0.08f));
        }
    }
}
