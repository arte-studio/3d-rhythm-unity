using NUnit.Framework;
using System.Collections;
//using System.Drawing;
using Unity.VisualScripting;
#if UNITY_EDITOR
using UnityEditor.Experimental.GraphView;
#endif
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

[System.Serializable] //クラス・構造体をシリアライズ可能にする、データの保存・転送、Unityのインスペクタ表示
public class NoteData
{
    public float time; //ノーツを押す時間
    public int lane;   // Sphereノート用 (0～10)
    //public int from;   // ライン始点 (ラインノート用)
    //public int to;     // ライン終点 (ラインノート用)
    public string type; // "touch", "connect"
}

[System.Serializable]
public class NotesWrapper //JSON からデータを読み込むためのラッパークラス、JsonUtility.FromJson<T>() は トップレベルが配列の場合は直接読み込めないのでオブジェクトかする必要がある
{
    public NoteData[] notes;
}

// GameManager.cs のクラス外（または内部）に追加

public class ActiveNote
{
    //touchノーツ用の状態管理
    public GameObject NoteObject;   // 画面に表示中のノーツオブジェクト
    public NoteData Data;           // 対応する譜面データ
    public bool IsUsed;             // 判定済みフラグ（notes_isused[index] の代わり）
    public TouchNotes_Flag FlagComponent; // フラグコンポーネントへの参照を保持
    //public NoteLeds noteLeds; // LED制御用コンポーネントへの参照

    // Connect ノーツ用の状態管理 (ConnectNotes_Judge.csから移植)
    /*public bool Connect_DragStarted = false;
    public bool Connect_DragEnded = false;
    public bool Connect_CubeTouched = false;
    public float Connect_ElapsedTime = 0f;
    public float Connect_TotalJudgeTime = 0f;
    public float Connect_RequiredTime = 3f; // 必要ドラッグ時間
    public float Connect_JudgeEndOffset = 1f; // 時間切れまでの許容時間
    public float Connect_JudgeEndTime;
    public float Connect_MissAbsoluteTime;*/

    public ActiveNote(GameObject obj, NoteData data, int index)
    {
        NoteObject = obj;
        Data = data;
        IsUsed = false;

        // Connectノーツの場合、TimeとJudgeTimeRangeから終了時間を設定
        if (data.type == "connect")
        {
            /*Connect_JudgeEndTime = Connect_RequiredTime + Connect_JudgeEndOffset;
            Connect_DragStarted = false;
            Connect_DragEnded = false;
            Connect_CubeTouched = false;
            Connect_ElapsedTime = 0f;
            Connect_MissAbsoluteTime = data.time + Connect_RequiredTime + Connect_JudgeEndOffset;*/
            
        }

        //touchノーツの場合
        FlagComponent = obj.GetComponent<TouchNotes_Flag>();
        FlagComponent?.ResetFlag();

        // 初期状態として、待機色（黒など）に設定
        obj.GetComponent<Mugyu_LEDPerformance>()?.SetAllLEDColor(Color.black);
    }
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance; //クラス全体で共有される唯一のインスタンス

    // --- ★ここから修正 (デバッグモード) ---
    [Header("Debug Settings")]
    // [Tooltip("デバッグモードを有効にする（ノーツLEDの動作確認用）")]
    // [SerializeField] private bool isDebugMode = false; // private から public に変更
    public bool isDebugMode = false;
    // --- ★修正ここまで ---

    [Header("Game Control")]
    [Tooltip("自動でゲームを開始するか。Inspectorで切り替え可能。")]
    public bool autoStart = false;
    [Tooltip("Scene の VideoPlayer を参照 (任意)。未設定時は自動検索します)")]
    public UnityEngine.Video.VideoPlayer videoPlayer;

    /* 音源ソース */
    [Header("MusicSource")]
    public AudioSource Game_MusicSource;      // 本番のゲームの音源オブジェクトのAudioSourceをセット
    public AudioSource perfectgood_EffectSource;      // 本番のゲームの音源オブジェクトのAudioSourceをセット

    /* 音楽の時間系 */
    [HideInInspector]
    private double StartTime = 0; // 音楽再生開始時刻
    // UnityのAudioSourceは基本的にAudioSettings.dspTimeと連携して動作するため、ここは変更なし
    public double CurrentTime => AudioSettings.dspTime - StartTime; //現在の音楽の再生時間、他クラスから読み取り可能

