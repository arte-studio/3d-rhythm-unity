using UnityEngine;

public class NoteLEDs : MonoBehaviour
{
    private const int NUM_MUGU = 40;
    private const int NUM_CONNECT = 40;

    private const int NUM_MUGU_LED = 64;
    private const int NUM_CONNECT_SED = 32;

    private const int NUM_DEVICES = 8;
    private const int NUM_TOUCH = 5;
    private const int NUM_LED = (NUM_MUGU_LED + NUM_CONNECT_SED)*NUM_TOUCH;

    private Color32[][] noteLeds = new Color32[NUM_DEVICES][NUM_LED];

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (noteLeds == null || noteLeds.Length != NUM_DEVICES) noteLeds = new Color32[NUM_DEVICES][];
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void SetMuguLED(int muguId, int row, int col, Color32 color32)
    {
        // todo 後回し
    }

    void SetMuguLEDAll(int muguId, Color32 color32)
    {
        for (int i = 0; i < NUM_MUGU_LED; i++)
        {
            noteLeds[muguId / NUM_DEVICES][(muguId % NUM_DEVICES) * (NUM_MUGU_LED+NUM_CONNECT_SED) + i] = color32;
        }
    }

    void SetConnectLED(int connectId, int row, Color32 color32)
    {
        noteLeds[connectId / NUM_DEVICES][(connectId % NUM_DEVICES) * (NUM_MUGU_LED + NUM_CONNECT_SED) + NUM_MUGU_LED + row] = color32;
    }

    void SetConnectLEDAll(int connectId, Color32 color32)
    {
        for (int i = 0; i < NUM_MUGU_LED; i++)
        {
            noteLeds[connectId / NUM_DEVICES][(connectId % NUM_DEVICES) * (NUM_MUGU_LED + NUM_CONNECT_SED) + NUM_MUGU_LED + i] = color32;
        }
    }
}
