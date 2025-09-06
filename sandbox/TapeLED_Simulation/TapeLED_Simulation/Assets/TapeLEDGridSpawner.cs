using UnityEngine;

using UnityEngine;

public class TapeLEDGridSpawner : MonoBehaviour
{
    public GameObject tapeLEDPrefab;   // 1本のテープLEDプレハブ
    public int count = 10;             // 縦横の本数
    public float spacing = 0.05f;      // テープ間の間隔 (m)

    void Start()
    {
        SpawnGrid();
    }

    void SpawnGrid()
    {
        // 横方向（X軸に沿って伸びるテープ）
        for (int z = 0; z < count; z++)
        {
            Vector3 pos = transform.position + new Vector3(0, z * spacing, 0);
            Instantiate(tapeLEDPrefab, pos, Quaternion.identity, this.transform);
        }

        // 縦方向（Z軸に沿って伸びるテープ → 90度回転させる）
        for (int x = 0; x < count; x++)
        {
            Vector3 pos = transform.position + new Vector3(x * spacing, 0, 0);
            Instantiate(tapeLEDPrefab, pos, Quaternion.Euler(0, 90, 0), this.transform);
        }
    }
}

