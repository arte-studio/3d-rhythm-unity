using UnityEngine;

public class TapeGridSpawner : MonoBehaviour
{
    public GameObject tapePrefab;   // テープのPrefab
    public int tapeCountX = 10;     // X方向の本数
    public int tapeCountZ = 10;     // Z方向の本数
    public float tapeSpacing = 0.1f; // テープ同士の間隔（m単位）

    void Start()
    {
        SpawnTapes();
    }

    void SpawnTapes()
    {
        if (tapePrefab == null)
        {
            Debug.LogError("Tape Prefab が割り当てられていません！", this);
            return;
        }

        // 中心に配置されるようにオフセット
        float offsetX = -(tapeCountX - 1) * 0.5f * tapeSpacing;
        float offsetZ = -(tapeCountZ - 1) * 0.5f * tapeSpacing;

        for (int x = 0; x < tapeCountX; x++)
        {
            for (int z = 0; z < tapeCountZ; z++)
            {
                Vector3 pos = transform.position + new Vector3(offsetX + x * tapeSpacing, 0, offsetZ + z * tapeSpacing);

                // テープを生成
                GameObject tape = Instantiate(tapePrefab, pos, Quaternion.identity, this.transform);

                // 向きの設定（X方向とZ方向に分けたい場合）
                if (x % 2 == 0)
                {
                    tape.transform.rotation = Quaternion.Euler(0, 0, 0); // Z方向に伸びる
                }
                else
                {
                    tape.transform.rotation = Quaternion.Euler(0, 90, 0); // X方向に伸びる
                }
            }
        }
    }
}