    /* 譜面データ */
    [Header("NotesData_filename")]
    public string Game_NotesData; //本番用譜面データのファイル名

    /* ノーツ判定処理系 */
    [HideInInspector]
    public float targetTime; //ノーツが押されるべき時間

    /* “タッチ”ノーツの判定の厳しさパラメータ */
    [Header("Judge Settings - Sphere")]
    public double perfectRange = 1.0f; //Perfectの範囲内の時間
    public double goodRange = 3.0f; //Goodの範囲内の時間
    public double missRange = 0.5f; //Missの範囲内の時間

    [HideInInspector]
    public float notesignalTime = 3f;      // スタートする時間の何秒前から合図を出すか

    /* ノーツの分類とか */
    [HideInInspector]
    public int laneIndex; //このSphereが属するレーン番号
    //private NoteData[] notes;
    bool[] notes_isused;
    //private int noteIndex; //ノーツが来る番号

    /* 本番ゲームのスコア */
    private int Touch_score = 0; //“タッチ”によるスコア

    /* 判定ごとのスコア */
    [Header("Judge Settings - Score")]
    public int Perfect_score = 5; //“Perfect”のときのスコア
    public int Good_score = 3; //“Good”のときのスコア

    /* むぎゅとつながるノーツの演出用のインスタンス */
    private System.Collections.Generic.List<Mugyu_LEDPerformance> mugyu_LEDPerformance = new System.Collections.Generic.List<Mugyu_LEDPerformance>();
    private System.Collections.Generic.List<Connect_LEDPerformance> connect_LEDPerformance = new System.Collections.Generic.List<Connect_LEDPerformance>();
    UdpController udpController;

    /* コネクトLED演出設定 */
    [Header("Connect LED Performance")]
    [Tooltip("H値(色相)の最小値 (0-360)")]
    [UnityEngine.Range(0, 360)] public float connectHMin = 185f;
    [Tooltip("H値(色相)の最大値 (0-360)")]
    [UnityEngine.Range(0, 360)] public float connectHMax = 340f;
    [Tooltip("S値(彩度) (0-1)")]
    [UnityEngine.Range(0f, 1f)] public float connectSaturation = 1.0f;
    [Tooltip("V値(明度) (0-1)")]
    [UnityEngine.Range(0f, 1f)] public float connectValue = 1.0f; // B: Brightness / V: Value
    [Tooltip("色相変化の速さ (大きいほど速い)")]
    public float connectSpeed = 0.5f;

    //再生中かどうか
    bool isplaying = false;
    // 一時停止フラグ
    private bool isPaused = false;
    // 一時停止時の DSP 時刻
    private double pauseDspTime = 0.0;

    public bool IsPlaying => isplaying;
    public bool IsPaused => isPaused;
    public int CurrentScore => Touch_score;

