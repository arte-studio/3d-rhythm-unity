using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;

/// <summary>
/// ESP32デバイスとUDPブロードキャスト通信を行い、LEDとタッチセンサーを制御するクラス
/// </summary>
public class UdpController : MonoBehaviour
{
    // --- ネットワーク設定 ---
    [Header("Network Settings")]
    [Tooltip("UDPパケットを送信するブロードキャストアドレス")]
    public string broadcastAddress = "192.168.10.255";
    [Tooltip("ESP32側が待ち受けるポート番号")]
    public int espPort = 8888;
    [Tooltip("Unity側が待ち受けるポート番号")]
    public int unityPort = 9999;

    // --- LED設定 ---
    [Header("LED Settings")]
    private const int NUM_DEVICES = 8;
    private const int NUM_TOUCH = 5;
    private const int NUM_PERF_LEDS = 480; // 演出用LED
    private const int NUM_NOTE_LEDS = 470; // ノーツ用LED

    // 各ESPデバイスのLED色データを保持する配列
    // [デバイスID][LEDインデックス]
    // private Color32[][] performanceLeds = new Color32[NUM_DEVICES][];
    private Color32[][] noteLeds = new Color32[NUM_DEVICES][];

    // --- タッチセンサー ---
    [Header("Touch Sensor State")]
    [Tooltip("各デバイスのタッチセンサーの状態をリアルタイムで格納する (読み取り専用)")]
    // [デバイスID][センサーインデックス]
    public bool[][] touchStates = new bool[NUM_DEVICES][];

    private bool arraysInitialized;

    // --- UDP関連 ---
    private UdpClient sendClient;    // 送信用のUDPクライアント
    private UdpClient receiveClient; // 受信用のUDPクライアント
    private Thread receiveThread;    // 受信処理をバックグラウンドで行うためのスレッド
    private IPEndPoint sendEndPoint; // 送信先のエンドポイント

    // --- デバイス管理 ---
    [Header("Device Management")]
    [Tooltip("各デバイスが登録済みかを表示")]
    public bool[] deviceRegistered = new bool[NUM_DEVICES];
    private IPEndPoint[] deviceEndPoints = new IPEndPoint[NUM_DEVICES];

    // --- データ送信用バッファ ---
    // パケットを毎回生成すると負荷が高いため、使いまわすためのバッファ
    // 演出用LEDパケット (ID, Type, 480 * 3 bytes)
    private byte[] perfPacket = new byte[2 + NUM_PERF_LEDS * 3];
    // ノーツ用LEDパケット (ID, Type, 470 * 3 bytes)
    private byte[] notePacket = new byte[2 + NUM_NOTE_LEDS * 3];

    private lineterm term;

    /// <summary>
    /// スクリプトが有効になった最初のフレームで呼ばれる初期化処理
    /// </summary>
    void Awake()
    {
        InitializeArrays();
    }

    void Start()
    {
        Application.targetFrameRate = 60; // 60fpsに設定

        term = FindFirstObjectByType<lineterm>();
        if (term == null)
        {
            Debug.LogError("lineterm コンポーネントが見つかりません。UdpController を無効化します。");
            enabled = false;
            return;
        }

        InitializeArrays();
        InitializeUdp();

        // テスト用にLEDデータを初期化（不要な場合はコメントアウトしてください）
        InitializeTestData();
    }

    /// <summary>
    /// 毎フレーム呼ばれる更新処理 
    /// </summary>
    void Update()
    {
        if (!arraysInitialized)
        {
            return;
        }
        // フレームごとに全デバイスにLEDデータを送信
        SendAllLedData();

        // デバッグ用に、Spaceキーが押されたらタッチ状態をコンソールに表示
        if (Input.GetKeyDown(KeyCode.Space))
        {
            for (int i = 0; i < NUM_DEVICES; i++)
            {
                // string.Joinで配列をカンマ区切りの文字列に変換して表示
                Debug.Log($"Device {i} Touch: {string.Join(", ", touchStates[i])}");
            }
        }
    }

    /// <summary>
    /// アプリケーション終了時に呼ばれる処理
    /// </summary>
    void OnDestroy()
    {
        // スレッドやクライアントを正しく閉じてリソースを解放する
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Abort();
        if (sendClient != null) sendClient.Close();
        if (receiveClient != null) receiveClient.Close();
    }

