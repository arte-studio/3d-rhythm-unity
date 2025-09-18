using UnityEngine;

public class LEDLinesPlacer : MonoBehaviour
{
    public GameObject ledLinePrefab; // LEDLineGenerator付きの空Prefab
    public SpherePlacer spherePlacer;

    void Start()
    {
        GameObject[] spheres = spherePlacer.spheres;

        // 六角形の辺（6本）
        for (int i = 0; i < 6; i++)
        {
            CreateLine(spheres[i], spheres[(i + 1) % 6], "Line_" + i + "_" + ((i + 1) % 6));
        }

        // 対角線（3本: 0-3, 1-4, 2-5）
        for (int i = 0; i < 3; i++)
        {
            CreateLine(spheres[i], spheres[i + 3], "Line_" + i + "_" + (i + 3));
        }
    }

    void CreateLine(GameObject start, GameObject end, string lineName)
    {
        GameObject line = Instantiate(ledLinePrefab, transform);
        line.name = lineName;

        LEDLineGenerator gen = line.GetComponent<LEDLineGenerator>();
        gen.startSphere = start;
        gen.endSphere = end;
    }
}
