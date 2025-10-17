using System.IO.Ports; // シリアル通信に必要
using UnityEngine;

public class LedController : MonoBehaviour
{
    // ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
    // ★ 自分のPC環境に合わせて、このポート名を必ず変更してください ★
    // ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
    public string portName = "COM6";
    public int baurate = 9600;

    private SerialPort serialPort;

    void Start()
    {
        serialPort = new SerialPort(portName, baurate);
        serialPort.ReadTimeout = 10;
        try
        {
            serialPort.Open();
            Debug.Log("シリアルポートを開きました。ポート名: " + portName);
        }
        catch (System.Exception e)
        {
            Debug.LogError("ポートを開けませんでした: " + e.Message);
        }
    }

    void Update()
    {
        // '1'キーで虹色パターン
        if (Input.GetKeyDown(KeyCode.I))
        {
            SendCommand("1");
            Debug.Log("虹色パターン開始");
        }
        // 'R'キーで赤色点灯
        if (Input.GetKeyDown(KeyCode.E))
        {
            SendCommand("R");
            Debug.Log("赤色点灯");
        }
        // '0'キーで消灯
        if (Input.GetKeyDown(KeyCode.O))
        {
            SendCommand("0");
            Debug.Log("LED消灯");
        }

        try
        {
            if (serialPort.IsOpen)
            {
                // Arduinoからの返信を1行読み取ってコンソールに表示
                string message = serialPort.ReadLine();
                Debug.Log(message);
            }
        }
        catch
        {
            // データが来ていない場合はTimeoutExceptionが発生するが、
            // これは正常な動作なので、何もしない。
        }
    }

    // Arduinoにコマンドを送信する関数
    public void SendCommand(string command)
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            serialPort.Write(command);
            Debug.Log("コマンド送信: " + command);
        }
    }

    // アプリケーション終了時にポートを閉じる
    void OnApplicationQuit()
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            serialPort.Close();
            Debug.Log("シリアルポートを閉じました。");
        }
    }
}