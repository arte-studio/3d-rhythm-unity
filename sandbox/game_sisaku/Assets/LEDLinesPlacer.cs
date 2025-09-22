using UnityEngine;

public class LEDLinesPlacer : MonoBehaviour
{
    public GameObject ledLinePrefab; // LEDLineGenerator付きのPrefab
    public SpherePlacer spherePlacer;

    // 生成したLEDラインを保持（LineRendererではなくGameObject）
    private GameObject[,] lineArray;

    void Start()
    {
        if (spherePlacer == null || spherePlacer.spheres == null || spherePlacer.spheres.Length < 6)
        {
            Debug.LogError("SpherePlacer が正しく設定されていません！");
            return;
        }

        GameObject[] spheres = spherePlacer.spheres;
        lineArray = new GameObject[6, 6];

        // 六角形の辺（6本）
        for (int i = 0; i < 6; i++)
        {
            CreateLine(spheres[i], spheres[(i + 1) % 6], i, (i + 1) % 6);
        }

        // 対角線（3本）
        for (int i = 0; i < 3; i++)
        {
            CreateLine(spheres[i], spheres[i + 3], i, i + 3);
        }
    }

    void CreateLine(GameObject start, GameObject end, int from, int to)
    {
        GameObject line = Instantiate(ledLinePrefab, transform);
        line.name = $"Line_{from}_{to}";

        // LEDLineGenerator 設定
        LEDLineGenerator gen = line.GetComponent<LEDLineGenerator>();
        if (gen == null)
        {
            Debug.LogError("ledLinePrefab に LEDLineGenerator がついていません！");
            return;
        }
        gen.startSphere = start;
        gen.endSphere = end;

        // LineNote 設定
        LineNote note = line.GetComponent<LineNote>();
        if (note != null)
        {
            note.fromSphere = start;
            note.toSphere = end;
        }

        // 配列に保存
        lineArray[from, to] = line;
        lineArray[to, from] = line;
    }

    // 外部から取得できるように
    public GameObject[,] GetLineArray()
    {
        return lineArray;
    }
}