    // ゲーム開始 (まだ開始していない場合)、一時停止中なら再開
    public void StartGame()
    {
        Debug.Log("StartGame() called. isplaying=" + isplaying + " isPaused=" + isPaused);

        if (isPaused)
        {
            Debug.Log("Resuming game from paused state.");
            ResumeGame();
            return;
        }

        // Visuals / LED が初期化されているかを念のため確認してから開始
        try
        {
            if (mugyu_LEDPerformance != null)
            {
                foreach (var m in mugyu_LEDPerformance)
                {
                    if (m != null) m.SetLEDGenerate();
                }
            }
            if (connect_LEDPerformance != null)
            {
                foreach (var c in connect_LEDPerformance)
                {
                    if (c != null) c.SetLEDGenerate();
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("Error while initializing LED visuals: " + ex.Message);
        }

        if (!isplaying)
        {
            if (Game_MusicSource == null)
            {
                Debug.LogError("Cannot start game: Game_MusicSource is not assigned in the inspector.");
                return;
            }

            // 動画があれば再生を開始
            if (videoPlayer == null)
            {
                videoPlayer = FindObjectOfType<UnityEngine.Video.VideoPlayer>();
            }
            if (videoPlayer != null)
            {
                try
                {
                    videoPlayer.Play();
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("VideoPlayer Play failed: " + ex.Message);
                }
            }

            Debug.Log("Starting GameFlow coroutine from StartGame().");
            StartCoroutine(GameFlow());
        }
    }

    // 一時停止
    public void PauseGame()
    {
        if (!isplaying || isPaused) return;
        // 音楽を一時停止し、DSP 時刻を保存
        if (Game_MusicSource != null && Game_MusicSource.isPlaying)
        {
            Game_MusicSource.Pause();
        }
        pauseDspTime = AudioSettings.dspTime;
        isPaused = true;
    }

    // 再開
    public void ResumeGame()
    {
        if (!isPaused) return;
        // 再開時には StartTime をシフトして、CurrentTime が途切れないようにする
        double resumeDsp = AudioSettings.dspTime;
        double pausedDuration = resumeDsp - pauseDspTime;
        StartTime += pausedDuration;
        if (Game_MusicSource != null)
        {
            Game_MusicSource.UnPause();
        }
        // 動画も再開
        if (videoPlayer == null) videoPlayer = FindObjectOfType<UnityEngine.Video.VideoPlayer>();
        if (videoPlayer != null)
        {
            try { videoPlayer.Play(); } catch { }
        }
        isPaused = false;
    }

    // リセット: シーンをリロードして初期化する（簡潔で安全な方法）
    public void ResetGame()
    {
        // 動画が再生中なら停止しておく
        if (videoPlayer != null)
        {
            try { videoPlayer.Stop(); } catch { }
        }
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
    private NoteData[] notes;
    private ActiveNote[] activeNotes; //  譜面データ全てのActiveNoteを管理
    private int lastSpawnedNoteIndex = 0; // 最後にプールからオブジェクトを割り当てたノーツのJSONインデックス

    private NoteLeds noteLeds;
    
    // --- Brightness settings (inspector) ---
    [Header("Brightness Settings")] 
    [UnityEngine.Range(0,255)] public int brightnessPerformance = 5; // 演出用明るさ (0-255)
    [UnityEngine.Range(0,255)] public int brightnessNotes = 10; // ノーツ用明るさ (0-255)
    private Coroutine brightnessCoroutine;
    
    // --- ここから追加 (タッチ判定連携) ---
    [Header("Touch Input Settings")]
    [Tooltip("同じレーンへの連続タッチを防ぐクールダウン時間(秒)")]
    [SerializeField] private float touchInputCooldown = 0.1f; // 0.1秒
    
    // 各レーン (0-10) のクールダウンタイマー
    private float[] laneCooldowns;
    // --- 追加ここまで ---


    //ゲームオブジェクトが生成された直後、Startより前に1回だけ呼ばれる
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this; //唯一のインスタンスを生成する
        }
        else
        {
            Destroy(gameObject); // 既にインスタンスが存在する場合は破棄
        }
    }

    IEnumerator Start()
    {
        Touch_score = 0;
        
        // 修正: レーンクールダウン配列の初期化 (レーンが0-14の15個と仮定)
        // ConvertNoteIdToHard/Game の実装に基づき 15 に変更
        laneCooldowns = new float[15]; 
        
        // ObjectRelocationの生成完了を待つ
        yield return new WaitForSeconds(0.1f);

        // touch_notesを取得
        ObjectRelocation relocation = ObjectRelocation.Instance;
        if (relocation != null && relocation.spawnedNotes.Count > 0)
        {
            System.Collections.Generic.List<GameObject> Notes = relocation.spawnedNotes;

            for (int i = 0; i < Notes.Count; i++)

            {
                Debug.Log(Notes[i].name);
                mugyu_LEDPerformance.Add(Notes[i].GetComponent<Mugyu_LEDPerformance>());
                if (mugyu_LEDPerformance[i] != null) mugyu_LEDPerformance[i].SetLEDGenerate();

                connect_LEDPerformance.Add(Notes[i].GetComponent<Connect_LEDPerformance>());
                if (connect_LEDPerformance[i] != null)
                {
                    connect_LEDPerformance[i].SetLEDGenerate();
                    connect_LEDPerformance[i].SetAllLEDColor(Color.black);
                }
            }
        }
        udpController = FindFirstObjectByType<UdpController>(); // ★修正: GameObject.Findを避ける
        noteLeds = FindFirstObjectByType<NoteLeds>(); // ★修正: GameObject.Findを避ける

        // GameFlow開始
        // 明るさ送信コルーチンを開始 (5秒ごと)
        if (brightnessCoroutine == null)
        {
            brightnessCoroutine = StartCoroutine(BrightnessSenderCoroutine());
        }

        // デバッグモードが有効なら、NoteLedsに開始を指示
        if (isDebugMode && noteLeds != null)
        {
            noteLeds.StartDebugMode();
            Debug.Log("デバッグモードに入りました");
        }

        // autoStart が true の場合のみ自動で GameFlow を開始する
        if (autoStart && !isDebugMode)
        {
            StartCoroutine(GameFlow());
            Debug.Log("autoStart により自動開始します");
        }
    }

    //チュートリアルから本番までやる流れの全体の処理
    private IEnumerator GameFlow()
    {
        // ゲーム終了まで待機
        yield return StartCoroutine(Game());

    }

    //実行
    private IEnumerator Game()
    {
        
        Coroutine connectLEDCoroutine = StartCoroutine(ConnectLEDPerform());　// Connectノーツの演出コルーチンの開始 

        LoadNotesFromJson(Game_NotesData); //譜面データ読み込み
        yield return StartCoroutine(MusicPlayer(Game_MusicSource)); //音楽を再生する

        // 音楽の再生終了を確認する
        while (Game_MusicSource.isPlaying)
        {
            yield return null;
        }

        //演出コルーチンの停止
        if (connectLEDCoroutine != null)
        {
            StopCoroutine(connectLEDCoroutine);
        }
        ScoreCalculate();

    }

    //JSON ファイルからノーツデータを読み込む
    void LoadNotesFromJson(string fileName)
    {
        TextAsset jsonFile = Resources.Load<TextAsset>(fileName); // Asset/Resources/内にある譜面データを読み込む
        NotesWrapper wrapper = JsonUtility.FromJson<NotesWrapper>(jsonFile.text); //JsonUtility.FromJson で文字列をCのクラスに変換し、NotesWrapperクラスのインスタンスに代入
        notes = wrapper.notes; //JSON から読み込んだ ノーツ配列を GameManagerのプライベート変数のnotes 配列に代入
        //  修正点: ActiveNote配列を初期化
        activeNotes = new ActiveNote[notes.Length];
        notes_isused = new bool[notes.Length];
        for (int i = 0; i < notes_isused.Length; i++) notes_isused[i] = false;
    }

    private void Update()
    {
        // 追加: クールダウンタイマーの更新
        if (isplaying)
        {
            for (int i = 0; i < laneCooldowns.Length; i++)
            {
                if (laneCooldowns[i] > 0)
                {
                    laneCooldowns[i] -= Time.fixedDeltaTime;
                }
            }
        }

        // --- ここから追加 (デバッグモード) ---
        if (isDebugMode)
        {
            if (udpController != null && noteLeds != null)
            {
                // UdpController の touchStates (ハードウェア入力) をスキャン
                for (int dev = 0; dev < 8; dev++) // NUM_DEVICES
                {
                    if (udpController.touchStates[dev] == null) continue;
                    for (int sen = 0; sen < 5; sen++) // NUM_TOUCH
                    {
                        // ハードID (deviceId + innerId) を 譜面レーンID (0-16) に変換
                        NoteLeds.HardId hard = new NoteLeds.HardId(dev, sen);
                        int lane = noteLeds.ConvertNoteIdToGame(hard);
                        
                        if (lane != -1) // -1 は無効レーン
                        {
                            // 押されているかどうか
                            bool isTouching = udpController.touchStates[dev][sen];
                            // NoteLedsにタッチ状態を通知
                            noteLeds.SetDebugTouchState(lane, isTouching);
                        }
                    }
                }
            }
            // デバッグモード中は通常のノーツ判定をスキップ
            return;
        }
        // --- 追加ここまで ---
        
        if (isplaying) //ノーツがすべて終わっていなければ
        {
            TouchNotes_judge();
        }

    }

    //MusicSourceを再生して、開始時刻を記録する
    private IEnumerator MusicPlayer(AudioSource MusicSource)
    {
        //音楽再生
        MusicSource.Play();

        // DSPタイムで開始時刻を記録
        StartTime = AudioSettings.dspTime;

        // 再生状態になるまで待機
        while (!MusicSource.isPlaying)
        {
            yield return null; // 1フレーム待つ
        }
        isplaying = true;
        //ノーツのインデックスを0に初期化
        //noteIndex = 0;

        lastSpawnedNoteIndex = 0; // プールから割り当てたノーツインデックスを初期化

        while (MusicSource.isPlaying)
        {
            // ノーツ出現ロジック
            while (lastSpawnedNoteIndex < notes.Length && notes[lastSpawnedNoteIndex].time <= CurrentTime + notesignalTime)
            {
                NoteData noteData = notes[lastSpawnedNoteIndex];

                // ノーツオブジェクトをプールから取得・再利用
                GameObject noteObject = ObjectRelocation.Instance.GetNextAvailableNote(noteData.type);


                if (noteObject != null)
                {
                    // 譜面データと取得したオブジェクトを紐付けて ActiveNote を作成
                    ActiveNote activeNote = new ActiveNote(noteObject, noteData, lastSpawnedNoteIndex);
                    activeNotes[lastSpawnedNoteIndex] = activeNote;
                    Debug.Log($"Note assigned: Index {lastSpawnedNoteIndex}, Lane {noteData.lane}, Type: {noteData.type}");
                }
                else
                {
                    Debug.LogError($"Note Object for type '{noteData.type}' not available in pool. Check ObjectRelocation's JSON and mapping.");
                }

                lastSpawnedNoteIndex++;
            }

            yield return null;
        }
        isplaying = false;

    }

    private void EffectPlayer(AudioSource MusicSource)
    {
        //音楽再生
        MusicSource.Play();
    }

    private void TouchNotes_judge()
    {
        //  修正点: activeNotes 配列をループし、ノーツの判定を行う
        for (int notenum = 0; notenum < activeNotes.Length; notenum++)
        {
            ActiveNote currentActiveNote = activeNotes[notenum];

            // 判定処理が必要なノーツかチェック
            // 1. ノーツが譜面データに存在し、
            // 2. まだ判定されておらず (IsUsed=false)、
            // 3. かつ、ノーツが出現済み（オブジェクトが割り当て済み）の場合
            if (currentActiveNote == null || currentActiveNote.IsUsed || currentActiveNote.Data.type != "touch")
                continue;

            float targetTime = currentActiveNote.Data.time;
            float diff = (float)(CurrentTime - targetTime);

            // TouchNotes_judge() の for ループの先頭付近
            /* 
            if (notenum < activeNotes.Length && activeNotes[notenum] != null)
            {
                Debug.Log($"Checking Note Index: {notenum}, IsUsed: {activeNotes[notenum].IsUsed}");
            }
            //*/

            // ノーツの判定時間かどうか
            if ((CurrentTime >= targetTime - goodRange)&&(CurrentTime <= targetTime + goodRange + missRange))
            {
                //  Perfect/Good/Missの判定ロジック (currentActiveNote.FlagComponent.TouchFlag を利用)
                bool judged = false;

                // Perfect判定
                if (Mathf.Abs(diff) <= perfectRange && currentActiveNote.FlagComponent.TouchFlag)
                {
                    judged = true;

                    Touch_score += Perfect_score;
                    Debug.Log($"PERFECT! lane {currentActiveNote.Data.lane} Time: {CurrentTime:F3}");

                    //色変化
                    //currentActiveNote.NoteObject.GetComponent<Mugyu_LEDPerformance>()?.SetAllLEDColor(Color.magenta);
                    PerfectPerformance(currentActiveNote.NoteObject);
                    //noteLeds.SetAllMuguColors(currentActiveNote.Data.lane, Color.magenta);//notenum ではなく lane を渡す
                    noteLeds.SetAllMuguColors(currentActiveNote.Data.lane, Color.magenta);
                    //音変化
                    EffectPlayer(perfectgood_EffectSource);
                }
                // Good判定
                else if (Mathf.Abs(diff) <= goodRange && currentActiveNote.FlagComponent.TouchFlag)
                {
                    judged = true;

                    Touch_score += Good_score;
                    Debug.Log($"GOOD! lane {currentActiveNote.Data.lane} Time: {CurrentTime:F3}");

                    //色変化
                    //currentActiveNote.NoteObject.GetComponent<Mugyu_LEDPerformance>()?.SetAllLEDColor(Color.white);
                    noteLeds.SetAllMuguColors(currentActiveNote.Data.lane, Color.white);// notenum ではなく lane を渡す
                    //音変化
                    EffectPlayer(perfectgood_EffectSource);
                }
                // Miss判定 (時間切れ)
                else if (CurrentTime > targetTime + missRange)
                {
                    judged = true;

                    Debug.Log($"MISS! lane {currentActiveNote.Data.lane} Time: {CurrentTime:F3}");

                    //色変化
                    //currentActiveNote.NoteObject.GetComponent<Mugyu_LEDPerformance>()?.SetAllLEDColor(Color.cyan);
                    noteLeds.SetAllMuguColors(currentActiveNote.Data.lane, Color.cyan);// notenum ではなく lane を渡す
                }

                //判定確定後
                if (judged)
                {
                    currentActiveNote.IsUsed = true;
                    currentActiveNote.FlagComponent.ResetFlag();

                    if (currentActiveNote.NoteObject != null)
                    {
                        // 判定後、0.5秒後に色を黒に戻す処理を開始
                        StartCoroutine(ResetNoteColorAfterDelay(currentActiveNote.NoteObject, 0.5f));
                    }
                }

                //  判定可能範囲内での色変化
                else if (Mathf.Abs(diff) <= targetTime + goodRange)
                {
                    currentActiveNote.NoteObject.GetComponent<Mugyu_LEDPerformance>()?.SetAllLEDColor(Color.yellow);
                    // 修正: notenum ではなく lane を渡す
                    noteLeds.SetAllMuguColors(currentActiveNote.Data.lane, Color.yellow);
                }
            }
        }
    }

    public IEnumerator ResetNoteColorAfterDelay(GameObject noteObject, float delay)
    {
        // 指定された時間だけ実行を一時停止
        yield return new WaitForSeconds(delay);

        // 待機後、ノーツの色をリセット
        if (noteObject != null)
        {
            // Mugyu_LEDPerformanceコンポーネントを取得
            var ledPerformance = noteObject.GetComponent<Mugyu_LEDPerformance>();

            if (ledPerformance != null)
            {
                // ノーツが再利用可能状態（非アクティブ/黒色）に戻るように色を設定
                ledPerformance.SetAllLEDColor(Color.black);
                // 必要に応じてノーツオブジェクトをプールに戻す処理などを追加できます。
                // noteObject.SetActive(false);
            }
        }
    }

    // PERFECT時にMugyuモジュールのLED Matrixを虹色回転アニメーションで点灯させる演出
    // <param name="noteObject">PERFECT判定となったノーツオブジェクト</param>
    private void PerfectPerformance(GameObject noteObject)
    {
        if (noteObject == null) return;

        var mugyuPerf = noteObject.GetComponent<Mugyu_LEDPerformance>();
        var matrixGenerator = noteObject.GetComponentInChildren<LEDMatrixGenerator>();

        if (mugyuPerf != null && matrixGenerator != null)
        {
            // 既存のコルーチンがあれば停止し、新しいアニメーションを開始
            // 注意: 実行中のアニメーションを管理する辞書などがないため、ここでは単純に開始のみ。
            //         もし連打でアニメーションが上書きされるのが問題なら、管理が必要です。

            StartCoroutine(RunPerfectRainbowAnimation(mugyuPerf, matrixGenerator, 0.2f)); // 0.5秒間アニメーション
        }
    }



    /// Mugyu LED Matrixを色相変化しながら回転させるコルーチン
    private IEnumerator RunPerfectRainbowAnimation(Mugyu_LEDPerformance mugyuPerf, LEDMatrixGenerator matrixGenerator, float duration)
    {
        float startTime = Time.time;
        GameObject[,] frontLEDs = matrixGenerator.GetFrontLEDs();

        if (frontLEDs == null || frontLEDs.GetLength(0) == 0) yield break;

        int rows = frontLEDs.GetLength(0);
        int cols = frontLEDs.GetLength(1);

        // HSB の S(彩度) と V(明度) は最大 (1.0) に設定
        const float S = 1.0f;
        const float V = 1.0f;

        // Matrix の中心座標 (8x8 の場合、(3.5, 3.5))
        float centerX = (cols - 1) / 2.0f;
        float centerY = (rows - 1) / 2.0f;

        while (Time.time < startTime + duration)
        {
            // 1. 時間経過に基づくH値の全体オフセット (色調の変化速度)
            float timeOffset = (Time.time - startTime) * 0.8f; // 0.8f は回転速度

            // 2. LED Matrixの走査と色設定
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    // LEDの位置 (中心からの相対座標)
                    float x = c - centerX;
                    float y = r - centerY;

                    // 3. 角度と距離を計算 (秒針/回転アニメーションの基盤)
                    // 角度 (θ): Math.Atan2(y, x) でラジアンを取得 (円運動)
                    float angle = Mathf.Atan2(y, x);
                    // 距離 (r): 中心からの距離
                    float distance = Mathf.Sqrt(x * x + y * y);

                    // 4. H値を計算
                    // H値 = (角度に基づくグラデーション) + (時間経過による変化)
                    // angleは -π から π なので、0〜1に正規化: (angle / (2 * Mathf.PI)) + 0.5f
                    float angleNormalized = (angle / (2 * Mathf.PI)) + 0.5f;

                    // H値の最終決定: 時間オフセットを加算し、0〜1の範囲にクランプ
                    float hValue = (angleNormalized + timeOffset) % 1.0f;

                    // 5. 色を設定
                    Color rainbowColor = Color.HSVToRGB(hValue, S, V);
                    mugyuPerf.SetLEDColor(r, c, rainbowColor);
                    //noteLeds.SetAllMuguColors(currentActiveNote.Data.lane, Color.magenta);
                }
            }

            yield return new WaitForSeconds(0.01f); // 1フレーム待機
        }

