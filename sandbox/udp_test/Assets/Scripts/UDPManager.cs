using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

public class UDPManager : MonoBehaviour
{
    // 送信設定
    public string unityIp = "192.168.10.16"; // Unityが動作しているPCのIPアドレス
    public int sendToEsp32Port = 8888;        // ESP32への送信ポート(pc -> esp32)
    public string esp32Ip = "192.168.10.35"; // ESP32のIPアドレス

    // 受信設定
    public int receiveFromEsp32Port = 9999;   // ESP32からの受信ポート(esp32 -> pc)

    private UdpClient udpClient;
    private Thread receiveThread;

    void Start()
    {
        udpClient = new UdpClient(receiveFromEsp32Port);

        // UnityMainThreadDispatcherのインスタンスを生成（シーンに存在しない場合）
        UnityMainThreadDispatcher.Instance();

        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();

        Debug.Log("UDP Manager started. Listening on port " + receiveFromEsp32Port);
    }

    void Update()
    {
        //// キーボード入力でSendDataを呼び出す
        //if (Input.GetKeyDown(KeyCode.A))
        //{
        //    SendData("toggle_led_a");
        //    Debug.Log("A key pressed, sent toggle_led_a");
        //}
        //if (Input.GetKeyDown(KeyCode.B))
        //{
        //    SendData("toggle_led_b");
        //    Debug.Log("B key pressed, sent toggle_led_b");
        //}
    }

    private void ReceiveData()
    {
        while (true)
        {
            try
            {
                Debug.Log("Receive Thread: Waiting for data..."); // ★追加1

                IPEndPoint anyIp = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udpClient.Receive(ref anyIp);

                Debug.Log("Receive Thread: Packet received!"); // ★追加2

                string message = Encoding.UTF8.GetString(data);

                // 受信したデータをメインスレッドに渡して処理
                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    HandleReceivedData(message);
                });
            }
            catch (SocketException e)
            {
                Debug.LogWarning("SocketException: " + e.Message);
            }
        }
    }

    // UDPデータをESP32に送信する関数
    public void SendData(string message)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            UdpClient sender = new UdpClient();
            sender.Send(data, data.Length, esp32Ip, sendToEsp32Port);
            sender.Close();
            Debug.Log("Sent to ESP32: " + message);
        }
        catch (SocketException e)
        {
            Debug.LogError("SocketException: " + e.Message);
        }
    }

    // 受信したデータをメインスレッドで処理する関数
    private void HandleReceivedData(string message)
    {
        Debug.Log("Received and handled on Main Thread: " + message);

        // ここに受信データに応じた処理を記述
        if (message.Contains("on"))
        {
            Debug.Log("LED is now ON.");
        }
        else if (message.Contains("off"))
        {
            Debug.Log("LED is now OFF.");
        }
    }

    void OnApplicationQuit()
    {
        if (receiveThread != null)
        {
            receiveThread.Abort();
        }
        if (udpClient != null)
        {
            udpClient.Close();
        }
    }


    // インスペクターから設定するためのPublicな変数
    public InputAction toggleA;
    public InputAction toggleB;
    public InputAction toggleC;

    void OnEnable()
    {
        // アクションを有効化
        toggleA.Enable();
        toggleB.Enable();
        toggleC.Enable();

        // アクションが実行されたときのイベントにメソッドを登録
        toggleA.performed += ctx => SendData("0");
        toggleB.performed += ctx => SendData("1");
        toggleC.performed += ctx => SendData("R");
        //toggleB.performed += ctx => SendData("toggle_led_b");
    }

    void OnDisable()
    {
        // アクションを無効化
        toggleA.Disable();
        toggleB.Disable();
    }
}