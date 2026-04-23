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
/// ★根本対策：UdpClient.Receive がGCを発生させるため、Socket.ReceiveFrom を使用
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
    private const int NUM_PERF_LEDS = 480; // 演出用LED (4ストリップ * 120 LED)
    private const int NUM_PREF_LEDS_7 = 240; // デバイス7用の演出LED (2ストリップ * 120 LED)
    private const int NUM_NOTE_LEDS = 470; // ノーツ用LED
    
    // 1ストリップあたりのバイト長 (120 LED * 3 バイト)
    private const int BYTES_PER_STRIP = 360; 

    // --- タッチセンサー ---
    [Header("Touch Sensor State")]
    [Tooltip("各デバイスのタッチセンサーの状態をリアルタイムで格納する (読み取り専用)")]
    // [デバイスID][センサーインデックス]
    public bool[][] touchStates = new bool[NUM_DEVICES][];

    private bool arraysInitialized;

    // --- UDP関連 ---
    private UdpClient sendClient;      // 送信用のUDPクライアント
    // private UdpClient receiveClient; // ★GC対策のため Socket に変更
    private Socket receiveSocket;      // ★GC対策：受信用のソケット
    private byte[] receiveBuffer = new byte[64]; // ★GC対策：受信バッファ (6バイトパケット等には十分)
    
    private Thread receiveThread;      // 受信処理をバックグラウンドで行うためのスレッド
    private IPEndPoint sendEndPoint; // 送信先のエンドポイント
    
    // メインスレッドへタッチイベントを渡すためのキュー (タッチONの瞬間)
    private ConcurrentQueue<(int deviceId, int sensorId)> touchEventQueue = new ConcurrentQueue<(int, int)>();
    
    // デバイス発見をメインスレッドに通知するキュー
    private ConcurrentQueue<int> discoveryQueue = new ConcurrentQueue<int>();
    // タッチ通信受信(生存確認)をメインスレッドに通知するキュー
    private ConcurrentQueue<int> touchActivityQueue = new ConcurrentQueue<int>();


    // --- デバイス管理 ---
    [Header("Device Management")]
    [Tooltip("各デバイスが登録済みかを表示")]
    public bool[] deviceRegistered = new bool[NUM_DEVICES];
    private IPEndPoint[] deviceEndPoints = new IPEndPoint[NUM_DEVICES];

    // デバイスのステータス監視用
    // 最後にデバイスを発見した時刻 (Time.time)
    private float[] lastDiscoveryTime = new float[NUM_DEVICES];
    // 最後にタッチパケットを受信した時刻 (Time.time)
    private float[] lastTouchTime = new float[NUM_DEVICES];
    // 最後に各センサがタッチされた時刻 [deviceId][sensorId]
    private float[][] lastTouchTimePerSensor;
    // StatusDisplay用の文字列を構築 (GC Alloc対策)
    private StringBuilder statusBuilder = new StringBuilder(); 

    // --- データ送信用バッファ ---
    // パケットを毎回生成すると負荷が高いため、使いまわすためのバッファ
    // 演出用LEDパケット (ID, Type, 480 * 3 bytes)
    private byte[] perfPacket = new byte[2 + NUM_PERF_LEDS * 3];
    // ノーツ用LEDパケット (ID, Type, 470 * 3 bytes)
    private byte[] notePacket = new byte[2 + NUM_NOTE_LEDS * 3];

    private PerfLeds perfLedsComponent;
    private NoteLeds noteLedsComponent;
    // private TouchNotes_Flag touchNotesFlagComponent; // 削除: GameManagerが処理するため不要

    [Header("UDP Settings")]
    [Tooltip("UDP送信の更新頻度（FPS）。1〜120の範囲で指定してください。")]
    [UnityEngine.Range(1, 120)]
    public int udpFps = 60;

    // 内部用：送信間隔（秒）と最終送信時刻
    private float udpInterval = 1f / 60f;
    private float lastUdpSendTime = 0f;

    /// <summary>
    /// 現在の実効 UDP 送信レートを返します（Inspector 設定に従った値）。
    /// 1 / udpInterval と同等です。
    /// </summary>
    public float CurrentUdpFps => udpInterval > 0f ? 1f / udpInterval : 0f;

    /// <summary>
    /// 最終送信からの経過秒数を返します（メインスレッドの Time.time を参照）。
    /// </summary>
    public float TimeSinceLastSend => Time.time - lastUdpSendTime;

    /// <summary>
    /// スクリプトが有効になった最初のフレームで呼ばれる初期化処理
    /// </summary>
    void Awake()
    {
        InitializeArrays();
    }

    // Inspector上でudpFpsを変更したときに即時反映する (Editor実行時およびインスペクタ編集時)
    void OnValidate()
    {
        udpFps = Mathf.Clamp(udpFps, 1, 120);
        udpInterval = 1f / (float)udpFps;
    }

    void Start()
    {
        Application.targetFrameRate = 60; // 60fpsに設定

        perfLedsComponent = FindFirstObjectByType<PerfLeds>();
        if (perfLedsComponent == null)
        {
            Debug.LogError("PerfLeds コンポーネントが見つかりません．UdpController を無効化します．");
            enabled = false;
            return;
        }

        // PerfLeds の perfLedData が利用可能か確認
        if (perfLedsComponent.perfLedData == null)
        {
            Debug.LogError("PerfLeds.perfLedData が参照できません．PerfLeds.cs側で public に設定されているか，または初期化が完了しているか確認してください．");
            enabled = false;
            return;
        }

        // 不足していたコンポーネントの初期化を追加
        noteLedsComponent = FindFirstObjectByType<NoteLeds>();
        if (noteLedsComponent == null)
        {
            Debug.LogError("NoteLeds コンポーネントが見つかりません．UdpController を無効化します．");
            enabled = false;
            return;
        }

        InitializeArrays();
        InitializeUdp();

        // テスト用にLEDデータを初期化（不要な場合はコメントアウトしてください）
        InitializeTestData();

        // UDP 送信間隔の初期化
        udpFps = Mathf.Clamp(udpFps, 1, 120);
        udpInterval = 1f / (float)udpFps;
        lastUdpSendTime = Time.time - udpInterval; // 起動直後にすぐ送信されるように
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
        // Inspectorで設定したFPSに合わせて送信
        if (Time.time - lastUdpSendTime >= udpInterval)
        {
            SendAllLedData();
            lastUdpSendTime = Time.time;
        }

        // デバッグ用に、Spaceキーが押されたらタッチ状態をコンソールに表示
        if (Input.GetKeyDown(KeyCode.Space))
        {
            for (int i = 0; i < NUM_DEVICES; i++)
            {
                // string.Joinで配列をカンマ区切りの文字列に変換して表示
                Debug.Log($"Device {i} Touch: {string.Join(", ", touchStates[i])}");
            }
        }
        
        // --- タッチイベント処理 ---
        // キューにデータがなくなるまで、メインスレッドで安全に処理する
        while (touchEventQueue.TryDequeue(out var touchEvent))
        {
            // センサごとの最終タッチ時刻を更新
            if (touchEvent.deviceId >= 0 && touchEvent.deviceId < NUM_DEVICES && touchEvent.sensorId >= 0 && touchEvent.sensorId < NUM_TOUCH)
            {
                if (lastTouchTimePerSensor == null)
                {
                    // 念のため初期化（通常は InitializeArrays() で初期化済み）
                    lastTouchTimePerSensor = new float[NUM_DEVICES][];
                    for (int d = 0; d < NUM_DEVICES; d++) lastTouchTimePerSensor[d] = new float[NUM_TOUCH];
                }
                lastTouchTimePerSensor[touchEvent.deviceId][touchEvent.sensorId] = Time.time;
                // デバイス全体の最終タッチ時刻も更新
                lastTouchTime[touchEvent.deviceId] = Time.time;
            }

            if (GameManager.Instance != null)
            {
                // GameManager にタッチイベントを通知
                GameManager.Instance.HandleTouchInput(touchEvent.deviceId, touchEvent.sensorId);
            }
        }
        
        // --- デバイスステータス更新処理 ---

        // 1. デバイス発見キューを処理
        // (別スレッドからEnqueueされたデバイスIDを取り出す)
        while (discoveryQueue.TryDequeue(out int discoveredId))
        {
            // 最終発見 の時刻を更新する (再発見を反映するため)
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
    }

    /// <summary>
    /// アプリケーション終了時に呼ばれる処理
    /// </summary>
    void OnDestroy()
    {
        // スレッドやクライアントを正しく閉じてリソースを解放する
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Abort();
        if (sendClient != null) sendClient.Close();
        // if (receiveClient != null) receiveClient.Close(); // ★GC対策: 変更
        if (receiveSocket != null) receiveSocket.Close(); // ★GC対策: 変更
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

        // ステータス用配列の初期化
        if (lastDiscoveryTime == null || lastDiscoveryTime.Length != NUM_DEVICES) lastDiscoveryTime = new float[NUM_DEVICES];
        if (lastTouchTime == null || lastTouchTime.Length != NUM_DEVICES) lastTouchTime = new float[NUM_DEVICES];

        for (int i = 0; i < NUM_DEVICES; i++)
        {
            if (touchStates[i] == null || touchStates[i].Length != NUM_TOUCH)
            {
                touchStates[i] = new bool[NUM_TOUCH];
            }
            deviceRegistered[i] = false;
            deviceEndPoints[i] = null;

            // 時刻を 0f (未受信) で初期化
            lastDiscoveryTime[i] = 0f;
            lastTouchTime[i] = 0f;
            // センサごとの最終タッチ時刻を初期化
            if (lastTouchTimePerSensor == null) lastTouchTimePerSensor = new float[NUM_DEVICES][];
            lastTouchTimePerSensor[i] = new float[NUM_TOUCH];
            for (int j = 0; j < NUM_TOUCH; j++) lastTouchTimePerSensor[i][j] = 0f;
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

        // --- ★GC対策: UdpClient の代わりに Socket を使用 ---
        try
        {
            // receiveClient = new UdpClient(unityPort); // ★GC対策: 削除
            
            receiveSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            receiveSocket.Bind(new IPEndPoint(IPAddress.Any, unityPort));
        }
        catch (Exception e)
        {
            Debug.LogError($"ポート {unityPort} でのUDPソケットの初期化に失敗しました．ポートが既に使用されている可能性があります．: {e.Message}");
            enabled = false;
            return;
        }
        // --- ★GC対策ここまで ---
        
        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true; // アプリ終了時にスレッドも自動で終了させる
        receiveThread.Start();
        Debug.Log($"UDPパケットの待受ポート: {unityPort}");
    }

    /// <summary>
    /// PerfLeds.cs の ConvertID と同じロジック．
    /// 譜面ID (0-89) を PerfLeds の物理ストリップID (0-89) に変換する．
    /// </summary>
    private int ConvertPerfLedsID(int i)
    {
        if (i < 0 || i >= 90) return -1; // 範囲外チェック
        int n = i % 30 * 3;
        if ((int)i / 30 == 0) n += 0;
        else if ((int)i / 30 == 1) n += 2;
        else if ((int)i / 30 == 2) n += 1;
        return n;
    }

    /// <summary>
    /// 全てのデバイス（10台分）のLEDデータを送信するメインの関数
    /// </summary>
    public void SendAllLedData()
    {
        // PerfLeds が準備完了しているかチェック
        if (!arraysInitialized || sendClient == null || perfLedsComponent.perfLedData == null || perfLedsComponent.perfLedData.Length < (90 * BYTES_PER_STRIP))
        {
            // PerfLeds がまだデータを生成していない (または初期化中)
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
                // デバイス7は i=0 のパケット(type 0)のみ送信し、i=1, 2 はスキップ
                if (deviceId == 7 && i > 0) continue; 
                
                perfPacket[0] = (byte)deviceId;
                perfPacket[1] = (byte)i;
                
                // --- 根本修正 (GC対策) ---
                // byte[] ledData = perfLedsComponent.GetBytes2(begin, end); // ★削除 (GCの原因)

                // このパケットで送信するストリップ数 (通常4, デバイス7は2)
                int numStripsThisPacket = (deviceId == 7) ? 2 : 4;
                // このパケットで送信するLED数 (通常480, デバイス7は240)
                int numPerfLeds = (deviceId == 7) ? NUM_PREF_LEDS_7 : NUM_PERF_LEDS;

                // 譜面IDの開始インデックス
                int beginNoteId = deviceId * 4 + 30 * i;
                
                // コピー先のオフセット (perfPacket の 2バイト目以降)
                int destOffset = 2;

                for (int j = 0; j < numStripsThisPacket; j++)
                {
                    // 譜面ID (0-89)
                    int noteId = beginNoteId + j; 
                    
                    // 物理ストリップID (0-89) に変換
                    int physicalStripId = ConvertPerfLedsID(noteId); 

                    // 物理IDが不正 (範囲外) の場合はスキップ
                    if (physicalStripId == -1)
                    {
                        Debug.LogWarning($"ConvertPerfLedsID({noteId}) が無効な値 -1 を返しました．");
                        destOffset += BYTES_PER_STRIP; // オフセットだけ進めておく (データはコピーされない)
                        continue; 
                    }

                    // コピー元のオフセット (perfLedData の当該ストリップの開始位置)
                    int sourceOffset = physicalStripId * BYTES_PER_STRIP;

                    // 境界チェック (安全のため)
                    if (perfLedsComponent.perfLedData.Length < sourceOffset + BYTES_PER_STRIP ||
                        perfPacket.Length < destOffset + BYTES_PER_STRIP)
                    {
                        Debug.LogError($"[GC Fix] Array.Copy 境界外エラー．noteId={noteId}, physicalId={physicalStripId}");
                        destOffset += BYTES_PER_STRIP;
                        continue; // このストリップのコピーをスキップ
                    }

                    // PerfLeds の perfLedData から perfPacket に 1ストリップ分(360 bytes)コピー
                    Array.Copy(perfLedsComponent.perfLedData, sourceOffset, perfPacket, destOffset, BYTES_PER_STRIP);

                    // 次のコピー先オフセット
                    destOffset += BYTES_PER_STRIP;
                }
                
                // --- 根本修正 (ここまで) ---

                // ESP32側は GRB 順を期待しているため，RとGを入れ替える (GRB -> RGB 変換)
                // for (int j = 0; j < numPerfLeds; j++)
                // {
                //     int idx = 2 + j * 3; // perfPacket 内のオフセット (ヘッダ2バイト分)
                //     byte val0 = perfPacket[idx + 0]; // R (または G)
                //     byte val1 = perfPacket[idx + 1]; // G (または R)
                //     perfPacket[idx + 0] = val1; // Rの位置に G を
                //     perfPacket[idx + 1] = val0; // Gの位置に R を
                //     // B (idx + 2) はそのまま
                // }
                // --- 送信先を変更 ---
                try
                {
                    sendClient.Send(perfPacket, perfPacket.Length, targetEndPoint);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error sending perf data to Device {deviceId} at {targetEndPoint}: {e.Message}");
                    deviceRegistered[deviceId] = false; // エラーが出たら登録を解除して再発見を促す
                }
            }

            // ノーツ用LEDデータを1パケットで送信
            notePacket[0] = (byte)deviceId;
            notePacket[1] = 3;
            for (int i = 0; i < NUM_NOTE_LEDS; i++)
            {
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
        }
        // 送信完了のログ
        // Debug.Log("Sent Data");
    }

    /// <summary>
    /// バックグラウンドスレッドでESP32からのデータを受信し続ける関数
    /// </summary>
    private void ReceiveData()
    {
        // IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0); // ★GC対策: 変更
        EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0); // ★GC対策: 変更

        while (true)
        {
            try
            {
                // --- ★GC対策: Socket.ReceiveFrom を使用 ---
                // byte[] data = receiveClient.Receive(ref anyIP); // ★GC対策: 削除
                
                // あらかじめ確保したバッファ(receiveBuffer)にデータを受信
                int receivedBytes = receiveSocket.ReceiveFrom(receiveBuffer, ref remoteEP);
                // --- ★GC対策ここまで ---

                // 発見パケット [255][ID] の処理を追加
                // ★GC対策: data.Length -> receivedBytes
                // ★GC対策: data[i] -> receiveBuffer[i]
                if (receivedBytes == 2 && receiveBuffer[0] == 255)
                {
                    int deviceId = receiveBuffer[1];
                    if (deviceId >= 0 && deviceId < NUM_DEVICES)
                    {
                        // 送信元IPアドレスを取得
                        IPAddress remoteAddress = ((IPEndPoint)remoteEP).Address;

                        // 新しいデバイス、またはIPアドレスが変わったデバイスを発見
                        if (!deviceRegistered[deviceId])
                        {
                            Debug.Log($"Device {deviceId} 発見！ IP: {remoteAddress}. ACKを送信します．");
                        }
                        else if (deviceEndPoints[deviceId] == null || !deviceEndPoints[deviceId].Address.Equals(remoteAddress))
                        {
                            Debug.Log($"Device {deviceId} IP更新！ IP: {remoteAddress}. ACKを送信します．");
                        }
                        else
                        {
                            // 頻繁にログが出すぎるためコメントアウト
                            // Debug.Log($"Device {deviceId} 再発見！ IP: {remoteAddress}. ACKを送信します．");
                        }
                        deviceEndPoints[deviceId] = new IPEndPoint(remoteAddress, espPort);
                        deviceRegistered[deviceId] = true;
                        
                        // メインスレッドにデバイス発見を通知
                        discoveryQueue.Enqueue(deviceId);
                        
                        // 確認応答(ACK) [254] をユニキャストで返信
                        byte[] ackPacket = { 254 };
                        sendClient.Send(ackPacket, ackPacket.Length, deviceEndPoints[deviceId]);
                    }
                }
                // パケットの長さが期待通りかチェック (ID 1バイト + Touch NUM_TOUCHバイト)
                else if (receivedBytes == NUM_TOUCH + 1)
                {
                    int deviceId = receiveBuffer[0];
                    if (deviceId >= 0 && deviceId < NUM_DEVICES)
                    {
                        for (int i = 0; i < NUM_TOUCH; i++)
                        {
                            // 受信した 1 or 0 を bool (true/false) に変換
                            bool newState = (receiveBuffer[i + 1] == 1); // ★GC対策: data[i+1] -> receiveBuffer[i+1]
                            
                            // 状態が ON になった瞬間だけをキューに入れる
                            if (newState == true && (touchStates[deviceId] == null || touchStates[deviceId][i] == false)) // 配列初期化中のエラー回避
                            {
                                // メインスレッドで処理するため、イベントをキューに追加
                                touchEventQueue.Enqueue((deviceId, i));
                            }
                            
                            // メインスレッドが参照する配列の状態を更新 (配列が初期化されていれば)
                            if (touchStates[deviceId] != null)
                            {
                                touchStates[deviceId][i] = newState; 
                            }
                        }

                        // タッチパケットを受信したこと(生存確認)をメインスレッドに通知
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

    /// <summary>
    /// ブロードキャストで明るさ設定パケットを送信します．
    /// パケット形式: [BROADCAST_ID=255][PACKET_TYPE_BRIGHTNESS=100][brightness_perf][brightness_notes]
    /// </summary>
    /// <param name="perf">演出用明るさ (0-255)</param>
    /// <param name="notes">ノーツ用明るさ (0-255)</param>
    public void SendBrightness(byte perf, byte notes)
    {
        try
        {
            if (sendClient == null)
            {
                Debug.LogWarning("SendBrightness: sendClient is null, skipping brightness send.");
                return;
            }

            // 定義に合わせる
            const byte BROADCAST_ID = 255;
            const byte PACKET_TYPE_BRIGHTNESS = 100;

            byte[] packet = new byte[4];
            packet[0] = BROADCAST_ID;
            packet[1] = PACKET_TYPE_BRIGHTNESS;
            packet[2] = perf;
            packet[3] = notes;

            IPEndPoint ep = sendEndPoint ?? new IPEndPoint(IPAddress.Parse(broadcastAddress), espPort);
            sendClient.Send(packet, packet.Length, ep);
            // Debug.Log($"Sent brightness packet: perf={perf}, notes={notes} to {ep}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error sending brightness packet: {e.Message}");
        }
    }
    
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

            // 2. 最終発見 (Last Discovery)
            float lastDiscovery = lastDiscoveryTime[i];
            if (lastDiscovery > 0f)
            {
                // 最終発見時刻を表示
                statusBuilder.Append($"Discovery: {lastDiscovery:F1}. ");
            }
            else
            {
                statusBuilder.Append("Discovery: N/A. ");
            }

            // 3. 最終タッチ通信 (Last Touch)
            float lastTouch = lastTouchTime[i];
            if (lastTouch > 0f)
            {
                // 最後にタッチパケットを受信してからの経過時間 (色分け判定用)
                float elapsed = currentTime - lastTouch;
                
                // 経過時間に応じて色分け (1s 以下: 緑, 1-2s: 黄, >2s: 赤)
                string colorTag;
                if (elapsed > 2.0f) colorTag = "<color=red>";
                else if (elapsed > 0.5f) colorTag = "<color=yellow>";
                else colorTag = "<color=green>";
                
                // 最終タッチ時刻を表示
                statusBuilder.Append($"LastTouch: {colorTag}{lastTouch:F1}</color>");
            }
            else
            {
                // まだ一度もタッチパケットを受信していない
                statusBuilder.Append("LastTouch: N/A");
            }

            // センサごとの最終タッチ時刻を表示
            statusBuilder.Append(" ");
            statusBuilder.Append("Sensors:");
            if (lastTouchTimePerSensor != null && lastTouchTimePerSensor.Length > i && lastTouchTimePerSensor[i] != null)
            {
                for (int s = 0; s < NUM_TOUCH; s++)
                {
                    float ts = lastTouchTimePerSensor[i][s];
                    if (ts > 0f)
                    {
                        // 表示は絶対時刻 (Time.time)、色付けは経過時間で判定
                        float elapsedSensor = currentTime - ts;
                        string sensorColor;
                        if (elapsedSensor > 2.0f) sensorColor = "<color=red>";
                        else if (elapsedSensor > 1.0f) sensorColor = "<color=yellow>";
                        else sensorColor = "<color=green>";
                        statusBuilder.Append($" S{s}:{sensorColor}{ts:F1}</color>");
                    }
                    else
                    {
                        statusBuilder.Append($" S{s}:N/A");
                    }
                }
            }

            statusBuilder.AppendLine(); // 次の行へ (改行)
        }
        
        // --- ノーツ状態の表示を追加 ---
        statusBuilder.AppendLine(); // 空行を挿入
        statusBuilder.AppendLine("--- Active Notes Status ---");
        
        var gameManager = GameManager.Instance;
        if (gameManager != null && gameManager.ActiveNotes != null)
        {
            int totalNotes = gameManager.ActiveNotes.Length;
            int activeCount = 0;
            int usedCount = 0;
            int touchCount = 0;
            int connectCount = 0;
            
            // ノーツの統計を集計
            foreach (var note in gameManager.ActiveNotes)
            {
                if (note != null)
                {
                    activeCount++;
                    if (note.IsUsed) usedCount++;
                    if (note.Data.type == "touch") touchCount++;
                    else if (note.Data.type == "connect") connectCount++;
                }
            }
            
            // サマリー表示
            statusBuilder.AppendLine($"Total: {totalNotes}, Active: {activeCount}, Used: {usedCount}");
            statusBuilder.AppendLine($"Touch: {touchCount}, Connect: {connectCount}");
            statusBuilder.AppendLine($"Spawned Index: {gameManager.LastSpawnedNoteIndex} / {totalNotes}");
            
            // 現在アクティブなノーツの詳細（最大10件まで表示）
            int displayCount = 0;
            const int maxDisplay = 10;
            statusBuilder.AppendLine("Recent Notes:");
            
            for (int i = 0; i < gameManager.ActiveNotes.Length && displayCount < maxDisplay; i++)
            {
                var note = gameManager.ActiveNotes[i];
                if (note != null && !note.IsUsed && note.NoteObject != null && note.NoteObject.activeSelf)
                {
                    string stateColor = note.IsUsed ? "<color=grey>" : "<color=cyan>";
                    string typeInfo = note.Data.type == "touch" ? "T" : "C";
                    statusBuilder.Append($"  {stateColor}[{i:D3}] {typeInfo} Lane:{note.Data.lane} Time:{note.Data.time:F2}</color>");
                    statusBuilder.AppendLine();
                    displayCount++;
                }
            }
            
            if (displayCount == 0)
            {
                statusBuilder.AppendLine("  (No active notes)");
            }
        }
        else
        {
            statusBuilder.AppendLine("GameManager not initialized");
        }
        // --- ノーツ状態の表示ここまで ---
        
        // 構築した文字列を StatusDisplay コンポーネントの public 変数に設定
        targetDisplay.statusText = statusBuilder.ToString();
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

            // ... (テストデータ設定) ...
            // (この部分は元のコードから省略されているため、そのままにしています)
        }
        Debug.Log("テスト用のLEDデータを初期化しました．");
    }
}

