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
    public string type; // "touch", "line"
}

[System.Serializable]
public class NotesWrapper //JSON からデータを読み込むためのラッパークラス、JsonUtility.FromJson<T>() は トップレベルが配列の場合は直接読み込めないのでオブジェクトかする必要がある
{
    public NoteData[] notes;
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

    /* ノーツの分類とか */
    [HideInInspector]
    private NoteData[] notes;

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

    // レーンごとのノーツリスト
    private System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<NoteData>> notesByLane = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<NoteData>>();

    // 各レーンの現在のノーツインデックスを管理 レーン番号 → 現在処理中のノーツインデックス
    private System.Collections.Generic.Dictionary<int, int> noteIndexByLane = new System.Collections.Generic.Dictionary<int, int>();


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
            // ゲームオブジェクト型の配列Notesを作って、ObjectRelocation.csの生成したtouch_notesを保持するリスト
            System.Collections.Generic.List<GameObject> Notes = relocation.spawnedNotes;

            for (int i=0;i<Notes.Count; i++)
            {
                Debug.Log(Notes[i].name);
                //Notes の i 番目のオブジェクトから Mugyu_LEDPerformance コンポーネントを取得し、mugyu_LEDPerformance リストに追加する
                mugyu_LEDPerformance.Add(Notes[i].GetComponent<Mugyu_LEDPerformance>());

                if (mugyu_LEDPerformance[i] != null)
                    // Mugyu_LEDPerformance.cs内のLEDMatrixGenerator.cs内のGetFrontLEDs()を実行する
                    mugyu_LEDPerformance[i].SetLEDGenerate();
            } 
        }

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

    private void FixedUpdate()
    {
        if(isplaying) //再生中かどうか
        {
            ///lane[]の中にnoteIndex[]を置くような形にする
            ///jsonを読み込んでlaneごとに分類してそれを二次元配列([laneIndex][notesIndex])にいれればいいかな
            ///全部のむぎゅに対して判定を行う
            ///Unity上でテストしやすいように入力をマウスではなくキーボードにする
            ///つなげるも同様
            ///つなげるは判定用のオブジェクトを作ってそこに触れたら判定するようにする

            // 全てのレーン番号に対して順番に処理を行う
            foreach (int lane in notesByLane.Keys) //notesByLane.Keysはレーン番号を返す
            {
                if (ObjectRelocation.Instance.objectByTypeAndLane["touch"].ContainsKey(lane))
                {
                    TouchNotes_judge(lane);
                }
            }
        }
    }

    void LoadNotesFromJson(string fileName)
    {
        TextAsset jsonFile = Resources.Load<TextAsset>(fileName);
        NotesWrapper wrapper = JsonUtility.FromJson<NotesWrapper>(jsonFile.text);
        notes = wrapper.notes;

        // レーンの状態を削除する
        notesByLane.Clear();
        noteIndexByLane.Clear();

        //ノーツデータをレーンごとに整理して、判定用のインデックスも初期化する
        foreach (NoteData note in notes)
        {
            if (!notesByLane.ContainsKey(note.lane)) // 新しいレーンが出てきたときにリストとインデックスを用意する
            {
                notesByLane[note.lane] = new System.Collections.Generic.List<NoteData>(); // notesByLane[lane] でそのレーンのノーツをまとめて扱える
                noteIndexByLane[note.lane] = 0; // 現在処理中のノーツインデックスの初期化
            }
            notesByLane[note.lane].Add(note); //すでにレーンが登録されていたらそこにノーツインデックスを追加する
        }

        // 各レーン内で時間順にソート
        foreach (var laneNotes in notesByLane.Values)
        {
            laneNotes.Sort((a, b) => a.time.CompareTo(b.time));

        }

        // コンソールにレーンごとのノーツデータを表示
        foreach (var kvp in notesByLane)
        {
            int lane = kvp.Key;
            var laneNotes = kvp.Value;
            string noteTimes = "";
            foreach (var n in laneNotes)
            {
                noteTimes += $"({n.time}, {n.type}) ";
            }
            Debug.Log($"Lane {lane}: {noteTimes}");
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

        //音楽が再生していれば
        while (MusicSource.status == CriAtomSource.Status.Playing)
        {
                isplaying = true;
                yield return null;
        }
        isplaying = false;


    }

    /*private void TouchNotes_judge(int lane)
    {
        // そのレーンにノーツが存在しない場合はスキップ
        if (!notesByLane.ContainsKey(lane)) return;
        var laneNotes = notesByLane[lane];
        int noteIndex = noteIndexByLane[lane];
        if (noteIndex >= laneNotes.Count) return;

        NoteData currentNoteData = laneNotes[noteIndex];
        float targetTime = currentNoteData.time;
        float diff = (float)(CurrentTime - targetTime);

        // notesByLaneのレーン番号を使ってObjectRelocationから取得
        if (!ObjectRelocation.Instance.objectByTypeAndLane.ContainsKey("touch")) return;
        var touchDict = ObjectRelocation.Instance.objectByTypeAndLane["touch"]; // Dictionary<int, List<GameObject>>

        if (!touchDict.ContainsKey(lane)) return; // レーンが存在するか確認
        var notesList = touchDict[lane]; // List<GameObject> 型

        if (noteIndex >= notesList.Count) return;

        GameObject currentNote = notesList[noteIndex]; // これで GameObject 型になる
        if (currentNote == null) return;

        TouchNotes_Flag touchFlag = currentNote.GetComponent<TouchNotes_Flag>();
        if (touchFlag == null) return;


        // 判定処理
        if (Mathf.Abs(diff) <= perfectRange && touchFlag.TouchFlag)
        {
            Touch_score += Perfect_score;
            Debug.Log($"PERFECT! lane {lane}");
            mugyu_LEDPerformance[noteIndex].SetAllLEDColor(Color.white);
            noteIndexByLane[lane]++;
            touchFlag.ResetFlag();
        }
        else if (Mathf.Abs(diff) <= goodRange && touchFlag.TouchFlag)
        {
            Touch_score += Good_score;
            Debug.Log($"GOOD! lane {lane}");
            mugyu_LEDPerformance[noteIndex].SetAllLEDColor(Color.white);
            noteIndexByLane[lane]++;
            touchFlag.ResetFlag();
        }
        else if ((Mathf.Abs(diff) <= JudgeTimeRange && touchFlag.TouchFlag) || (CurrentTime > targetTime + JudgeTimeRange))
        {
            Debug.Log($"MISS! lane {lane}");
            mugyu_LEDPerformance[noteIndex].SetAllLEDColor(Color.white);
            noteIndexByLane[lane]++;
            touchFlag.ResetFlag();
        }

        // 演出用
        if (noteIndexByLane[lane] < laneNotes.Count && mugyu_LEDPerformance.Count > noteIndex)
        {
            if (Mathf.Abs(diff) <= JudgeTimeRange)
                mugyu_LEDPerformance[noteIndex].SetAllLEDColor(Color.green);
            else
                mugyu_LEDPerformance[noteIndex].SetAllLEDColor(Color.black);
        }
    }*/

    private void TouchNotes_judge(int lane)
    {
        if (!notesByLane.ContainsKey(lane)) return;
        var laneNotes = notesByLane[lane];
        int noteIndex = noteIndexByLane[lane];

        //var notesList = ObjectRelocation.Instance.objectByTypeAndLane["touch"][lane];

        var touchDict = ObjectRelocation.Instance.objectByTypeAndLane["touch"];
        if (!touchDict.ContainsKey(lane)) return; // レーンが存在しない場合はスキップ

        var notesList = touchDict[lane]; // 安全にアクセス


        while (noteIndex < laneNotes.Count)
        {
            NoteData currentNoteData = laneNotes[noteIndex];
            float diff = (float)(CurrentTime - currentNoteData.time);
            GameObject currentNote = notesList[noteIndex];
            TouchNotes_Flag touchFlag = currentNote.GetComponent<TouchNotes_Flag>();
            if (touchFlag == null) break;

            // 判定
            if (Mathf.Abs(diff) <= perfectRange && touchFlag.TouchFlag)
            {
                Touch_score += Perfect_score;
                touchFlag.ResetFlag();
                noteIndexByLane[lane]++;
            }
            else if (Mathf.Abs(diff) <= goodRange && touchFlag.TouchFlag)
            {
                Touch_score += Good_score;
                touchFlag.ResetFlag();
                noteIndexByLane[lane]++;
            }
            else if ((Mathf.Abs(diff) <= JudgeTimeRange && touchFlag.TouchFlag) || (CurrentTime > currentNoteData.time + JudgeTimeRange))
            {
                touchFlag.ResetFlag();
                noteIndexByLane[lane]++;
            }
            else
            {
                // 判定範囲外なら次のノーツはまだ来ないのでループ終了
                break;
            }

            noteIndex = noteIndexByLane[lane]; // 次のノーツに進む
        }
    }



    //ノーツを“つなげる”の判定処理をコンソールに表示する関数
    private void ConnectNotes_judge()
    {
        
    }

    //合計スコアを計算する関数
    private void ScoreCalculate()
    {

    }

    
}
