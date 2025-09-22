using UnityEngine;
using static UnityEditor.PlayerSettings;

public class TapeLEDSpawner : MonoBehaviour
{
    public GameObject ledPrefab;
    public float tapeLength = 1.0f;   // テープの長さ
    public float pitch = 0.05f;       // LED間隔
    public float offset = 0.002f;     // テープ表面からの高さ

    void Start()
    {
        SpawnLEDs();
    }

    void SpawnLEDs()
    {
        if (ledPrefab == null)
        {
            Debug.LogError("LED Prefab が割り当てられていません！", this);
            return;
        }

        // テープの長さ方向を検出（ローカルX/Zの大きい方を採用）
        Vector3 dir = (transform.localScale.x >= transform.localScale.z) ? transform.right : transform.forward;

        // 個数を計算
        int ledCount = Mathf.FloorToInt(tapeLength / pitch);
        float start = -(ledCount - 1) * 0.5f * pitch;

        for (int i = 0; i < ledCount; i++)
        {
            // 長さ方向に並べて、テープのローカルY(=up方向)に少し浮かせる
            Vector3 worldPos = transform.position + dir * (start + i * pitch) + transform.up * offset;

            GameObject led = Instantiate(ledPrefab, worldPos, Quaternion.identity);
            led.transform.rotation = Quaternion.identity; // LEDは回転させない（丸い球体なのでOK）
            led.transform.SetParent(this.transform); // あとから親にする
        }
    }
}
