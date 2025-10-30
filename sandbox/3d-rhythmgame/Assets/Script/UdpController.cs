using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent; // スレッドセーフなキューのために必要
using System.Text; // StringBuilder のために必要

/// <summary>
/// ESP32デバイスとUDPブロードキャスト通信を行い、LEDとタッチセンサーを制御するクラス
/// </summary>
public class UdpController : MonoBehaviour
{
    // --- ネットワーク設定 ---
    [Header("Network Settings")]
    [Tooltip("UDPパケットを送信するブロードキャストアドレス")]
    public string broadcastAddress = "192.168.0.255";
    [Tooltip("ESP32側が待ち受けるポート番号")]
    public int espPort = 8888;
    [Tooltip("Unity側が待ち受けるポート番号")]
    public int unityPort = 9999;

    [SerializeField]
    private StatusDisplay targetDisplay; // ステータス表示用UIコンポーネント

    // --- LED設定 ---
    [Header("LED Settings")]
    private const int NUM_DEVICES = 8;
    private const int NUM_TOUCH = 5;
    private const int NUM_PERF_LEDS = 480; // 演出用LED
    private const int NUM_NOTE_LEDS = 470; // ノーツ用LED

    // --- タッチセンサー ---
    [Header("Touch Sensor State")]
    [Tooltip("各デバイスのタッチセンサーの状態をリアルタイムで格納する (読み取り専用)")]
    // [デバイスID][センサーインデックス]
    public bool[][] touchStates = new bool[NUM_DEVICES][];

    private bool arraysInitialized;

    // --- UDP関連 ---
    private UdpClient sendClient;      // 送信用のUDPクライアント
    private UdpClient receiveClient; // 受信用のUDPクライアント
    private Thread receiveThread;      // 受信処理をバックグラウンドで行うためのスレッド
    private IPEndPoint sendEndPoint; // 送信先のエンドポイント
    
    // メインスレッドへタッチイベントを渡すためのキュー (タッチONの瞬間)
    private ConcurrentQueue<(int deviceId, int sensorId)> touchEventQueue = new ConcurrentQueue<(int, int)>();
    
    // ★追加: デバイス発見をメインスレッドに通知するキュー
    private ConcurrentQueue<int> discoveryQueue = new ConcurrentQueue<int>();
    // ★追加: タッチ通信受信(生存確認)をメインスレッドに通知するキュー
    private ConcurrentQueue<int> touchActivityQueue = new ConcurrentQueue<int>();


    // --- デバイス管理 ---
    [Header("Device Management")]
    [Tooltip("各デバイスが登録済みかを表示")]
    public bool[] deviceRegistered = new bool[NUM_DEVICES];
    private IPEndPoint[] deviceEndPoints = new IPEndPoint[NUM_DEVICES];

    // ★追加: デバイスのステータス監視用
    // 最後にデバイスを発見した時刻 (Time.time)
    private float[] lastDiscoveryTime = new float[NUM_DEVICES];
    // 最後にタッチパケットを受信した時刻 (Time.time)
    private float[] lastTouchTime = new float[NUM_DEVICES];
    // StatusDisplay用の文字列を構築 (GC Alloc対策)
    private StringBuilder statusBuilder = new StringBuilder(); 

    // --- データ送信用バッファ ---
    // パケットを毎回生成すると負荷が高いため、使いまわすためのバッファ
    // 演出用LEDパケット (ID, Type, 480 * 3 bytes)
    private byte[] perfPacket = new byte[2 + NUM_PERF_LEDS * 3];
    // ノーツ用LEDパケット (ID, Type, 470 * 3 bytes)
    private byte[] notePacket = new byte[2 + NUM_NOTE_LEDS * 3];

    private lineterm term;
    private NoteLeds noteLedsComponent;
    // private TouchNotes_Flag touchNotesFlagComponent; // GameManagerが処理するため不要

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

        // TouchNotes_Flag の検索は不要

