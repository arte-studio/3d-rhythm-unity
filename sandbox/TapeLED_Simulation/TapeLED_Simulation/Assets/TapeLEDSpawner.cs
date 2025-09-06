using UnityEngine;

public class TapeLEDSpawner : MonoBehaviour
{
    public GameObject ledPrefab;     // LEDプレハブ
    public float tapeLength = 1.0f;  // テープ長 (m)
    public float pitch = 0.005f;     // LED間隔 (5mm)
    public float offsetY = 0.002f;   // テープから浮かせる高さ

    void Start()
    {
        SpawnLEDs();
    }

    void SpawnLEDs()
    {
        int ledCount = Mathf.FloorToInt(tapeLength / pitch);

        for (int i = 0; i < ledCount; i++)
        {
            float x = i * pitch - tapeLength / 2f;
            Vector3 pos = transform.position + new Vector3(x, offsetY, 0);
            Instantiate(ledPrefab, pos, Quaternion.identity, this.transform);
        }
    }
}
