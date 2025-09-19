using UnityEngine;
using System.Collections;

public class LEDMatrixController : MonoBehaviour
{
    public GameObject ledPrefab;     // LEDプレハブ
    public int rows = 8;
    public int cols = 8;
    public float panelSize = 0.065f; // 65mm
    public float ledSize = 0.005f;   // 5mm
    public float updateInterval = 0.05f; // 更新周期（秒）

    private LED[,] leds;
    private Renderer[,] renderers;

    public struct LED
    {
        public byte r, g, b;
        public LED(byte r, byte g, byte b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
        }
    }

    void Start()
    {
        leds = new LED[rows, cols];
        renderers = new Renderer[rows, cols];

        // LED間隔を計算
        float spacing = (panelSize - (ledSize * cols)) / (cols - 1);

        // 左上から右へ、次の行も左から右へ（直列）
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                float posX = x * (ledSize + spacing);
                float posY = -y * (ledSize + spacing);
                Vector3 pos = new Vector3(posX, posY, 0);

                GameObject obj = Instantiate(ledPrefab, pos, Quaternion.identity, transform);
                obj.transform.localScale = Vector3.one * ledSize;
                renderers[y, x] = obj.GetComponent<Renderer>();

                leds[y, x] = new LED(0, 0, 0); // 消灯で初期化
            }
        }

        StartCoroutine(UpdateLEDs());
    }

    IEnumerator UpdateLEDs()
    {
        while (true)
        {
            ApplyLEDColors();
            yield return new WaitForSeconds(updateInterval);
        }
    }

    void ApplyLEDColors()
    {
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                LED led = leds[y, x];
                Color color = new Color(led.r / 255f, led.g / 255f, led.b / 255f);

                // URP Lit Shader では _BaseColor に適用する方が確実
                renderers[y, x].material.SetColor("_BaseColor", color);

                // Emission も使うなら下も追加
                renderers[y, x].material.SetColor("_EmissionColor", color);
            }
        }
    }


    // --- 制御用 API ---
    public void SetLED(int index, byte r, byte g, byte b)
    {
        int y = index / cols;
        int x = index % cols;
        leds[y, x] = new LED(r, g, b);
    }

    public void SetLED(int row, int col, byte r, byte g, byte b)
    {
        leds[row, col] = new LED(r, g, b);
    }

    public Renderer GetRenderer(int row, int col)
    {
        if (row >= 0 && row < rows && col >= 0 && col < cols)
            return renderers[row, col];
        return null;
    }

}
