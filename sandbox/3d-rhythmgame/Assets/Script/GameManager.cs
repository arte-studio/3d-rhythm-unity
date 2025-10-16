using CriWare;
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
    public string type; // "touch", "line"
}

[System.Serializable]
public class NotesWrapper //JSON からデータを読み込むためのラッパークラス、JsonUtility.FromJson<T>() は トップレベルが配列の場合は直接読み込めないのでオブジェクトかする必要がある
{
    public NoteData[] notes;
}

// GameManager.cs のクラス外（または内部）に追加

public class ActiveNote
{
    public GameObject NoteObject;  // 画面に表示中のノーツオブジェクト
    public NoteData Data;          // 対応する譜面データ
    public bool IsUsed;            // 判定済みフラグ（notes_isused[index] の代わり）
    public TouchNotes_Flag FlagComponent; // フラグコンポーネンスへの参照を保持

    public ActiveNote(GameObject obj, NoteData data, int index)
    {
        NoteObject = obj;
        Data = data;
        IsUsed = false;
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
    public CriAtomSource Tutorial_MusicSource;  // Inspectorでチュートリアルの音源オブジェクトのCriAtomSourceをセット
    public CriAtomSource Game_MusicSource;  // Inspectorで本番のゲームの音源オブジェクトのCriAtomSourceをセット

    /* 音楽の時間系 */
    private double StartTime = 0; // 音楽再生開始時刻
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
        // ObjectRelocationの生成完了を待つ
        yield return new WaitForSeconds(0.1f);

        // touch_notesを取得
        ObjectRelocation relocation = ObjectRelocation.Instance;
        if (relocation != null && relocation.spawnedNotes.Count > 0)
        {
            System.Collections.Generic.List<GameObject> Notes = relocation.spawnedNotes;

            for (int i=0;i<Notes.Count; i++)
            {
                Debug.Log(Notes[i].name);
                mugyu_LEDPerformance.Add(Notes[i].GetComponent<Mugyu_LEDPerformance>());

                if (mugyu_LEDPerformance[i] != null)
                    mugyu_LEDPerformance[i].SetLEDGenerate();
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
        while (Tutorial_MusicSource.status == CriAtomSource.Status.Playing)
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
    private IEnumerator MusicPlayer(CriAtomSource MusicSource)
    {
        //音楽再生
        MusicSource.Play();

        // DSPタイムで開始時刻を記録
        StartTime = AudioSettings.dspTime;

        // 再生状態になるまで待機
        while (MusicSource.status != CriAtomSource.Status.Playing)
        {
            yield return null; // 1フレーム待つ
        }
        isplaying = true;
        //ノーツのインデックスを0に初期化
        //noteIndex = 0;

        lastSpawnedNoteIndex = 0; // プールから割り当てたノーツインデックスを初期化

        while (MusicSource.status == CriAtomSource.Status.Playing)
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
                    Debug.Log($"Note assigned: Index {lastSpawnedNoteIndex}, Lane {noteData.lane}");
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
                    //Debug.Log($"MISS! (Time Over) lane {currentActiveNote.Data.lane}");
                    judged = true;
                    // LEDをMissの色に設定 (例: 赤)
                    currentActiveNote.NoteObject.GetComponent<Mugyu_LEDPerformance>()?.SetAllLEDColor(Color.red);
                }
                // Miss判定 (早すぎ/遅すぎタッチ)
                else if (currentActiveNote.FlagComponent.TouchFlag)
                {
                    //Debug.Log($"MISS! (Tapped out of range) lane {currentActiveNote.Data.lane}");
                    judged = true;
                    currentActiveNote.NoteObject.GetComponent<Mugyu_LEDPerformance>()?.SetAllLEDColor(Color.red);
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
                    // ノーツオブジェクトは固定位置にあるため、非アクティブ化せず、色を待機状態（黒）に戻す、または判定エフェクトを実行します。

                    // 判定後、一定時間後に色を黒に戻すコルーチンを呼び出すなどして、再利用に備えます。
                    // StartCoroutine(ResetNoteColorAfterDelay(currentActiveNote.NoteObject, 0.5f));
                }

                //  判定可能範囲内での色変化
                else if (Mathf.Abs(diff) <= JudgeTimeRange)
                {
                    currentActiveNote.NoteObject.GetComponent<Mugyu_LEDPerformance>()?.SetAllLEDColor(Color.green);
                }
            }
        }
    }

