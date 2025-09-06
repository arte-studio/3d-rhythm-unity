using UnityEngine;

public class TapeLEDSpawner : MonoBehaviour
{
    [Header("参照")]
    public GameObject ledPrefab;     // LEDプレハブ（光る球）

    [Header("テープ設定")]
    public float tapeLength = 1.0f;  // テープ長さ (m)
    public float pitch = 0.005f;     // LED間隔 (m, 5mm)

    [Header("LEDの位置調整")]
    public float offsetY = 0.002f;   // テープ上に浮かせる高さ

    void Start()
    {
        SpawnLEDs();
    }

    void SpawnLEDs()
    {
        if (ledPrefab == null)
        {
            Debug.LogError("LED Prefab が設定されていません！");
            return;
        }

        int ledCount = Mathf.FloorToInt(tapeLength / pitch);

        for (int i = 0; i < ledCount; i++)
        {
            // X方向に等間隔で並べる
            float x = i * pitch - tapeLength / 2f;
            Vector3 pos = transform.position + new Vector3(x, offsetY, 0);

            // LEDを生成してTapeの子にする
            GameObject led = Instantiate(ledPrefab, pos, Quaternion.identity, this.transform);
            led.name = $"LED_{i}";
        }
    }
}
