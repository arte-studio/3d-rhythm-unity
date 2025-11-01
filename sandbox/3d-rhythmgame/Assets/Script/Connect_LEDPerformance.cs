using UnityEngine;

public class Connect_LEDPerformance : MonoBehaviour
{
    public ConnectLEDGenerator connectGenerator;
    private GameObject[,] frontLEDs;
    UdpController udpController;

    void Awake()
    {
        connectGenerator = GetComponentInChildren<ConnectLEDGenerator>();
    }
    private void Start()
    {
        udpController = GameObject.Find("UdpController").GetComponent<UdpController>();
    }

    public void SetLEDGenerate()
    {
        if (connectGenerator != null)
            frontLEDs = connectGenerator.GetFrontLEDs();

    }

    public void SetLEDColor(int row, int col, Color color)
    {
        if (frontLEDs != null)
            frontLEDs[row, col].GetComponent<Renderer>().material.color = color;
    }

    public void SetAllLEDColor(Color color)
    {
        if (frontLEDs != null)
        {
            foreach (var led in frontLEDs)
                led.GetComponent<Renderer>().material.color = color;
            // --- ここでゲームのロジックに応じてLEDの色を更新してください ---
            // 例: performanceLeds[デバイスID][LED番号] = new Color32(255, 0, 0, 255);
            // 例: noteLeds[デバイスID][LED番号] = Color.blue;

            // フレームごとに全デバイスにLEDデータを送信
            //if (udpController != null) udpController.SendAllLedData(); これはUDP送信の上書きしてるらしいので消していいらしい
        }

    }
}
