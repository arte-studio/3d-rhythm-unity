using UnityEngine;

public class NoteLeds : MonoBehaviour
{
    private const int NUM_MUGU_LEDS = 64;
    private const int NUM_CON_LEDS = 32;
    private const int NUM_MUGU = 40;
    private const int NUM_CON = 40;
    private const int NUM_DEVICES = 8;

    private Color32[NUM_DEVICES, (NUM_MUGU_LEDS + NUM_CON_LEDS)*5] ledColors;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Initialize the ledColors array
        ledColors = new Color32[NUM_DEVICES, (NUM_MUGU_LEDS + NUM_CON_LEDS) * 5];
    }

    void SetMuguColor(int muguId, int raw, int cow, Color32 color)
    {
        int index = (NUM_MUGU_LEDS + NUM_CON_LEDS) * (muguId % NUM_DEVICES) + raw * 8 + cow;
        ledColors[muguId, index] = color;
    }

    // すべてのMUGUの色を設定する
    public void SetAllMuguColors(int muguId, Color32 color)
    {
        for (int i = 0; i < NUM_MUGU_LEDS; i++)
        {
            int index = (NUM_MUGU_LEDS + NUM_CON_LEDS) * (muguId % NUM_DEVICES) + i;
            ledColors[muguId, index] = color;
        }
    }

    void SetConColor(int conId, int raw, Color32 color)
    {
        int index = (NUM_MUGU_LEDS + NUM_CON_LEDS) * (conId % NUM_DEVICES) + NUM_MUGU_LEDS + raw;
        // LEDのマイコンが違うので、ここで反転させる
        Color32 reverseColor = new Color32(color.g, color.r, color.b, color.a);
        ledColors[conId, index] = reverseColor;
    }

    public Color32[,] GetLedColors()
    {
        return ledColors;
    }

    public Color GetLedColor(int deviceId, int ledIndex)
    {
        return ledColors[deviceId, ledIndex];
    }

    /**
     * 譜面上のLEDのIDを、物理的なLEDのインデックスに変換する
     */
    public int ConvertNoteId(int id) {
        // ノーツIDからLEDインデックスへの変換ロジックを実装
        if (0 <= id && id <= 4) {
            return id + 20;
        } else if (5 <= id && id <= 6) {
            return id + 30;
        } else if (7 <= id && id <= 11) {
            return id - 7;
        } else if (12 <= id && id <= 16) {
            return id + 3;
        } else {
            return 17; // 無効なIDの場合
        }
    }
}
