using UnityEngine;

public class LEDCubeSimulator : MonoBehaviour
{
    [Header("パネルの物理サイズ (m)")]
    public float width = 1.0f;
    public float height = 2.0f;
    public float depth = 0.2f;

    [Header("LEDピッチ (m)")]
    public float pitchX = 0.10f; // 横方向
    public float pitchY = 0.03f; // 縦方向
    public float pitchZ = 0.10f; // 奥行き方向

    [Header("LED寸法 (m)")]
    public float ledDiameter = 0.005f;

    [Header("Prefab設定")]
    public GameObject ledPrefab;

    void Start()
    {
        GenerateLEDCube();
    }

    public void GenerateLEDCube()
    {
        // 既存LED削除
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        // サイズ / ピッチ から個数計算
        int countX = Mathf.FloorToInt(width / pitchX) + 1;
        int countY = Mathf.FloorToInt(height / pitchY) + 1;
        int countZ = Mathf.FloorToInt(depth / pitchZ) + 1;

        // 中央基準オフセット
        float offsetX = (countX - 1) * pitchX * 0.5f;
        float offsetY = (countY - 1) * pitchY * 0.5f;
        float offsetZ = (countZ - 1) * pitchZ * 0.5f;

        // 配置ループ
        for (int x = 0; x < countX; x++)
        {
            for (int y = 0; y < countY; y++)
            {
                for (int z = 0; z < countZ; z++)
                {
                    Vector3 pos = new Vector3(
                        x * pitchX - offsetX,
                        y * pitchY - offsetY,
                        z * pitchZ - offsetZ
                    );

                    GameObject led = Instantiate(ledPrefab, pos, Quaternion.identity, transform);
                    led.transform.localScale = Vector3.one * ledDiameter;
                }
            }
        }

        Debug.Log($"LED個数: {countX} x {countY} x {countZ} = {countX * countY * countZ}");
    }
}