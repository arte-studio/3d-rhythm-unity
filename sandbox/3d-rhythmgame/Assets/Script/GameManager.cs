using CriWare;
using System.Collections;
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

    //ノーツの分類とか
    [HideInInspector]
    public int laneIndex; //このSphereが属するレーン番号
    private NoteData[] notes;
    private int noteIndex; //ノーツが来る番号

    /* 本番ゲームのスコア */
    private int Touch_score = 0; //“タッチ”によるスコア
    //private int Connect_score = 0; //“つなげる”によるスコア
    //private int Total_score = 0; //“タッチ”によるスコア

    /* 判定ごとのスコア */
    [Header("Judge Settings - Score")]
    public int Perfect_score = 5; //“Perfect”のときのスコア
    public int Good_score = 3; //“Good”のときのスコア

    /* むぎゅモジュール演出用 */
    public LEDPerformance ledPerformance;

    //ゲームオブジェクトが生成された直後、Startより前に1回だけ呼ばれる
    void Awake()
    {
        Instance = this; //唯一のインスタンスを生成する
    }

    void Start()
    {
        ledPerformance.PlaySquare();
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

        //ノーツのインデックスを0に初期化
        noteIndex = 0;

        while (MusicSource.status == CriAtomSource.Status.Playing) //音楽が再生していれば
        {
            if (noteIndex < notes.Length) //ノーツがすべて終わっていなければ
            {
                /* ※デバッグ用(現在の音楽の再生時間を表示) */
                //Debug.Log($"再生時間: {CurrentTime:F2} 秒");
                yield return new WaitForSeconds(0.5f);

                targetTime = notes[noteIndex].time; // 現在のノーツの目標時間を設定
                TouchNotes_judge();
            }

            yield return null;
        }

    }

    //音楽の再生時間を0.5秒ごとにコンソールに表示するコルーチン ※使わない
    /*private IEnumerator LogSongTime(double startTime,CriAtom MusicSource)
    {
        // Playing になるまで待つ
        while (MusicSource.status != CriAtomSource.Status.Playing) //音楽が再生中でなければ
        {
            yield return null; // 1フレーム待機
        }

        Debug.Log("音楽が再生状態になりました");

        while (MusicSource.status == CriAtomSource.Status.Playing) //音楽が再生されていれば
        {
            CurrentTime = AudioSettings.dspTime - startTime; //現在の音楽の再生時間を取得
            Debug.Log($"再生時間: {CurrentTime:F2} 秒"); //現在の音楽の再生時間を小数点以下2桁 までログに表示させる
            yield return new WaitForSeconds(0.5f);
        }
    }*/

    //ノーツを“タッチ”の判定処理をコンソールに表示する関数
    private void TouchNotes_judge()
    {
        laneIndex = notes[noteIndex].lane;

        GameObject currentNote = ObjectRelocation.Instance.objectByTypeAndLane["touch"][noteIndex]; //現在のノーツオブジェクトを取得して変数に保存
        if (currentNote == null) return;
        TouchNotes_Flag touchFlag = currentNote.GetComponent<TouchNotes_Flag>(); //GetComponent<T>() でcurrentNoteにアタッチされたTouchNotes_Flagを取得
        if (touchFlag == null) return;

        float diff = (float)(CurrentTime - targetTime); //現在の曲の再生時間とノーツの目標時刻の差を計算する　Unity では多くの関数が float を使う

        //Debug.Log($"notes is null? {notes == null}"); ※ノーツデータが正しく読み込まれていない場合true
        Debug.Log($"noteIndex = {noteIndex}, notes.Length = {notes.Length}, lane = {laneIndex}");
        //Debug.Log($"notes[{noteIndex}] is null? {notes[noteIndex] == null}");

        /* ノーツが押されたときの判定処理 */
        //"時間差がPerfectの範囲内 かつ ノーツが押された"なら ※Unity上ならTouchFlagからフラグをもらう
        if (Mathf.Abs(diff) <= perfectRange && touchFlag.TouchFlag)
        {
            Touch_score += Perfect_score;//タッチスコアに加算
            Debug.Log($"PERFECT! lane {laneIndex}");
            noteIndex++; //次のノーツの判定に移る
            touchFlag.ResetFlag(); // タッチフラグをリセットする(falseにする)
        }
        //"時間差がGoodの範囲内  かつ ノーツが押された"なら
        else if (Mathf.Abs(diff) <= goodRange && touchFlag.TouchFlag)
        {
            Touch_score += Good_score;
            Debug.Log($"GOOD! lane {laneIndex}");
            noteIndex++;
            touchFlag.ResetFlag();
        }
        //"時間差がGoodの範囲内  かつ ノーツが押された" または "現在の時間が判定時間を過ぎた"なら
        else if ((Mathf.Abs(diff) <= JudgeTimeRange && touchFlag.TouchFlag) || (CurrentTime > targetTime + JudgeTimeRange))
        {
            Debug.Log($"MISS! lane {laneIndex}");
            noteIndex++; 
            touchFlag.ResetFlag();
        }
        
        //判定時間内ならオブジェクトの色を緑にそれ以外ならオブジェクトを白に　※演出ができたら要らない
        Renderer noteRenderer = currentNote.GetComponent<Renderer>();

        if (Mathf.Abs(diff) <= JudgeTimeRange)
        {
            
            noteRenderer.material.color = Color.green;
        }
        else
        {
            noteRenderer.material.color = Color.white;
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
