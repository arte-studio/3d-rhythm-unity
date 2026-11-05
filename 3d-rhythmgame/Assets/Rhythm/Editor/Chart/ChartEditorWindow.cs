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
    /// <summary>
    /// 譜面エディタ（Window/Arte Rhythm/Chart Editor）．
    ///
    /// 横が時間，縦がレーン（画面上は1始まり）．音声の波形と拍のグリッドを重ねて表示し，
    /// プレイモードに入らずに曲を鳴らしながら編集できる．Timeline ではなく専用画面にしたのは，
    /// 拍のグリッド・スナップ・レーン単位の配置・タップ入力が Timeline では作りにくいため．
    /// 演出の編集は Timeline で行い，同じ曲時刻で再生される（SongVisualSync）．
    ///
    /// 操作:
    ///   クリック = ノーツを置く / 選ぶ（Shift で追加選択），ドラッグ = 移動，右クリック = 削除
    ///   ルーラー（上端）をクリック = カーソル移動，Space = カーソルから再生 / 停止
    ///   Alt + クリック = 一番近い拍にノーツを置く，M = メトロノームの入 / 切（拍と曲が合っているか耳で確かめる）
    ///   波形の上で右クリック = 曲の区切り（前奏，A メロなど）を足す・名前を変える・消す．区切りの線をドラッグで動かす
    ///   1〜0, Q〜P = そのレーンにノーツを置く（再生中は再生位置，停止中はカーソル位置）
    ///   B / N = テンポのタップ（再生中に拍に合わせて 4 回以上．小節の 1 拍目は N），Delete = 削除，←→ = グリッド1つ移動，↑↓ = レーン移動
    ///   Ctrl+Z / Ctrl+Y = 元に戻す / やり直し，Ctrl+S = 保存，ホイール = スクロール，Ctrl+ホイール = 拡大縮小
    /// </summary>
    public sealed partial class ChartEditorWindow : EditorWindow
    {
        const float Gutter = 44, RulerH = 20, WaveH = 56, LaneH = 22, ToolbarH = 42;
        static readonly KeyCode[] LaneKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0,
            KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R, KeyCode.T, KeyCode.Y, KeyCode.U, KeyCode.I, KeyCode.O, KeyCode.P,
        };
        static readonly int[] Divisions = { 1, 2, 3, 4, 6, 8 };
        static readonly string[] SectionNames = { "前奏", "Aメロ", "Bメロ", "サビ1", "間奏", "サビ前", "サビ2", "最後" };
        static readonly Color[] SectionColors =
        {
            new Color(0.35f, 0.55f, 1f), new Color(0.8f, 0.45f, 1f), new Color(0.3f, 0.85f, 0.8f), new Color(1f, 0.85f, 0.3f),
            new Color(1f, 0.5f, 0.6f), new Color(0.6f, 1f, 0.5f), new Color(1f, 0.65f, 0.3f), new Color(0.6f, 0.6f, 0.9f),
        };
        static readonly string[] DivisionLabels = { "1拍", "1/2", "1/3", "1/4", "1/6", "1/8" };

        [SerializeField] SongAsset song;
        [SerializeField] float pxPerSec = 120;
        [SerializeField] double scrollSec = -0.5;
        [SerializeField] int divisionIndex = 3;
        [SerializeField] bool snap = true;
        [SerializeField] int setupIndex = 1; // 0 は「指定しない」．既定は最初の設営プロファイル
        [SerializeField] float tapLatencyMs;
        [SerializeField] bool metronome;

        AudioClip metronomeClip;
        string metronomeKey;

        SongData data;
        TempoMap tempo;
        TempoMap savedTempo; // 最後に保存した（読み込んだ）テンポ．保存のときに変わっていたらノーツと演出を動かすか聞く
        readonly List<ChartNote> notes = new List<ChartNote>();
        readonly List<SongSection> sections = new List<SongSection>();
        int draggingSection = -1;
        string sectionDragBefore;
        Vector2 menuPos;
        Action pendingPopup; // メニューの選択は OnGUI の外で呼ばれるので，次の OnGUI でポップアップを出す
        readonly HashSet<int> selected = new HashSet<int>();
        readonly List<string> undo = new List<string>(), redo = new List<string>();
        bool dirty;
        double cursorSec;

        string[] setupNames = Array.Empty<string>();
        SetupProfile[] setups = Array.Empty<SetupProfile>();
        HashSet<int> wiredLanes = new HashSet<int>();

        float[] waveMin, waveMax;
        float waveSecPerBin;

        readonly List<TapTempo.Tap> taps = new List<TapTempo.Tap>();
        string tapMessage = "";
        TempoPoint tapBase; // 叩き始める前のテンポ（叩いている間はこれを基準にする）

        // ドラッグ中の状態
        bool dragging;
        double dragStartSec;
        int dragStartLane;
        List<ChartNote> dragOriginal;
        Vector2 mouseDownPos;

        int Division => Divisions[Mathf.Clamp(divisionIndex, 0, Divisions.Length - 1)];

        [MenuItem("Window/Arte Rhythm/Chart Editor")]
        public static void Open() => GetWindow<ChartEditorWindow>("Chart Editor");

        /// <summary>SongAsset をダブルクリックしたら譜面エディタで開く．</summary>
        [UnityEditor.Callbacks.OnOpenAsset]
        static bool OnOpenAsset(int instanceId, int line)
        {
            if (!(EditorUtility.InstanceIDToObject(instanceId) is SongAsset s)) return false;
            var w = GetWindow<ChartEditorWindow>("Chart Editor");
            w.SetSong(s);
            return true;
        }

        void OnEnable()
        {
            wantsMouseMove = true;
            LoadSetups();
            if (song != null) SetSong(song);
            EditorApplication.update += Tick;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            EditorAudio.Stop();
        }

        void Tick()
        {
            if (EditorAudio.IsPlaying) Repaint();
        }

        // ---- 読み込み・保存 ----

        void LoadSetups()
        {
            var guids = AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets/Rhythm/Data/Hardware/setups" });
            var list = new List<(string, SetupProfile)>();
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.EndsWith(".json")) continue;
                try { list.Add((Path.GetFileNameWithoutExtension(path), HardwareJson.LoadSetup(File.ReadAllText(path)))); }
                catch (Exception e) { Debug.LogWarning($"[ChartEditor] {path}: {e.Message}"); }
            }
            setupNames = list.Select(x => x.Item1).Prepend("（設営を指定しない）").ToArray();
            setups = list.Select(x => x.Item2).Prepend(null).ToArray();
            UpdateWiredLanes();
        }

        void UpdateWiredLanes()
        {
            setupIndex = Mathf.Clamp(setupIndex, 0, setups.Length - 1);
            var s = setups.Length > 0 ? setups[setupIndex] : null;
            wiredLanes = s == null ? null : new HashSet<int>(s.lanes.Select(l => l.lane));
        }

        void SetSong(SongAsset s)
        {
            if (dirty && song != null && song != s && EditorUtility.DisplayDialog("譜面エディタ", $"{song.name} の変更を保存しますか", "保存", "破棄"))
                Save();
            EditorAudio.Stop();
            song = s;
            data = null;
            notes.Clear();
            selected.Clear();
            undo.Clear();
            redo.Clear();
            dirty = false;
            waveMin = waveMax = null;
            if (song == null || song.songJson == null) return;
            try
            {
                data = SongJson.Load(song.songJson.text);
                tempo = data.TempoMap();
                savedTempo = tempo;
                notes.AddRange(data.ToChart().Notes);
                sections.Clear();
                sections.AddRange(data.Sections.OrderBy(x => x.Time).Select(x => new SongSection { Time = x.Time, Name = x.Name }));
            }
            catch (Exception e)
            {
                data = null;
                Debug.LogError($"[ChartEditor] {song.name} の song.json を読めない: {e.Message}");
            }
            BuildWaveform();
            Repaint();
        }

        void Save()
        {
            if (data == null || song?.songJson == null) return;
            var newTempo = data.TempoMap();
            if (savedTempo != null && !savedTempo.SameAs(newTempo))
            {
                // テンポや 1 小節目を直した: ノーツ・区切り・演出を「小節.拍」を保って動かすか
                int choice = EditorUtility.DisplayDialogComplex("テンポが変わりました",
                    "ノーツ，曲の区切り，演出（Timeline のクリップ）を，同じ「小節.拍」に来るように動かしますか？\n\n" +
                    "・拍に合わせて置いたものは動かす（例: 12 小節目の 3 拍目のまま）\n" +
                    "・音を聞いて置いた（拍とは関係ない）ものは動かさない",
                    "全部動かす", "動かさない", "区切りと演出だけ動かす");
                if (choice != 1)
                {
                    RecordUndo();
                    if (choice == 0)
                        for (int i = 0; i < notes.Count; i++)
                            notes[i] = new ChartNote(Math.Max(0, TempoMap.Retime(notes[i].Time, savedTempo, newTempo)), notes[i].Lane, notes[i].Type, notes[i].Flow);
                    foreach (var sec in sections) sec.Time = Math.Max(0, TempoMap.Retime(sec.Time, savedTempo, newTempo));
                    int moved = song.timeline != null ? SongTimelineSync.Retime(song.timeline, savedTempo, newTempo) : 0;
                    Debug.Log($"[ChartEditor] テンポに合わせて動かした: {(choice == 0 ? $"ノーツ {notes.Count}，" : "")}区切り {sections.Count}，演出 {moved}");
                }
            }
            data.SetNotes(notes);
            data.Sections = sections.OrderBy(x => x.Time).Select(x => new SongSection { Time = Math.Round(x.Time, 3), Name = x.Name }).ToList();
            string path = AssetDatabase.GetAssetPath(song.songJson);
            File.WriteAllText(path, SongJson.Save(data));
            AssetDatabase.ImportAsset(path);
            // SetNotes で並び替わるので，編集用の一覧も保存した順に合わせる
            notes.Clear();
            notes.AddRange(data.ToChart().Notes);
            selected.Clear();
            dirty = false;
            savedTempo = data.TempoMap();
            if (song.timeline != null) SongTimelineSync.Sync(song.timeline, data, song.LengthSec);
            Debug.Log($"[ChartEditor] 保存: {path}（{notes.Count} ノーツ）");
        }

        void BuildWaveform()
        {
            var clip = song != null ? song.audio : null;
            if (clip == null) return;
            if (clip.loadType != AudioClipLoadType.DecompressOnLoad || !clip.LoadAudioData())
            {
                Debug.LogWarning($"[ChartEditor] {clip.name} の波形を読めない（Load Type を Decompress On Load にすると表示できる）");
                return;
            }
            var samples = new float[clip.samples * clip.channels];
            if (!clip.GetData(samples, 0)) return;
            waveSecPerBin = 0.01f;
            int bins = Mathf.CeilToInt(clip.length / waveSecPerBin);
            waveMin = new float[bins];
            waveMax = new float[bins];
            int perBin = Mathf.Max(1, (int)(clip.frequency * waveSecPerBin));
            for (int b = 0; b < bins; b++)
            {
                float lo = 0, hi = 0;
                int s0 = b * perBin, s1 = Mathf.Min(clip.samples, s0 + perBin);
                for (int i = s0; i < s1; i++)
                {
                    float v = 0;
                    for (int c = 0; c < clip.channels; c++) v += samples[i * clip.channels + c];
                    v /= clip.channels;
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                }
                waveMin[b] = lo;
                waveMax[b] = hi;
            }
        }

        // ---- 編集（元に戻す付き） ----

        // 元に戻す用: ノーツと区切りを文字列にする（"ノーツ|区切り"）
        string Snapshot() =>
            string.Join(";", notes.Select(n => string.Format(CultureInfo.InvariantCulture, "{0:R},{1},{2},{3}", n.Time, n.Lane, (int)n.Type, n.Flow))) + "|" +
            string.Join(";", sections.Select(x => string.Format(CultureInfo.InvariantCulture, "{0:R},{1}", x.Time, Uri.EscapeDataString(x.Name))));

        void Restore(string snapshot)
        {
            notes.Clear();
            selected.Clear();
            var halves = snapshot.Split('|');
            sections.Clear();
            if (halves.Length > 1 && halves[1].Length > 0)
            {
                foreach (var part in halves[1].Split(';'))
                {
                    var f = part.Split(',');
                    sections.Add(new SongSection { Time = double.Parse(f[0], CultureInfo.InvariantCulture), Name = Uri.UnescapeDataString(f[1]) });
                }
            }
            string s = halves[0];
            if (s.Length == 0) return;
            foreach (var part in s.Split(';'))
            {
                var f = part.Split(',');
                notes.Add(new ChartNote(double.Parse(f[0], CultureInfo.InvariantCulture), int.Parse(f[1]), (NoteType)int.Parse(f[2]), f.Length > 3 ? int.Parse(f[3]) : 0));
            }
        }

        void RecordUndo()
        {
            undo.Add(Snapshot());
            if (undo.Count > 200) undo.RemoveAt(0);
            redo.Clear();
            dirty = true;
        }

        void Undo()
        {
            if (undo.Count == 0) return;
            redo.Add(Snapshot());
            Restore(undo[undo.Count - 1]);
            undo.RemoveAt(undo.Count - 1);
            dirty = true;
        }

        void Redo()
        {
            if (redo.Count == 0) return;
            undo.Add(Snapshot());
            Restore(redo[redo.Count - 1]);
            redo.RemoveAt(redo.Count - 1);
            dirty = true;
        }

        double SnapTime(double t) => snap ? tempo.Snap(t, Division) : Math.Round(t, 3);

        int AddNote(double t, int lane)
        {
            // 同じ場所に既にあれば置かない（ダブルクリックや連打で重なるのを防ぐ）
            int existing = notes.FindIndex(n => n.Lane == lane && Math.Abs(n.Time - t) < 1e-4);
            if (existing >= 0) return existing;
            RecordUndo();
            notes.Add(new ChartNote(t, lane, NoteType.Touch));
            return notes.Count - 1;
        }

        void DeleteSelected()
        {
            if (selected.Count == 0) return;
            RecordUndo();
            foreach (int i in selected.OrderByDescending(i => i)) notes.RemoveAt(i);
            selected.Clear();
        }

        void MoveSelected(double dt, int dLane)
        {
            if (selected.Count == 0) return;
            if (selected.Any(i => notes[i].Lane + dLane < 0)) return;
            RecordUndo();
            foreach (int i in selected)
                notes[i] = new ChartNote(Math.Max(0, notes[i].Time + dt), notes[i].Lane + dLane, notes[i].Type, notes[i].Flow);
        }

        // ---- 座標 ----

        int LaneCount
        {
            get
            {
                int n = 11;
                if (notes.Count > 0) n = Math.Max(n, notes.Max(x => x.Lane) + 1);
                if (wiredLanes != null && wiredLanes.Count > 0) n = Math.Max(n, wiredLanes.Max() + 1);
                return n;
            }
        }

        Rect area;
        float LanesTop => area.y + RulerH + WaveH;
        float XOf(double t) => area.x + Gutter + (float)((t - scrollSec) * pxPerSec);
        double TimeOf(float x) => scrollSec + (x - area.x - Gutter) / pxPerSec;
        float YOfLane(int lane) => LanesTop + lane * LaneH;
        int LaneOf(float y) => Mathf.FloorToInt((y - LanesTop) / LaneH);

        double SongLength => song != null && song.LengthSec > 0 ? song.LengthSec : (notes.Count > 0 ? notes.Max(n => n.Time) + 5 : 60);

        /// <summary>再生中なら再生位置（曲時刻），止まっていれば NaN．</summary>
        double PlayheadSec
        {
            get
            {
                double p = EditorAudio.Position;
                return double.IsNaN(p) ? double.NaN : p - (data?.AudioStartSec ?? 0);
            }
        }

        const string MetronomeDir = "Assets/ArteTemp";
        const string MetronomePath = MetronomeDir + "/metronome.wav";

        sealed class SectionNamePopup : PopupWindowContent
        {
            string name;
            readonly Action<string> done;

            public SectionNamePopup(string current, Action<string> done)
            {
                name = current;
                this.done = done;
            }

            public override Vector2 GetWindowSize() => new Vector2(240, 48);

            public override void OnGUI(Rect rect)
            {
                GUI.SetNextControlName("name");
                name = EditorGUILayout.TextField("名前", name);
                EditorGUI.FocusTextInControl("name");
                bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
                if ((GUILayout.Button("決める") || enter) && !string.IsNullOrWhiteSpace(name))
                {
                    done(name.Trim());
                    editorWindow.Close();
                }
            }
        }

        double CurrentBpm
        {
            get
            {
                var pts = tempo.Points;
                double t = selected.Count > 0 ? notes[selected.First()].Time : cursorSec;
                var p = pts.LastOrDefault(x => x.TimeSec <= t) ?? pts[0];
                return p.Bpm;
            }
        }

        // ---- テンポ表（TempoTableWindow）から使う ----

        internal SongData Data => data;
        internal double CursorSec => cursorSec;

        internal void ReplaceTempo(List<TempoPoint> points)
        {
            data.Tempo = points.OrderBy(p => p.TimeSec).Select(p => new TempoPoint { TimeSec = p.TimeSec, Bpm = p.Bpm, BeatsPerBar = p.BeatsPerBar }).ToList();
            tempo = data.TempoMap();
            dirty = true;
            Repaint();
        }
    }
}