        // アニメーション終了後、黒に戻す
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                mugyuPerf.SetLEDColor(r, c, Color.black);
            }
        }
    }

    // コネクトノーツのLED色を曲中にH値で変化させ続ける演出コルーチン(1つのオブジェクト内で色変化し続ける)
    private IEnumerator ConnectLEDPerform()
    {
        // Inspectorの設定値を取得し、0-1の範囲に正規化
        float currentH = connectHMin / 360f;
        float minH = connectHMin / 360f;
        float maxH = connectHMax / 360f;
        float speed = connectSpeed;

        int direction = 1; // 1: 増加方向, -1: 減少方向

        int currentLEDIndex = 0; // 現在色を変更するLEDのインデックス (0〜29)
        int totalLEDs = 30; // ConnectLEDGenerator.cols の値に合わせる

        // 音楽再生中のみ実行
        while (Game_MusicSource.isPlaying)
        {
            // === H値の更新（全体の色の基調変化）===
            currentH += Time.deltaTime * speed * direction;

            // 範囲チェックと方向転換
            if (currentH >= maxH)
            {
                currentH = maxH;
                direction = -1;
            }
            else if (currentH <= minH)
            {
                currentH = minH;
                direction = 1;
            }

            // 3. HSB (Hue, Saturation, Brightness) から Color に変換
            // 明度と彩度には Inspector の設定値を使用
            Color targetColor = Color.HSVToRGB(currentH, connectSaturation, connectValue);

            // === 個々の LED への適用 ===

            // 4. Connect オブジェクトの currentLEDIndex に新しい色を適用
            foreach (var connectPerf in connect_LEDPerformance)
            {
                if (connectPerf != null)
                {
                    // Connect_LEDPerformance の SetLEDColor を使用し、
                    // 現在のインデックスのLEDだけ色を更新
                    connectPerf.SetLEDColor(0, currentLEDIndex, targetColor);
                    //noteLeds.SetAllMuguColors(currentActiveNote.Data.lane, Color.yellow);
                    noteLeds.SetConColor(0, currentLEDIndex, targetColor);
                }
            }

            // 次の LED にインデックスを進める (30個なので 0〜29 をループ)
            currentLEDIndex = (currentLEDIndex + 1) % totalLEDs;

            
            yield return null;
        }

        // 音楽が終了したら、全ての LED を黒に戻す（全LEDをループ処理）
        for (int i = 0; i < totalLEDs; i++)
        {
            foreach (var connectPerf in connect_LEDPerformance)
            {
                connectPerf?.SetLEDColor(0, i, Color.black);
            }
        }
        // 最後に一度、黒のデータを送信させるために少し待つ
        yield return new WaitForSeconds(0.1f);
    }

    /// <summary>
    /// 5秒ごとに明るさを送信するコルーチン
    /// </summary>
    private IEnumerator BrightnessSenderCoroutine()
    {
        // 初回は即送信し、その後Waitで5秒ごと
        while (true)
        {
            if (udpController != null)
            {
                try
                {
                    udpController.SendBrightness((byte)brightnessPerformance, (byte)brightnessNotes);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"BrightnessSenderCoroutine error: {e.Message}");
                }
            }
            yield return new WaitForSeconds(5f);
        }
    }

    // --- ★ここから追加 (タッチ判定連携) ---

    /// <summary>
    /// UdpControllerからタッチイベントを受け取り、対応するノーツのフラグを立てる
    /// </summary>
    /// <param name="deviceId">タッチされたデバイスID (0-7)</param>
    /// <param name="sensorId">タッチされたセンサーID (0-4)</param>
    public void HandleTouchInput(int deviceId, int sensorId)
    {
        // 1. デバイスIDとセンサーIDを、ゲーム内の「レーン番号」に変換
    // ★修正: ConvertNoteIdToGame はハードID (deviceId + innerId) を受け取る
    NoteLeds.HardId hardId = new NoteLeds.HardId(deviceId, sensorId);
    int lane = noteLeds.ConvertNoteIdToGame(hardId);
        
        if (lane == -1)
        {
            // Debug.LogWarning($"未定義のタッチ入力: Device={deviceId}, Sensor={sensorId}");
            return; // 未定義のマッピングなら何もしない
        }
        
        // 2. このレーンがクールダウン中でないかチェック (連打防止)
        if (laneCooldowns[lane] > 0)
        {
            return; // クールダウン中は入力を無視
        }
        
        // 3. クールダウンを設定
        laneCooldowns[lane] = touchInputCooldown;

        // 4. 現在判定可能な (activeNotes 内の) ノーツを探す
        ActiveNote targetNote = null;
        
        // 判定可能な時間内のノーツをすべて探す（近いもの優先など、ロジックは要調整）
        for (int i = 0; i < activeNotes.Length; i++)
        {
            ActiveNote note = activeNotes[i];
            
            // 既に判定済みか、出現前か、タイプが違うか、レーンが違うか
            if (note == null || note.IsUsed || note.Data.type != "touch" || note.Data.lane != lane)
            {
                continue;
            }

            // 判定可能時間内か
            float diff = (float)(CurrentTime - note.Data.time);
            if ((CurrentTime >= note.Data.time - goodRange) && (CurrentTime <= note.Data.time + goodRange + missRange))
            {
                // ★ロジック改善の余地あり:
                // もし同じレーンに複数のノーツが判定可能な場合、
                // 最も 'time' が近いノーツを 'targetNote' に選ぶべき。
                // (現在は最初に見つかったものを採用している)
                targetNote = note;
                break; // とりあえず最初に見つかったものにフラグを立てる
            }
        }

        // 5. 該当するノーツが見つかったら、フラグを立てる
        if (targetNote != null)
        {
            // Debug.Log($"HandleTouchInput: Lane {lane} のノーツ (Time: {targetNote.Data.time}) にフラグを立てます。");
            targetNote.FlagComponent.SetClicked();
        }
        else
        {
            // Debug.Log($"HandleTouchInput: Lane {lane} に判定可能なノーツが見つかりません。");
            // (判定範囲外での空タッチ)
        }
    }

    /// <summary>
    /// (仮実装) デバイスIDとセンサーIDを、譜面データのレーン番号(0-10)に変換する
    /// ★★★ ここのマッピングは、実際のハードウェア仕様に合わせて必ず修正してください ★★★
    /// </summary>
    /// <returns>対応するレーン番号 (0-10)。見つからない場合は -1。</returns>
    private int ConvertDeviceAndSensorToLane(int deviceId, int sensorId)
    {
        // ※この関数は HandleTouchInput 内で noteLeds.ConvertNoteIdToGame を
        // 使うように変更されたため、現在は使用されていません。
        // ハードウェア仕様の参照用として残しておきます。
        
        // 例: デバイス0のセンサー0-4 が レーン0-4
        if (deviceId == 0)
        {
            if (sensorId >= 0 && sensorId <= 4)
            {
                return sensorId; // 0, 1, 2, 3, 4
            }
        }
        // 例: デバイス1のセンサー0-4 が レーン5-9
        else if (deviceId == 1)
        {
            if (sensorId >= 0 && sensorId <= 4)
            {
                return sensorId + 5; // 5, 6, 7, 8, 9
            }
        }
        // 例: デバイス2のセンサー0 が レーン10
        else if (deviceId == 2)
        {
            if (sensorId == 0)
            {
                return 10;
            }
        }
        
        // ... 他のデバイスのマッピングをここに追加 ...
        
        
        // 該当なし
        return -1;
    }
    
    // --- ★追加ここまで ---


    //合計スコアを計算する関数
    private void ScoreCalculate()
    {
        Debug.Log($"{Touch_score}");
    }  
}
