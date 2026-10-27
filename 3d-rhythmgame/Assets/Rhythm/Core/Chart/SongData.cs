using System;
using System.Collections.Generic;
using System.Linq;
using Arte.Rhythm.Gameplay;
using Newtonsoft.Json;

namespace Arte.Rhythm.Charts
{
    /// <summary>song.json のノーツ1個．</summary>
    public sealed class SongNote
    {
        /// <summary>曲時刻（秒）．</summary>
        [JsonProperty("t")] public double Time;
        /// <summary>レーン番号（0始まり．編集画面では1始まりで表示する）．</summary>
        [JsonProperty("lane")] public int Lane;
        [JsonProperty("type")] public string Type = "touch";
    }

    /// <summary>
    /// 1曲分のデータ（song.json）．譜面・テンポ・同期の補正値・判定ルールをまとめて持つ．
    /// 音声・動画・Timeline のアセットは Unity 側の SongAsset が持ち，ここにはファイル名を参考として書く．
    ///
    /// 時刻の基準は「曲時刻」: 音声ファイルの AudioStartSec の位置を 0 秒とする．
    /// 音声の頭に無音がある場合や，動画が音声とずれている場合はオフセットで合わせる．
    /// </summary>
    public sealed class SongData
    {
        public const string CurrentFormat = "arte-rhythm-song/1";

        [JsonProperty("format")] public string Format = CurrentFormat;
        [JsonProperty("title")] public string Title = "";
        [JsonProperty("credits")] public string Credits = "";

        /// <summary>参考: 音声ファイル名．</summary>
        [JsonProperty("audio")] public string Audio = "";

        /// <summary>曲時刻 0 が音声ファイルの何秒目か．</summary>
        [JsonProperty("audioStartSec")] public double AudioStartSec;

        /// <summary>曲の長さ（秒）．0 なら音声の長さを使う．</summary>
        [JsonProperty("lengthSec")] public double LengthSec;

        /// <summary>参考: 演出動画のファイル名（旧方式の演出．Timeline へ移行するまで使う）．</summary>
        [JsonProperty("video")] public string Video = "";

        /// <summary>曲時刻 t のときに動画の (t + VideoOffsetSec) 秒目を出す．</summary>
        [JsonProperty("videoOffsetSec")] public double VideoOffsetSec;

        [JsonProperty("tempo")] public List<TempoPoint> Tempo = new List<TempoPoint>();

        /// <summary>この曲だけ判定ルールを変える場合に書く．null なら既定（仕様書の判定幅）．</summary>
        [JsonProperty("rules", NullValueHandling = NullValueHandling.Ignore)] public JudgeRules Rules;

        [JsonProperty("notes")] public List<SongNote> Notes = new List<SongNote>();

        [JsonIgnore] public JudgeRules EffectiveRules => Rules ?? new JudgeRules();

        public TempoMap TempoMap() => new TempoMap(Tempo);

        public Chart ToChart() => new Chart(Notes.Select(n => new ChartNote(n.Time, n.Lane, ParseType(n.Type))));

        /// <summary>譜面を書き戻す（時刻順に並べ，時刻はミリ秒の1/10で丸める）．</summary>
        public void SetNotes(IEnumerable<ChartNote> notes)
        {
            Notes = new Chart(notes).Notes
                .Select(n => new SongNote { Time = Math.Round(n.Time, 4), Lane = n.Lane, Type = TypeName(n.Type) })
                .ToList();
        }

        public static NoteType ParseType(string s) => s switch
        {
            "touch" => NoteType.Touch,
            "connect" => NoteType.Connect,
            _ => throw new FormatException($"未知のノーツ種別 \"{s}\""),
        };

        public static string TypeName(NoteType t) => t == NoteType.Connect ? "connect" : "touch";
    }

    public static class SongJson
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Formatting = Formatting.Indented,
        };

        public static SongData Load(string json)
        {
            var song = JsonConvert.DeserializeObject<SongData>(json, Settings);
            if (song.Format != SongData.CurrentFormat) throw new FormatException($"未対応の形式: {song.Format}");
            return song;
        }

        /// <summary>
        /// 保存．ノーツは1行1個にして，git の差分で何を変えたか読めるようにする．
        /// </summary>
        public static string Save(SongData song)
        {
            var notes = song.Notes;
            song.Notes = new List<SongNote>();
            string head;
            try { head = JsonConvert.SerializeObject(song, Settings); }
            finally { song.Notes = notes; }
            var lines = notes.Select(n => "    " + JsonConvert.SerializeObject(n, Formatting.None));
            string body = notes.Count == 0 ? "[]" : "[\n" + string.Join(",\n", lines) + "\n  ]";
            return head.Replace("\"notes\": []", "\"notes\": " + body).Replace("\r\n", "\n") + "\n";
        }

        /// <summary>旧譜面 JSON から作る（レーン番号は1始まり → 0始まりに直る）．</summary>
        public static SongData FromLegacy(string legacyJson, string title)
        {
            var song = new SongData { Title = title };
            song.SetNotes(LegacyChartJson.Parse(legacyJson).Notes);
            return song;
        }
    }
}
