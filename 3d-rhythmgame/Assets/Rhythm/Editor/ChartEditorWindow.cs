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
    public sealed class ChartEditorWindow : EditorWindow
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
            string.Join(";", notes.Select(n => string.Format(CultureInfo.InvariantCulture, "{0:R},{1},{2}", n.Time, n.Lane, (int)n.Type))) + "|" +
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
                notes.Add(new ChartNote(double.Parse(f[0], CultureInfo.InvariantCulture), int.Parse(f[1]), (NoteType)int.Parse(f[2])));
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
                notes[i] = new ChartNote(Math.Max(0, notes[i].Time + dt), notes[i].Lane + dLane, notes[i].Type);
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
                if (data.Tempo.Count > 1) GUILayout.Label($"（他 {data.Tempo.Count - 1} 箇所のテンポ変化は song.json で編集）");
                GUILayout.FlexibleSpace();
                tapLatencyMs = EditorGUILayout.FloatField(new GUIContent("タップ補正(ms)", "キー入力で置くノーツを何ミリ秒早めるか"), tapLatencyMs, GUILayout.Width(160));
                pxPerSec = EditorGUILayout.Slider(pxPerSec, 20, 800, GUILayout.Width(160));
            }
        }

        void SetTempo(double bpm, double firstBeatSec, int beatsPerBar)
        {
            var p = new TempoPoint { TimeSec = firstBeatSec, Bpm = bpm, BeatsPerBar = beatsPerBar };
            if (data.Tempo.Count == 0) data.Tempo.Add(p);
            else data.Tempo[0] = p;
            tempo = data.TempoMap();
            dirty = true;
        }

        /// <summary>
        /// 曲に合わせて叩いた拍からテンポと小節の頭を合わせる（B = 拍，N = 小節の 1 拍目．計算は TapTempo）．
        /// 間が 2 秒以上空いたら新しく数え直す．
        /// </summary>
        void Tap(double t, bool downbeat)
        {
            if (taps.Count > 0 && t - taps[taps.Count - 1].Time > 2.0) taps.Clear();
            taps.Add(new TapTempo.Tap(t, downbeat));
            var current = data.Tempo.Count > 0 ? data.Tempo[0] : null;
            int bpb = current?.BeatsPerBar ?? 4;
            var r = TapTempo.Fit(taps, current, bpb);
            if (r == null)
            {
                tapMessage = $"タップ {taps.Count} 回（{TapTempo.MinTaps} 回から合わせ始める）";
                return;
            }
            var res = r.Value;
            SetTempo(res.KeptTempo ? current.Bpm : Math.Round(res.Bpm, 2), Math.Round(res.FirstBarSec, 4), bpb);
            int downs = taps.Count(x => x.Downbeat);
            tapMessage = $"タップ {taps.Count} 回（うち 1 拍目 {downs}）: " +
                         (res.KeptTempo ? $"テンポは今のまま（{current.Bpm} BPM）" : $"{res.Bpm:F2} BPM") +
                         $"，小節の頭 {res.FirstBarSec:F3} 秒（{res.ShiftSec * 1000:+0;-0} ms），ばらつき {res.ErrorSec * 1000:F0} ms" +
                         (downs == 0 ? "．1 拍目で N を叩くとどの拍が小節の頭かも合わせる" : "");
        }

        void SetMetronome(bool on)
        {
            metronome = on;
            double ph = PlayheadSec;
            if (!double.IsNaN(ph)) PlayFrom(ph);
        }

        /// <summary>曲時刻 t から鳴らす（メトロノームが入っていれば，曲にクリック音を混ぜた音を鳴らす．ずれないように 1 本の音にする）．</summary>
        void PlayFrom(double t)
        {
            var clip = metronome ? MetronomeClip() : song.audio;
            EditorAudio.Play(clip ?? song.audio, t + data.AudioStartSec);
        }

        AudioClip MetronomeClip()
        {
            var src = song.audio;
            if (src == null || !src.LoadAudioData()) return null;
            string key = $"{src.GetInstanceID()}:{string.Join(";", tempo.Points.Select(p => $"{p.TimeSec},{p.Bpm},{p.BeatsPerBar}"))}:{data.AudioStartSec}";
            if (metronomeClip != null && metronomeKey == key) return metronomeClip;
            int ch = src.channels, rate = src.frequency;
            var samples = new float[src.samples * ch];
            if (!src.GetData(samples, 0)) return null;
            // 曲を少し下げてクリック音を足す（小節の頭 2000Hz，ほかの拍 1200Hz，30ms で減衰）
            for (int i = 0; i < samples.Length; i++) samples[i] *= 0.7f;
            double length = (double)src.samples / rate - data.AudioStartSec;
            int clickLen = (int)(0.03 * rate);
            foreach (var (sec, isBeat, isBar) in tempo.GridLines(0, length, 1))
            {
                if (!isBeat && !isBar) continue;
                long start = (long)Math.Round((sec + data.AudioStartSec) * rate);
                double freq = isBar ? 2000 : 1200;
                float amp = isBar ? 0.6f : 0.4f;
                for (int k = 0; k < clickLen; k++)
                {
                    long s = start + k;
                    if (s < 0 || s >= src.samples) continue;
                    float v = amp * (float)(Math.Sin(2 * Math.PI * freq * k / rate) * Math.Exp(-k / (0.006 * rate)));
                    for (int c = 0; c < ch; c++) samples[s * ch + c] += v;
                }
            }
            // エディタの試聴はスクリプトで作った AudioClip を鳴らせないので，WAV に書いて読み込む（ArteTemp は git に入れない）
            Directory.CreateDirectory(MetronomeDir);
            WriteWav(MetronomePath, samples, ch, rate);
            AssetDatabase.ImportAsset(MetronomePath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(MetronomePath) is AudioImporter importer)
            {
                var settings = importer.defaultSampleSettings;
                if (settings.compressionFormat != AudioCompressionFormat.PCM || settings.loadType != AudioClipLoadType.DecompressOnLoad)
                {
                    settings.compressionFormat = AudioCompressionFormat.PCM;
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    importer.defaultSampleSettings = settings;
                    importer.SaveAndReimport();
                }
            }
            metronomeClip = AssetDatabase.LoadAssetAtPath<AudioClip>(MetronomePath);
            metronomeKey = metronomeClip != null ? key : null;
            return metronomeClip;
        }

        const string MetronomeDir = "Assets/ArteTemp";
        const string MetronomePath = MetronomeDir + "/metronome.wav";

        static void WriteWav(string path, float[] samples, int channels, int rate)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = samples.Length * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)channels);
                w.Write(rate);
                w.Write(rate * channels * 2);
                w.Write((short)(channels * 2));
                w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                w.Write(bytes);
                foreach (float v in samples) w.Write((short)(Mathf.Clamp(v, -1f, 1f) * 32767));
            }
        }

        void TogglePlay()
        {
            if (EditorAudio.IsPlaying)
            {
                double ph = PlayheadSec;
                EditorAudio.Stop();
                if (!double.IsNaN(ph)) cursorSec = ph;
            }
            else PlayFrom(cursorSec);
        }

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
                        foreach (int i in selected) notes[i] = new ChartNote(dragOriginal[i].Time + dt, dragOriginal[i].Lane + dl, dragOriginal[i].Type);
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
            Band(n.Time - rules.GoodSec, n.Time - rules.PerfectSec, new Color(1f, 1f, 1f, 0.10f));
            Band(n.Time - rules.PerfectSec, n.Time + rules.MissAfterSec, new Color(1f, 0.3f, 1f, 0.16f));
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