    /// <summary>
    /// LEDやタッチセンサーの配列を初期化する
    /// </summary>
    private void InitializeArrays()
    {
        if (arraysInitialized)
        {
            return;
        }

        // if (performanceLeds == null || performanceLeds.Length != NUM_DEVICES) performanceLeds = new Color32[NUM_DEVICES][];
        if (noteLeds == null || noteLeds.Length != NUM_DEVICES) noteLeds = new Color32[NUM_DEVICES][];
        if (touchStates == null || touchStates.Length != NUM_DEVICES) touchStates = new bool[NUM_DEVICES][];
        if (deviceRegistered == null || deviceRegistered.Length != NUM_DEVICES) deviceRegistered = new bool[NUM_DEVICES];
        if (deviceEndPoints == null || deviceEndPoints.Length != NUM_DEVICES) deviceEndPoints = new IPEndPoint[NUM_DEVICES];

        for (int i = 0; i < NUM_DEVICES; i++)
        {
            // if (performanceLeds[i] == null || performanceLeds[i].Length != NUM_PERF_LEDS * 3)
            // {
            //     performanceLeds[i] = new Color32[NUM_PERF_LEDS * 3];
            // }
            if (noteLeds[i] == null || noteLeds[i].Length != NUM_NOTE_LEDS)
            {
                noteLeds[i] = new Color32[NUM_NOTE_LEDS];
            }
            if (touchStates[i] == null || touchStates[i].Length != NUM_TOUCH)
            {
                touchStates[i] = new bool[NUM_TOUCH];
            }
            deviceRegistered[i] = false;
            deviceEndPoints[i] = null;
        }

        arraysInitialized = true;
    }

    /// <summary>
    /// UDPクライアントを初期化し、受信スレッドを開始する
    /// </summary>
    private void InitializeUdp()
    {
        // 送信クライアントのセットアップ
        sendClient = new UdpClient();
        sendEndPoint = new IPEndPoint(IPAddress.Parse(broadcastAddress), espPort);
        Debug.Log($"UDPパケットの送信先: {sendEndPoint}");

        // 受信クライアントとスレッドのセットアップ
        receiveClient = new UdpClient(unityPort);
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true; // アプリ終了時にスレッドも自動で終了させる
        receiveThread.Start();
        Debug.Log($"UDPパケットの待受ポート: {unityPort}");
    }

    /// <summary>
    /// 全てのデバイス（10台分）のLEDデータを送信するメインの関数
    /// </summary>
    public void SendAllLedData()
    {
        if (!arraysInitialized || sendClient == null)
        {
            return;
        }
        // 0番から9番まで、すべてのデバイスIDに対してループ
        for (int deviceId = 0; deviceId < NUM_DEVICES; deviceId++)
        {
            // 未登録のデバイスはスキップ
            if (!deviceRegistered[deviceId])
            {
                continue;
            }
            // 宛先をブロードキャストから、登録済みのIPアドレスに変更
            IPEndPoint targetEndPoint = deviceEndPoints[deviceId];

            // 演出用LEDデータを3パケットに分けて送信
            for (int i = 0; i < 3; i++)
            {
                perfPacket[0] = (byte)deviceId;
                perfPacket[1] = (byte)i;
                // linetermからGetBytes2で配列を取得
                int begin = deviceId * 4 + 30 * i;
                int end = begin + 3;
                byte[] ledData = term.GetBytes2(begin, end);
                int array_length = ledData.Length;
                Debug.Log($"length{array_length}");
                int numPerfLeds = Math.Min(NUM_PERF_LEDS, array_length);
                if (deviceId == 7) numPerfLeds = Math.Min(230, array_length);// NUM_PERF_LEDS / 2;
                for (int j = 0; j < numPerfLeds; j++)
                {
                    perfPacket[2 + j * 3 + 0] = ledData[j * 3 + 0]; // todo キモいけどここ変えた
                    perfPacket[2 + j * 3 + 1] = ledData[j * 3 + 1];
                    perfPacket[2 + j * 3 + 2] = ledData[j * 3 + 2];
                }
                // --- 送信先を変更 ---
                try
                {
                    sendClient.Send(perfPacket, perfPacket.Length, targetEndPoint);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error sending to Device {deviceId} at {targetEndPoint}: {e.Message}");
                    deviceRegistered[deviceId] = false; // エラーが出たら登録を解除して再発見を促す
                }
            }

            // ノーツ用LEDデータを1パケットで送信
            notePacket[0] = (byte)deviceId;
            notePacket[1] = 3;
            for (int i = 0; i < NUM_NOTE_LEDS; i++)
            {
                notePacket[2 + i * 3 + 0] = noteLeds[deviceId][i].r;
                notePacket[2 + i * 3 + 1] = noteLeds[deviceId][i].g;
                notePacket[2 + i * 3 + 2] = noteLeds[deviceId][i].b;
            }
            // --- 送信先を変更 ---
            sendClient.Send(notePacket, notePacket.Length, targetEndPoint);

            // 送信完了のログ（必要に応じてコメントアウトしてください）
            // Debug.Log($"Sent LED data to Device {deviceId}");
        }
        // 送信完了のログ
        // Debug.Log("Sent Data");
    }

