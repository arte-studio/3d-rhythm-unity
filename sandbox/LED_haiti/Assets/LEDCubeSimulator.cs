using UnityEngine;

public class LEDCubeSimulator : MonoBehaviour
{
    [Header("LED数 (X, Y, Z)")]
    public int countX = 10;
    public int countY = 10;
    public int countZ = 10;

    [Header("LEDピッチ (m)")]
    public float pitchX = 0.01f;
    public float pitchY = 0.01f;
    public float pitchZ = 0.01f;

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
    }
}