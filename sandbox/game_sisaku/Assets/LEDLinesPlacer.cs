using UnityEngine;

public class LEDLinesPlacer : MonoBehaviour
{
    public GameObject ledLinePrefab; // LEDLineGenerator付きの空Prefab
    public SpherePlacer spherePlacer;

    // --- 追加: ラインを保持する配列 ---
    private LineRenderer[,] lineArray;

    void Start()
    {
        GameObject[] spheres = spherePlacer.spheres;

        // 6つのSphereなので最大 [6,6]
        lineArray = new LineRenderer[6, 6];

        // 六角形の辺（6本）
        for (int i = 0; i < 6; i++)
        {
            CreateLine(spheres[i], spheres[(i + 1) % 6], i, (i + 1) % 6);
        }

        // 対角線（3本: 0-3, 1-4, 2-5）
        for (int i = 0; i < 3; i++)
        {
            CreateLine(spheres[i], spheres[i + 3], i, i + 3);
        }
    }

    void CreateLine(GameObject start, GameObject end, int from, int to)
    {
        GameObject line = Instantiate(ledLinePrefab, transform);
        line.name = "Line_" + from + "_" + to;

        LEDLineGenerator gen = line.GetComponent<LEDLineGenerator>();
        gen.startSphere = start;
        gen.endSphere = end;

        // LineRenderer を取得して配列に登録
        LineRenderer lr = line.GetComponent<LineRenderer>();
        if (lr == null)
        {
            lr = line.AddComponent<LineRenderer>();
            lr.positionCount = 2;
        }else if (lr != null)
        {
            lineArray[from, to] = lr;
            lineArray[to, from] = lr; // 双方向でアクセス可能にする
        }
    }

    // --- 追加: GameManager が呼び出す ---
    public LineRenderer[,] GetLineArray()
    {
        return lineArray;
    }
}
