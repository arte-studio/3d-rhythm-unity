using UnityEngine;
using System.Collections;

[System.Serializable]
public class NoteData
{
    public float time;
    public int lane;
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
    public Renderer targetRenderer;
    public Color normalColor = Color.white;
    public Color highlightColor = Color.red;

    [Header("Judge Settings")]
    public float perfectRange = 0.1f;
    public float goodRange = 0.3f;

    // Inspectorに出さないように private に変更
    private NoteData[] notes;
    private int noteIndex = 0;
    private double startTime;

    public double GetSongTime() => AudioSettings.dspTime - startTime;

    void Awake()
    {
        Instance = this;
    }

    void LoadNotesFromJson(string fileName)
    {
        TextAsset jsonFile = Resources.Load<TextAsset>(fileName);
        NotesWrapper wrapper = JsonUtility.FromJson<NotesWrapper>(jsonFile.text);
        notes = wrapper.notes;
    }

    void Start()
    {
        LoadNotesFromJson("notes"); // Resources/notes.json を読み込む
        targetRenderer.material.color = normalColor;
        noteIndex = 0;
        StartCoroutine(StartGameAfterDelay(3f));
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
                StartCoroutine(PreFlashAndHit(notes[noteIndex].time));
                noteIndex++;
            }
        }
    }

    IEnumerator PreFlashAndHit(float hitTime)
    {
        // 0.5秒前に黄色
        targetRenderer.material.color = Color.yellow;

        // 判定タイミングまで待機
        double wait = hitTime - (AudioSettings.dspTime - startTime);
        if (wait > 0) yield return new WaitForSeconds((float)wait);

        // 判定タイミングで赤
        targetRenderer.material.color = highlightColor;
        Note note = targetRenderer.GetComponent<Note>();
        note.targetTime = hitTime;

        // 判定受付時間（GOOD の範囲）だけ待機
        yield return new WaitForSeconds(goodRange);

        // 判定範囲が終わった瞬間にMISS判定
        if (!note.IsHit)
        {
            Debug.Log("MISS!");
        }

        // Sphere の色を白に戻す
        targetRenderer.material.color = normalColor;

        // 次のノーツに備えてリセット
        note.ResetHit();
    }
}
