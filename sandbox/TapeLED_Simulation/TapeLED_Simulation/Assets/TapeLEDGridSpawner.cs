using UnityEngine;

public class TapeLEDCubeSpawner : MonoBehaviour
{
    public GameObject ledPrefab;   // LEDのPrefab
    public int count = 5;          // 1辺のLED数
    public float spacing = 0.2f;   // LED間隔

    void Start()
    {
        SpawnCube();
    }

    void SpawnCube()
    {
        float offset = -(count - 1) * 0.5f * spacing;

        // === XY 平面のテープ ===
        for (int z = 0; z < count; z++)
        {
            for (int y = 0; y < count; y++)
            {
                for (int x = 0; x < count; x++)
                {
                    // XYテープ用のLEDを生成
                    Vector3 pos = transform.position + new Vector3(offset + x * spacing, offset + y * spacing, offset + z * spacing);
                    CreateUniqueLED(pos);
                }
            }
        }

        // === YZ 平面のテープ ===
        for (int x = 0; x < count; x++)
        {
            for (int y = 0; y < count; y++)
            {
                for (int z = 0; z < count; z++)
                {
                    // YZテープ用のLEDを生成（重複チェック付き）
                    Vector3 pos = transform.position + new Vector3(offset + x * spacing, offset + y * spacing, offset + z * spacing);
                    CreateUniqueLED(pos);
                }
            }
        }
    }

    void CreateUniqueLED(Vector3 pos)
    {
        // すでにその座標にLEDがあるかチェック
        Collider[] hits = Physics.OverlapSphere(pos, spacing * 0.1f);
        if (hits.Length == 0)
        {
            Instantiate(ledPrefab, pos, Quaternion.identity, this.transform);
        }
    }
}