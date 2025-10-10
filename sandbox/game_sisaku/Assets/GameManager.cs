using UnityEngine;
using System.Collections;

[System.Serializable]
public class NoteData
{
    public float time;
    public int lane;   // Sphereノート用 (0～5)
    public int from;   // ライン始点 (ラインノート用)
    public int to;     // ライン終点 (ラインノート用)
}

[System.Serializable]
public class NotesWrapper //JSON からデータを読み込むためのラッパークラス、JsonUtility.FromJson<T>() は トップレベルが配列の場合は直接読み込めないのでオブジェクトかする必要がある
{
    public NoteData[] notes;
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance; //クラス全体で共有される唯一のインスタンス

    public AudioSource audioSource;

    [Header("Judge Settings - Sphere")]
    public float perfectRangeSphere = 0.1f;
    public float goodRangeSphere = 0.3f;

    [Header("Judge Settings - Line")]
    public float perfectRangeLine = 0.3f;
    public float goodRangeLine = 1.0f;


    public Renderer[] laneRenderers;  // Sphere用のRendererを決める配列,各レーンの Renderer の配列
    public Color normalColor = Color.white;
    public Color highlightColor = Color.red;


    private NoteData[] notes;
    private int noteIndex = 0;
    private double startTime;

    //LEDライン用（LineRenderer ではなく GameObject 配列に変更）
    private GameObject[,] ledLines;

    public double GetSongTime() => AudioSettings.dspTime - startTime; //読み取り専用　今の音楽の再生時間

    //ゲームオブジェクトが生成された直後、Startより前に1回だけ呼ばれる
    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        LoadNotesFromJson("notes"); // Resources/notes.json を読み込む

        // シーン内にある最初の SpherePlacer オブジェクトを探して、変数 placer に入れる
        SpherePlacer placer = FindFirstObjectByType<SpherePlacer>();

        //SpherePlacer が見つかっている場合のみ処理 する
        if (placer != null)
        {
            //複数のオブジェクトの色や表示をまとめて操作する処理
            GameObject[] spheres = placer.spheres; //placer の持っている spheres 配列 を取得
            laneRenderers = new Renderer[spheres.Length]; //各球体の Renderer コンポーネント を格納する配列を作る
            for (int i = 0; i < spheres.Length; i++)
            {
                laneRenderers[i] = spheres[i].GetComponent<Renderer>(); //球体の見た目を操作するために Renderer を取得
                laneRenderers[i].material.color = normalColor; // 色を 初期化（普通の色に戻す）
            }
        }

        //LEDラインを探して登録
        LEDLinesPlacer ledPlacer = FindFirstObjectByType<LEDLinesPlacer>(); //シーン内の LEDLinesPlacer が付いたゲームオブジェクトを1つ探して、変数 ledPlacer に入れる
        if (ledPlacer != null)
        {
            ledLines = ledPlacer.GetLineArray();// 1行目で見つけた ledPlacer に対して、メソッド GetLineArray() を呼び出してledLines にその配列を代入
        }

