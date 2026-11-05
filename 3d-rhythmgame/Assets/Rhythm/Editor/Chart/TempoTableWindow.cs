using System.Collections.Generic;
using System.Linq;
using Arte.Rhythm.Charts;
using UnityEditor;
using UnityEngine;

namespace Arte.Rhythm.Editor
{
    /// <summary>
    /// テンポ表（譜面エディタの「テンポ表…」）．曲の途中でテンポや拍子が変わるときに，変わる位置ごとに 1 行足す．
    /// 1 行目の時刻が 1 小節目の頭．2 行目以降はその時刻から新しいテンポ・拍子になる（小節の頭に置く）．
    /// 「反映」で譜面エディタに戻し，譜面エディタの保存で song.json に書く（ノーツと演出を動かすかはそのとき聞かれる）．
    /// </summary>
    sealed class TempoTableWindow : EditorWindow
    {
        ChartEditorWindow owner;
        List<TempoPoint> rows = new List<TempoPoint>();
        Vector2 scroll;

        public static void Open(ChartEditorWindow owner)
        {
            var w = GetWindow<TempoTableWindow>(true, "テンポ表", true);
            w.owner = owner;
            w.rows = owner.Data.Tempo.Select(p => new TempoPoint { TimeSec = p.TimeSec, Bpm = p.Bpm, BeatsPerBar = p.BeatsPerBar }).ToList();
            if (w.rows.Count == 0) w.rows.Add(new TempoPoint());
            w.minSize = new Vector2(420, 220);
        }

        void OnGUI()
        {
            if (owner == null || owner.Data == null)
            {
                EditorGUILayout.HelpBox("譜面エディタから開く", MessageType.Info);
                return;
            }
            EditorGUILayout.HelpBox("1 行目の時刻 = 1 小節目の頭．テンポや拍子が途中で変わるなら，変わる小節の頭の時刻で行を足す．", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("時刻（秒）", GUILayout.Width(110));
                GUILayout.Label("BPM", GUILayout.Width(90));
                GUILayout.Label("拍子（1 小節の拍数）", GUILayout.Width(130));
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            int remove = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var r = rows[i];
                    r.TimeSec = EditorGUILayout.DoubleField(r.TimeSec, GUILayout.Width(110));
                    r.Bpm = EditorGUILayout.DoubleField(r.Bpm, GUILayout.Width(90));
                    r.BeatsPerBar = EditorGUILayout.IntField(r.BeatsPerBar, GUILayout.Width(130));
                    using (new EditorGUI.DisabledScope(rows.Count <= 1))
                        if (GUILayout.Button("消す", GUILayout.Width(44))) remove = i;
                }
            }
            EditorGUILayout.EndScrollView();
            if (remove >= 0) rows.RemoveAt(remove);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("カーソルの位置に行を足す"))
                {
                    var last = rows.OrderBy(p => p.TimeSec).LastOrDefault(p => p.TimeSec <= owner.CursorSec) ?? rows[0];
                    rows.Add(new TempoPoint { TimeSec = System.Math.Round(owner.CursorSec, 4), Bpm = last.Bpm, BeatsPerBar = last.BeatsPerBar });
                }
                bool valid = rows.All(p => p.Bpm > 0 && p.BeatsPerBar > 0) && rows.Select(p => p.TimeSec).Distinct().Count() == rows.Count;
                using (new EditorGUI.DisabledScope(!valid))
                    if (GUILayout.Button("反映（譜面エディタで保存すると song.json に書く）")) owner.ReplaceTempo(rows);
            }
        }
    }
}
