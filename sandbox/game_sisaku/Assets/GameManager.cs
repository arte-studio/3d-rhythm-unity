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
public class NotesWrapper
{
    public NoteData[] notes;
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public AudioSource audioSource;

    [Header("Judge Settings - Sphere")]
    public float perfectRangeSphere = 0.1f;
    public float goodRangeSphere = 0.3f;

    [Header("Judge Settings - Line")]
    public float perfectRangeLine = 0.3f;
    public float goodRangeLine = 1.0f;


    public Renderer[] laneRenderers;  // Sphere用
    public Color normalColor = Color.white;
    public Color highlightColor = Color.red;


    private NoteData[] notes;
    private int noteIndex = 0;
    private double startTime;

    // --- LEDライン用（LineRenderer ではなく GameObject 配列に変更） ---
    private GameObject[,] ledLines;

    public double GetSongTime() => AudioSettings.dspTime - startTime;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        LoadNotesFromJson("notes"); // Resources/notes.json を読み込む

        // --- SpherePlacer から自動取得 ---
        SpherePlacer placer = FindFirstObjectByType<SpherePlacer>();
        if (placer != null)
        {
            GameObject[] spheres = placer.spheres;
            laneRenderers = new Renderer[spheres.Length];
            for (int i = 0; i < spheres.Length; i++)
            {
                laneRenderers[i] = spheres[i].GetComponent<Renderer>();
                laneRenderers[i].material.color = normalColor; // 初期化
            }
        }

        // --- LEDラインを探して登録 ---
        LEDLinesPlacer ledPlacer = FindFirstObjectByType<LEDLinesPlacer>();
        if (ledPlacer != null)
        {
            ledLines = ledPlacer.GetLineArray();
        }

        noteIndex = 0;
        StartCoroutine(StartGameAfterDelay(3f));
    }

    void LoadNotesFromJson(string fileName)
    {
        TextAsset jsonFile = Resources.Load<TextAsset>(fileName);
        NotesWrapper wrapper = JsonUtility.FromJson<NotesWrapper>(jsonFile.text);
        notes = wrapper.notes;
    }

    IEnumerator StartGameAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        startTime = AudioSettings.dspTime + 0.1f;
        audioSource.PlayScheduled(startTime);
    }

    void Update()
    {
        if (audioSource.isPlaying && noteIndex < notes.Length)
        {
            double songTime = GetSongTime();
            if (songTime >= notes[noteIndex].time - 0.5f)
            {
                StartCoroutine(HandleNote(notes[noteIndex]));
                noteIndex++;
            }
        }
    }

    // --- Sphere と ラインノートの分岐処理 ---
    IEnumerator HandleNote(NoteData noteData)
    {
        if (noteData.lane >= 0)
        {
            // ----- Sphereノート処理 -----
            Renderer r = laneRenderers[noteData.lane];
            r.material.color = Color.yellow;

            double wait = noteData.time - (AudioSettings.dspTime - startTime);
            if (wait > 0) yield return new WaitForSeconds((float)wait);

            r.material.color = highlightColor;
            Note note = r.GetComponent<Note>();
            note.targetTime = noteData.time;

            // ここを修正！
            yield return new WaitForSeconds(goodRangeSphere);

            if (!note.IsHit) Debug.Log("MISS!");
            r.material.color = normalColor;
            note.ResetHit();

        }
        else
        {
            // ----- ラインノート処理 -----
            GameObject lineObj = ledLines[noteData.from, noteData.to].gameObject;
            LEDLineGenerator gen = lineObj.GetComponent<LEDLineGenerator>();
            LineNote lineNote = lineObj.GetComponent<LineNote>();

            // 黄色で予告（全部）
            foreach (Transform child in gen.transform)
            {
                var r = child.GetComponent<Renderer>();
                if (r != null) r.material.color = Color.yellow;
            }

            double wait = noteData.time - (AudioSettings.dspTime - startTime);
            if (wait > 0) yield return new WaitForSeconds((float)wait);

            // 赤で進行開始
            float stepTime = goodRangeLine / gen.transform.childCount;
            for (int i = 0; i < gen.transform.childCount; i++)
            {
                var r = gen.transform.GetChild(i).GetComponent<Renderer>();
                if (r != null) r.material.color = highlightColor;
                yield return new WaitForSeconds(stepTime);
            }

            // 判定結果
            if (!lineNote.IsHit) Debug.Log("LINE MISS!");

            // リセット
            foreach (Transform child in gen.transform)
            {
                var r = child.GetComponent<Renderer>();
                if (r != null) r.material.color = normalColor;
            }
            lineNote.ResetHit();


        }
    }
}
