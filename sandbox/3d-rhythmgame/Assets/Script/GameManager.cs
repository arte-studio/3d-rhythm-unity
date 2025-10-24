using NUnit.Framework;
using System.Collections;
using Unity.VisualScripting;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.Audio;

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
    public GameObject NoteObject;  // 画面に表示中のノーツオブジェクト
    public NoteData Data;          // 対応する譜面データ
    public bool IsUsed;            // 判定済みフラグ（notes_isused[index] の代わり）
    public TouchNotes_Flag FlagComponent; // フラグコンポーネンスへの参照を保持

    // Connect ノーツ用の状態管理 (ConnectNotes_Judge.csから移植)
    public bool Connect_DragStarted = false;
    public bool Connect_DragEnded = false;
    public bool Connect_CubeTouched = false;
    public float Connect_ElapsedTime = 0f;
    public float Connect_TotalJudgeTime = 0f;
    public float Connect_RequiredTime = 3f; // 必要ドラッグ時間
    public float Connect_JudgeEndOffset = 1f; // 時間切れまでの許容時間
    public float Connect_JudgeEndTime;
    public float Connect_MissAbsoluteTime;

    public ActiveNote(GameObject obj, NoteData data, int index)
    {
        NoteObject = obj;
        Data = data;
        IsUsed = false;

        // Connectノーツの場合、TimeとJudgeTimeRangeから終了時間を設定
        if (data.type == "connect")
        {
            Connect_JudgeEndTime = Connect_RequiredTime + Connect_JudgeEndOffset;
            Connect_DragStarted = false;
            Connect_DragEnded = false;
            Connect_CubeTouched = false;
            Connect_ElapsedTime = 0f;
            Connect_MissAbsoluteTime = data.time+ Connect_RequiredTime + Connect_JudgeEndOffset;
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

    /* 音源ソース */
    [Header("MusicSource")]
    public AudioSource Tutorial_MusicSource;  // チュートリアルの音源オブジェクトのAudioSourceをセット
    public AudioSource Game_MusicSource;      // 本番のゲームの音源オブジェクトのAudioSourceをセット

    /* 音楽の時間系 */
    private double StartTime = 0; // 音楽再生開始時刻
    // UnityのAudioSourceは基本的にAudioSettings.dspTimeと連携して動作するため、ここは変更なし
    public double CurrentTime => AudioSettings.dspTime - StartTime; //現在の音楽の再生時間、他クラスから読み取り可能

    /* 譜面データ */
    [Header("NotesData_filename")]
    public string Tutorial_NotesData; //チュートリアル用譜面データのファイル名
    public string Game_NotesData;　//本番用譜面データのファイル名

    /* ノーツ判定処理系 */
    [HideInInspector]
    public float targetTime; //ノーツが押されるべき時間

    /* “タッチ”ノーツの判定の厳しさパラメータ */
    [Header("Judge Settings - Sphere")]
    public double perfectRange = 0.3f; //Perfectの範囲内の時間
    public double goodRange = 0.5f; //Goodの範囲内の時間
    public double JudgeTimeRange = 0.7f; //Missを出すための時間

    //public ConnectNotes_Position notesPosition; // InspectorでConnectNotes_Positionを指定
    //public GameObject ConnectNotes_prefab;         // InspectorでCubeプレハブを指定
    //public GameObject ConnectNotes_JudgeResion_prefab;         // Inspectorで判定の範囲を指定
    //private GameObject ConnectNote;
    //private GameObject ConnectNotes_JudgeResion;

    public float targetTime_start;  // スタートする時間
    public float targetTime_goal;   // ゴールする時間
    //private float targetTime_connect;       // “つなげる”を何秒でやるかを指定する
    public float notesignalTime = 3f;    // スタートする時間の何秒前から合図を合図を出すか

    /* ノーツの分類とか */
    [HideInInspector]
    public int laneIndex; //このSphereが属するレーン番号
    //private NoteData[] notes;
    bool[] notes_isused;
    //private int noteIndex; //ノーツが来る番号

    /* 本番ゲームのスコア */
    private int Touch_score = 0; //“タッチ”によるスコア
    //private int Connect_score = 0; //“つなげる”によるスコア
    //private int Total_score = 0; //“タッチ”によるスコア

    /* 判定ごとのスコア */
    [Header("Judge Settings - Score")]
    public int Perfect_score = 5; //“Perfect”のときのスコア
    public int Good_score = 3; //“Good”のときのスコア

    /* むぎゅモジュール演出用のインスタンス */
    private System.Collections.Generic.List<Mugyu_LEDPerformance> mugyu_LEDPerformance = new System.Collections.Generic.List<Mugyu_LEDPerformance>();
    private System.Collections.Generic.List<Connect_LEDPerformance> connect_LEDPerformance = new System.Collections.Generic.List<Connect_LEDPerformance>();
    UdpController udpController;

    //再生中かどうか
    bool isplaying = false;

    private NoteData[] notes;
    private ActiveNote[] activeNotes; //  譜面データ全てのActiveNoteを管理
    private int lastSpawnedNoteIndex = 0; // 最後にプールからオブジェクトを割り当てたノーツのJSONインデックス

    //ゲームオブジェクトが生成された直後、Startより前に1回だけ呼ばれる
    void Awake()
    {
        Instance = this; //唯一のインスタンスを生成する
    }

    IEnumerator Start()
    {
        Touch_score = 0;
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
                if (mugyu_LEDPerformance[i] != null)mugyu_LEDPerformance[i].SetLEDGenerate();

                connect_LEDPerformance.Add(Notes[i].GetComponent<Connect_LEDPerformance>());
                if (connect_LEDPerformance[i] != null) connect_LEDPerformance[i].SetLEDGenerate();
            }
        }
        udpController = GameObject.Find("UdpController").GetComponent<UdpController>();

        // GameFlow開始
        StartCoroutine(GameFlow());
    }

    //チュートリアルから本番までやる流れの全体の処理
    private IEnumerator GameFlow()
    {
        // チュートリアル開始 → 終了まで待機
        yield return StartCoroutine(Tutorial());

        // チュートリアル終了後、本編開始
        yield return StartCoroutine(Game());
    }

    //チュートリアル実行
    private IEnumerator Tutorial()
    {
        LoadNotesFromJson(Tutorial_NotesData); //譜面データ読み込み
        yield return StartCoroutine(MusicPlayer(Tutorial_MusicSource)); //音楽を再生する

        // チュートリアル音楽の再生終了を確認する
        while (Tutorial_MusicSource.isPlaying)
        {
            yield return null;
        }

    }

    //本番実行
    private IEnumerator Game()
    {
        LoadNotesFromJson(Game_NotesData); //譜面データ読み込み
        yield return StartCoroutine(MusicPlayer(Game_MusicSource)); //音楽を再生する
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
        for(int i = 0; i < notes_isused.Length; i++) notes_isused[i] = false;
    }

    private void FixedUpdate()
    {
        if(isplaying) //ノーツがすべて終わっていなければ
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
            if (notenum < activeNotes.Length && activeNotes[notenum] != null)
            {
                //Debug.Log($"Checking Note Index: {notenum}, IsUsed: {activeNotes[notenum].IsUsed}");
            }

            // ノーツの判定が開始する時間 (JudgeTimeRange 前から)
            if (CurrentTime >= targetTime - JudgeTimeRange)
            {
                //  Perfect/Good/Missの判定ロジック (currentActiveNote.FlagComponent.TouchFlag を利用)
                bool judged = false;

                // Perfect判定
                if (Mathf.Abs(diff) <= perfectRange && currentActiveNote.FlagComponent.TouchFlag)
                {
                    //Debug.Log($"PERFECT! lane {currentActiveNote.Data.lane}");
                    Touch_score += Perfect_score;
                    judged = true;
                    // LEDを判定結果の色に設定 (例: 白)
                    currentActiveNote.NoteObject.GetComponent<Mugyu_LEDPerformance>()?.SetAllLEDColor(Color.white);
                }
                // Good判定
                else if (Mathf.Abs(diff) <= goodRange && currentActiveNote.FlagComponent.TouchFlag)
                {
                    //Debug.Log($"GOOD! lane {currentActiveNote.Data.lane}");
                    Touch_score += Good_score;
                    judged = true;
                    currentActiveNote.NoteObject.GetComponent<Mugyu_LEDPerformance>()?.SetAllLEDColor(Color.white);
                }
                // Miss判定 (時間切れ)
                else if (CurrentTime > targetTime + JudgeTimeRange)
                {
                    Debug.Log($"MISS! (Time Over) lane {currentActiveNote.Data.lane}");
                    judged = true;
                    // LEDをMissの色に設定 (例: 赤)
                    currentActiveNote.NoteObject.GetComponent<Mugyu_LEDPerformance>()?.SetAllLEDColor(Color.cyan);
                }
                // Miss判定 (早すぎ/遅すぎタッチ)
                else if (currentActiveNote.FlagComponent.TouchFlag)
                {
                    Debug.Log($"MISS! (Tapped out of range) lane {currentActiveNote.Data.lane}");
                    judged = true;
                    currentActiveNote.NoteObject.GetComponent<Mugyu_LEDPerformance>()?.SetAllLEDColor(Color.cyan);
                    //mugyu_LEDPerformance[notenum].SetAllLEDColor(Color.white);
                }


                if (judged)
                {
                    //  修正 1: 判定確定ログは、フラグ設定前に行い、表示落ちを防ぐ
                    if (Mathf.Abs(diff) <= perfectRange)
                        Debug.Log($"PERFECT! lane {currentActiveNote.Data.lane} Time: {CurrentTime:F3}");
                    else if (Mathf.Abs(diff) <= goodRange)
                        Debug.Log($"GOOD! lane {currentActiveNote.Data.lane} Time: {CurrentTime:F3}");
                    else
                        Debug.Log($"MISS! lane {currentActiveNote.Data.lane} Time: {CurrentTime:F3}");

                    currentActiveNote.IsUsed = true;
                    currentActiveNote.FlagComponent.ResetFlag();

                    if (currentActiveNote.NoteObject != null)
                    {
                        // 判定後、0.5秒後に色を黒に戻す処理を開始
                        StartCoroutine(ResetNoteColorAfterDelay(currentActiveNote.NoteObject, 0.5f));
                    }
                }

                //  判定可能範囲内での色変化
                else if (Mathf.Abs(diff) <= JudgeTimeRange)
                {
                    currentActiveNote.NoteObject.GetComponent<Mugyu_LEDPerformance>()?.SetAllLEDColor(Color.yellow);
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

    //合計スコアを計算する関数
    private void ScoreCalculate()
    {
        Debug.Log($"{Touch_score}");
    }

    
}