        noteIndex = 0;
        StartCoroutine(StartGameAfterDelay(3f)); //StartGameAfterDelay という名前のコルーチン関数を呼び出して、3秒後にゲームを開始する
    }

    //JSON ファイルからノーツデータを読み込む
    void LoadNotesFromJson(string fileName)
    {
        TextAsset jsonFile = Resources.Load<TextAsset>(fileName); // Asset/Resources/内にあるnotes.jsonを読み込む
        NotesWrapper wrapper = JsonUtility.FromJson<NotesWrapper>(jsonFile.text); //JsonUtility.FromJson で文字列をCのクラスに変換し、NotesWrapperクラスのインスタンスに代入
        notes = wrapper.notes; //JSON から読み込んだ ノーツ配列を GameManagerのプライベート変数のnotes 配列に代入
    }

    IEnumerator StartGameAfterDelay(float delay) //IEnumeratorは「コルーチン関数」を表す戻り値の型
    {
        yield return new WaitForSeconds(delay); //yield return を使って途中で一時停止し、あとで再開できる
        startTime = AudioSettings.dspTime + 0.1f; //AudioSettings.dspTime は 現在のオーディオシステムの正確な時間（秒）に0.1秒足した時間をstartTimeに保存
        audioSource.PlayScheduled(startTime); //AudioSource に再生予約を入れる命令
    }

    void Update() //Unity の特別な関数で 毎フレーム（1秒間に60回くらい）呼ばれる
    {
        if (audioSource.isPlaying && noteIndex < notes.Length) //音楽が再生中か,全ノーツを処理し終わっていないか
        {
            double songTime = GetSongTime(); //現在の曲の 再生時間を取得
            if (songTime >= notes[noteIndex].time - 0.5f) //曲の再生時間がノーツが来る0.5秒前なら
            {
                StartCoroutine(HandleNote(notes[noteIndex])); //HandleNoteのコルーチンを呼び出す
                noteIndex++;
            }
        }
    }

    //Sphere と ラインノートの分岐処理
    IEnumerator HandleNote(NoteData noteData)
    {
        if (noteData.lane >= 0)
        {
            // ----- Sphereノート処理 -----
            Renderer r = laneRenderers[noteData.lane]; //ノーツが属するレーンの Rendererを取り出して、対象のレーンのRendererを変数rに一時的に代入
            r.material.color = Color.yellow; //このレーンのオブジェクトを黄色にする

            double wait = noteData.time - (AudioSettings.dspTime - startTime); //ノーツが出るべき時間 − 今の経過時間＝ノーツが出るまでの残り時間
            if (wait > 0) yield return new WaitForSeconds((float)wait); //出す時刻がまだ来ていない場合だけ,指定秒数だけ待機してからノーツ生成

            r.material.color = highlightColor; //ノーツが出現するレーンを光らせる色を変えるなどの視覚的な演出を
            Note note = r.GetComponent<Note>(); //このレーン上の Note コンポーネントを探して、インスタンス note に代入
            note.targetTime = noteData.time; //noteData.time は譜面データJSONなどに書かれている、ノーツが判定ラインに到達する時刻を note オブジェクトに渡すことで、このノーツはこのタイミングで動くまたはヒットするという情報を設定

            yield return new WaitForSeconds(goodRangeSphere); //判定がGoodの範囲時間より後になるまで待つ

            if (!note.IsHit) Debug.Log("MISS!"); // プレイヤーがタイミングを逃した場合、ログにMISSを出力
            r.material.color = normalColor;
            note.ResetHit(); //NoteクラスのResetHit()を呼ぶ

        }
        else
        {
            // ----- ラインノート処理 -----
            GameObject lineObj = ledLines[noteData.from, noteData.to]; //ledLines[from, to] で 対象のラインオブジェクト（GameObject）を取得
            LEDLineGenerator gen = lineObj.GetComponent<LEDLineGenerator>(); //LEDLineGenerator（ラインオブジェクト上のスクリプト）をgenに代入してこのラインの LED 部分を操作できるようにする
            LineNote lineNote = lineObj.GetComponent<LineNote>(); //LineNote （ラインノーツの判定やタイミングを管理するスクリプト）をlineNoteに代入してこのラインノーツの判定をコルーチンで制御できるようにする

            // 黄色で予告（全部）
            foreach (Transform child in gen.transform)
            {
                var r = child.GetComponent<Renderer>();
                if (r != null) r.material.color = Color.yellow;
            }

            double wait = noteData.time - (AudioSettings.dspTime - startTime);
            if (wait > 0) yield return new WaitForSeconds((float)wait);

            // 赤で進行開始（音楽に合わせて流れる）
            float stepTime = goodRangeLine / gen.transform.childCount;
            for (int i = 0; i < gen.transform.childCount; i++)
            {
                var r = gen.transform.GetChild(i).GetComponent<Renderer>();
                if (r != null && !lineNote.IsHit) // まだHITしてなければ赤
                    r.material.color = highlightColor;

                yield return new WaitForSeconds(stepTime);
            }

            // 判定結果
            if (!lineNote.IsHit) Debug.Log("LINE MISS!");

            // リセット（HIT時はLineNote側で緑になるのでここでは白に戻すだけ）
            foreach (Transform child in gen.transform)
            {
                var r = child.GetComponent<Renderer>();
                if (r != null && !lineNote.IsHit) // MISSのときだけ戻す
                    r.material.color = normalColor;
            }
            lineNote.ResetHit();



        }
    }
}