    /// <summary>
    /// バックグラウンドスレッドでESP32からのデータを受信し続ける関数
    /// </summary>
    private void ReceiveData()
    {
        IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            try
            {
                // データを受信するまでここで待機
                byte[] data = receiveClient.Receive(ref anyIP);

                // 受信したデータの内容をログに出力
                // Debug.Log($"Received {data.Length} bytes from {anyIP}");
                // Debug.Log($"Data: {BitConverter.ToString(data)}");

                // 発見パケット [255][ID] の処理を追加
                if (data.Length == 2 && data[0] == 255)
                {
                    int deviceId = data[1];
                    if (deviceId >= 0 && deviceId < NUM_DEVICES)
                    {
                        // 新しいデバイス、またはIPアドレスが変わったデバイスを発見
                        if (!deviceRegistered[deviceId])
                        {
                            Debug.Log($"Device {deviceId} 発見！ IP: {anyIP.Address}. ACKを送信します。");
                        }
                        else if (!deviceEndPoints[deviceId].Address.Equals(anyIP.Address))
                        {
                            Debug.Log($"Device {deviceId} IP更新！ IP: {anyIP.Address}. ACKを送信します。");
                        }
                        else
                        {
                            Debug.Log($"Device {deviceId} 再発見！ IP: {anyIP.Address}. ACKを送信します。");
                        }
                        deviceEndPoints[deviceId] = new IPEndPoint(anyIP.Address, espPort);
                        deviceRegistered[deviceId] = true;
                        // 確認応答(ACK) [254] をユニキャストで返信
                        byte[] ackPacket = { 254 };
                        sendClient.Send(ackPacket, ackPacket.Length, deviceEndPoints[deviceId]);
                    }
                }
                // パケットの長さが期待通りかチェック (ID 1バイト + Touch NUM_TOUCHバイト)
                else if (data.Length == NUM_TOUCH+1)
                {
                    int deviceId = data[0];
                    if (deviceId >= 0 && deviceId < NUM_DEVICES)
                    {
                        for (int i = 0; i < NUM_TOUCH; i++)
                        {
                            // 受信した 1 or 0 を bool (true/false) に変換して配列に格納
                            touchStates[deviceId][i] = (data[i + 1] == 1);
                        }
                    }
                }
                else
                {
                    // Debug.LogWarning($"Unexpected packet size: {data.Length} bytes from {anyIP}");
                    // // 受信したデータの内容をログに出力
                    // Debug.LogWarning($"Data: {BitConverter.ToString(data)}");
                }
            }
            catch (Exception err)
            {
                // スレッドが中断された場合などのエラーをログに出力
                Debug.LogError(err.ToString());
            }
        }
    }

    /// <summary>
    /// 動作確認用に、LED配列を初期の色で塗りつぶす
    /// </summary>
    private void InitializeTestData()
    {
        for (int i = 0; i < NUM_DEVICES; i++)
        {
            // デバイスごとに少しずつ色相をずらした色を生成
            Color32 perfColor = Color.HSVToRGB((float)i / NUM_DEVICES, 0.8f, 1.0f);
            Color32 noteColor = Color.HSVToRGB(((float)i / NUM_DEVICES + 0.5f) % 1.0f, 1.0f, 1.0f);

            // for (int j = 0; j < NUM_PERF_LEDS * 3; j++)
            // {
            //     performanceLeds[i][j] = perfColor;
            // }
            for (int j = 0; j < NUM_NOTE_LEDS; j++)
            {
                noteLeds[i][j] = noteColor;
            }
        }
        Debug.Log("テスト用のLEDデータを初期化しました。");
    }
}