        noteLedsComponent = FindFirstObjectByType<NoteLeds>();
        if (noteLedsComponent == null)
        {
            Debug.LogError("NoteLeds コンポーネントが見つかりません。UdpController を無効化します。");
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
        
        // --- タッチONイベント処理 ---
        // キューにデータがなくなるまで、メインスレッドで安全に処理する
        while (touchEventQueue.TryDequeue(out var touchEvent))
        {
            if (GameManager.Instance != null)
            {
                // GameManager にタッチイベントを通知
                GameManager.Instance.HandleTouchInput(touchEvent.deviceId, touchEvent.sensorId);
            }
        }
        
        // --- ★ここから追加 (デバイスステータス更新処理) ---

        // 1. デバイス発見キューを処理
        // (別スレッドからEnqueueされたデバイスIDを取り出す)
        while (discoveryQueue.TryDequeue(out int discoveredId))
        {
            // ★変更: 毎回 "First" (最終発見) の時刻を更新する (再発見を反映するため)
            // 0f は「未記録」とする。初回発見時のみ時刻を記録
            // if (lastDiscoveryTime[discoveredId] == 0f) 
            // {
            //     lastDiscoveryTime[discoveredId] = Time.time;
            // }
            lastDiscoveryTime[discoveredId] = Time.time;
        }
        
        // 2. タッチ通信受信キューを処理
        // (別スレッドからEnqueueされたデバイスIDを取り出す)
        while (touchActivityQueue.TryDequeue(out int activeId))
        {
            // 最後にタッチパケットを受信した時刻を常に更新
            lastTouchTime[activeId] = Time.time;
        }
        
        // 3. StatusDisplay (IMGUI) を更新
        UpdateStatusDisplay();
        
        // --- ★追加ここまで ---
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

        if (touchStates == null || touchStates.Length != NUM_DEVICES) touchStates = new bool[NUM_DEVICES][];
        if (deviceRegistered == null || deviceRegistered.Length != NUM_DEVICES) deviceRegistered = new bool[NUM_DEVICES];
        if (deviceEndPoints == null || deviceEndPoints.Length != NUM_DEVICES) deviceEndPoints = new IPEndPoint[NUM_DEVICES];

        // ★追加: ステータス用配列の初期化
        if (lastDiscoveryTime == null || lastDiscoveryTime.Length != NUM_DEVICES) lastDiscoveryTime = new float[NUM_DEVICES];
        if (lastTouchTime == null || lastTouchTime.Length != NUM_DEVICES) lastTouchTime = new float[NUM_DEVICES];

        for (int i = 0; i < NUM_DEVICES; i++)
        {
            // if (performanceLeds[i] == null || performanceLeds[i].Length != NUM_PERF_LEDS * 3)
            // {
            //     performanceLeds[i] = new Color32[NUM_PERF_LEDS * 3];
            // }
            // if (noteLeds[i] == null || noteLeds[i].Length != NUM_NOTE_LEDS)
            // {
            //     noteLeds[i] = new Color32[NUM_NOTE_LEDS];
            // }
            if (touchStates[i] == null || touchStates[i].Length != NUM_TOUCH)
            {
                touchStates[i] = new bool[NUM_TOUCH];
            }
            deviceRegistered[i] = false;
            deviceEndPoints[i] = null;

            // ★追加: 時刻を 0f (未受信) で初期化
            lastDiscoveryTime[i] = 0f;
            lastTouchTime[i] = 0f;
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
        try
        {
            receiveClient = new UdpClient(unityPort);
        }
        catch (Exception e)
        {
            Debug.LogError($"ポート {unityPort} でのUDPクライアントの初期化に失敗しました。ポートが既に使用されている可能性があります。: {e.Message}");
            enabled = false;
            return;
        }
        
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
                
                // ★追加: ledDataが期待通りの長さかチェック (境界外エラー防止)
                if (ledData.Length < NUM_PERF_LEDS * 3)
                {
                    // Debug.LogWarning($"GetBytes2({begin}, {end}) が返したデータ長 ({ledData.Length}) が不足しています。スキップします。");
                    continue; // このパケットの処理をスキップ
                }

                for (int j = 0; j < NUM_PERF_LEDS; j++)
                {
                    perfPacket[2 + j * 3 + 1] = ledData[j * 3 + 0]; // todo キモいけどここ変えた
                    perfPacket[2 + j * 3 + 0] = ledData[j * 3 + 1];
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
                // (Startでチェック済みのため、基本的には安全)
                Color32 noteLed = noteLedsComponent.GetLedColor(deviceId, i);
                notePacket[2 + i * 3 + 0] = noteLed.r;
                notePacket[2 + i * 3 + 1] = noteLed.g;
                notePacket[2 + i * 3 + 2] = noteLed.b;
            }
            // --- 送信先を変更 ---
            try
            {
                sendClient.Send(notePacket, notePacket.Length, targetEndPoint);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error sending notes to Device {deviceId} at {targetEndPoint}: {e.Message}");
                deviceRegistered[deviceId] = false; // エラーが出たら登録を解除して再発見を促す
            }


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
                        else if (deviceEndPoints[deviceId] == null || !deviceEndPoints[deviceId].Address.Equals(anyIP.Address))
                        {
                            Debug.Log($"Device {deviceId} IP更新！ IP: {anyIP.Address}. ACKを送信します。");
                        }
                        else
                        {
                            Debug.Log($"Device {deviceId} 再発見！ IP: {anyIP.Address}. ACKを送信します。");
                        }
                        deviceEndPoints[deviceId] = new IPEndPoint(anyIP.Address, espPort);
                        deviceRegistered[deviceId] = true;
                        
                        // ★追加: メインスレッドにデバイス発見を通知
                        discoveryQueue.Enqueue(deviceId);
                        
                        // 確認応答(ACK) [254] をユニキャストで返信
                        byte[] ackPacket = { 254 };
                        sendClient.Send(ackPacket, ackPacket.Length, deviceEndPoints[deviceId]);
                    }
                }
                // パケットの長さが期待通りかチェック (ID 1バイト + Touch NUM_TOUCHバイト)
                else if (data.Length == NUM_TOUCH + 1)
                {
                    int deviceId = data[0];
                    if (deviceId >= 0 && deviceId < NUM_DEVICES)
                    {
                        for (int i = 0; i < NUM_TOUCH; i++)
                        {
                            // 受信した 1 or 0 を bool (true/false) に変換
                            bool newState = (data[i + 1] == 1);
                            
                            // ★変更: 状態が ON になった瞬間だけをキューに入れる
                            if (newState == true && touchStates[deviceId][i] == false)
                            {
                                // メインスレッドで処理するため、イベントをキューに追加
                                touchEventQueue.Enqueue((deviceId, i));
                            }
                            
                            // メインスレッドが参照する配列の状態を更新
                            touchStates[deviceId][i] = newState; 
                        }

                        // ★追加: タッチパケットを受信したこと(生存確認)をメインスレッドに通知
                        touchActivityQueue.Enqueue(deviceId);
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

    // --- ★ここから追加 (ステータス表示メソッド) ---
    
    /// <summary>
    /// デバイスの接続状況と通信状況を StatusDisplay に表示する
    /// (IMGUIはリッチテキスト <color=...> タグをサポートしています)
    /// </summary>
    private void UpdateStatusDisplay()
    {
        if (targetDisplay == null)
        {
            return;
        }

        // StringBuilder をクリアして再利用 (新しい文字列インスタンスの生成を避ける)
        statusBuilder.Clear();
        statusBuilder.AppendLine("--- Device Status (Time.time) ---");
        
        float currentTime = Time.time; // 現在時刻を一度だけ取得

        for (int i = 0; i < NUM_DEVICES; i++)
        {
            statusBuilder.Append($"Dev {i}: ");

            // 1. 登録状態 (Registered)
            if (deviceRegistered[i])
            {
                statusBuilder.Append("<color=cyan>[REG]</color> ");
            }
            else
            {
                statusBuilder.Append("[---] ");
            }

            // 2. 初回発見 (First Contact) -> 最終発見 (Last Discovery) に変更
            if (lastDiscoveryTime[i] > 0f)
            {
                // 最後に発見されてからの経過時間
                // ★変更: ラベルを "First" -> "Discovery" に変更
                statusBuilder.Append($"Discovery: {(currentTime - lastDiscoveryTime[i]):F1}s ago. ");
            }
            else
            {
                // ★変更: ラベルを "First" -> "Discovery" に変更
                statusBuilder.Append("Discovery: N/A. ");
            }

            // 3. 最終タッチ通信 (Last Touch)
            if (lastTouchTime[i] > 0f)
            {
                // 最後にタッチパケットを受信してからの経過時間
                float elapsed = currentTime - lastTouchTime[i];
                
                // 2秒以上途絶えたら警告 (赤色)
                string colorTag = (elapsed > 2.0f) ? "<color=red>" : "<color=green>";
                
                statusBuilder.Append($"LastTouch: {colorTag}{elapsed:F1}s ago</color>");
            }
            else
            {
                // まだ一度もタッチパケットを受信していない
                statusBuilder.Append("LastTouch: N/A");
            }

            statusBuilder.AppendLine(); // 次の行へ (改行)
        }
        
        // 構築した文字列を StatusDisplay コンポーネントの public 変数に設定
        targetDisplay.statusText = statusBuilder.ToString();
    }
    // --- ★追加ここまで ---


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

            // ... (テストデータ設定) ...
            // (この部分は元のコードから省略されているため、そのままにしています)
        }
        Debug.Log("テスト用のLEDデータを初期化しました。");
    }
}