    /*ノーツを“タッチ”の判定処理をコンソールに表示する関数
    private void TouchNotes_judge()
    {
        for(int notenum = 0; notenum < notes.Length; notenum++)
        {
            targetTime = notes[notenum].time; // 現在のノーツの目標時間を設定

            if (!notes_isused[notenum])
            {
                laneIndex = notes[notenum].lane;

                float diff = (float)(CurrentTime - targetTime);//現在の曲の再生時間とノーツの目標時刻の差を計算する　Unity では多くの関数が float を使う



                if (CurrentTime >= targetTime)
                {
                    GameObject currentNote = ObjectRelocation.Instance.objectByTypeAndLane["touch"][notenum]; //現在のノーツオブジェクトを取得して変数に保存
                    if (currentNote == null) return;
                    TouchNotes_Flag touchFlag = currentNote.GetComponent<TouchNotes_Flag>(); //GetComponent<T>() でcurrentNoteにアタッチされたTouchNotes_Flagを取得
                    if (touchFlag == null) return;

                    //Debug.Log($"notes is null? {notes == null}"); ※ノーツデータが正しく読み込まれていない場合true
                    Debug.Log($"noteIndex = {notenum}, notes.Length = {notes.Length}, lane = {laneIndex}");
                    //Debug.Log($"notes[{noteIndex}] is null? {notes[noteIndex] == null}");

                    /* ノーツが押されたときの判定処理 
                    //"時間差がPerfectの範囲内 かつ ノーツが押された"なら ※Unity上ならTouchFlagからフラグをもらう
                    if (Mathf.Abs(diff) <= perfectRange && touchFlag.TouchFlag)
                    {
                        Touch_score += Perfect_score;//タッチスコアに加算
                        Debug.Log($"PERFECT! lane {laneIndex}");
                        mugyu_LEDPerformance[notenum].SetAllLEDColor(Color.white);
                        //noteIndex++; //次のノーツの判定に移る
                        notes_isused[notenum] = true; //にくぬき追加：ノーツを判定済みとしてマーク
                        touchFlag.ResetFlag(); // タッチフラグをリセットする(falseにする)
                    }
                    //"時間差がGoodの範囲内  かつ ノーツが押された"なら
                    else if (Mathf.Abs(diff) <= goodRange && touchFlag.TouchFlag)
                    {
                        Touch_score += Good_score;
                        Debug.Log($"GOOD! lane {laneIndex}");
                        mugyu_LEDPerformance[notenum].SetAllLEDColor(Color.white);
                        // --- ここでゲームのロジックに応じてLEDの色を更新してください ---

                        //noteLeds[デバイスID][LED番号] = Color.blue;
                        // フレームごとに全デバイスにLEDデータを送信
                        if (udpController != null) udpController.SendAllLedData();
                        notes_isused[notenum] = true;
                        //noteIndex++;
                        touchFlag.ResetFlag();
                    }
                    //"時間差がGoodの範囲内  かつ ノーツが押された" または "現在の時間が判定時間を過ぎた"なら
                    else if ((Mathf.Abs(diff) <= JudgeTimeRange && touchFlag.TouchFlag) || (CurrentTime > targetTime + JudgeTimeRange))
                    {
                        Debug.Log($"MISS! lane {laneIndex}");
                        mugyu_LEDPerformance[notenum].SetAllLEDColor(Color.white);
                        notes_isused[notenum] = true;
                        //noteIndex++;
                        touchFlag.ResetFlag();

                    }

                    //判定時間内ならオブジェクトの色を緑にそれ以外ならオブジェクトを白に　※演出ができたら要らない
                    //Renderer noteRenderer = currentNote.GetComponent<Renderer>();
                    if (mugyu_LEDPerformance.Count > notenum && mugyu_LEDPerformance[notenum] != null && !notes_isused[notenum])
                    {
                        if (Mathf.Abs(diff) <= JudgeTimeRange)
                        {
                            //Debug.Log($"mugyu_LEDPerformance is changed");
                            mugyu_LEDPerformance[notenum].SetAllLEDColor(Color.green);
                        }
                        else
                        {
                            //Debug.Log($"mugyu_LEDPerformance is default");
                            mugyu_LEDPerformance[notenum].SetAllLEDColor(Color.black);
                        }
                    }
                }
            }
        }
    }*/



    //ノーツを“つなげる”の判定処理をコンソールに表示する関数
    private void ConnectNotes_judge()
    {
        
    }

    //合計スコアを計算する関数
    private void ScoreCalculate()
    {

    }

    
}
