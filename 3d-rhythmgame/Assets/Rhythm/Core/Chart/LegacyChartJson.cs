using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Arte.Rhythm.Charts
{
    /// <summary>
    /// 旧版の譜面 JSON（Assets/Resources/*_NoteData_*.json）を読む．
    /// 形式: { "notes": [ { "time": 2.26, "lane": 3, "type": "touch" }, ... ] }
    /// 新しい譜面形式（P3）へ移行するまでと，移行ツールの入力に使う．
    ///
    /// 旧譜面のレーン番号は1始まり（譜面作者の意図）なので，読み込み時に1を引いて内部の0始まりにそろえる．
    /// 旧版のゲームはこの番号をそのまま0始まりのレーンとして使っていたため，本番ではレーンが1つずれていた．
    /// </summary>
    public static class LegacyChartJson
    {
        // JSON から値が入るフィールド（コンパイラには代入が見えない）
#pragma warning disable CS0649
        sealed class Wrapper
        {
            public List<Entry> notes;
        }

        sealed class Entry
        {
            public double time;
            public int lane;
            public string type;
        }
#pragma warning restore CS0649

        public static Chart Parse(string json)
        {
            var wrapper = JsonConvert.DeserializeObject<Wrapper>(json);
            if (wrapper?.notes == null) throw new FormatException("\"notes\" 配列がない");
            var notes = new List<ChartNote>(wrapper.notes.Count);
            foreach (var e in wrapper.notes)
            {
                NoteType type = e.type switch
                {
                    "touch" => NoteType.Touch,
                    "connect" => NoteType.Connect,
                    _ => throw new FormatException($"未知のノーツ種別 \"{e.type}\"（time {e.time}）"),
                };
                if (e.lane < 1) throw new FormatException($"旧譜面のレーン番号は1始まり: lane {e.lane}（time {e.time}）");
                notes.Add(new ChartNote(e.time, e.lane - 1, type));
            }
            return new Chart(notes);
        }
    }
}
