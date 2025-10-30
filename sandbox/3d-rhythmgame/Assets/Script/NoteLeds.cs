using UnityEngine;
using System.Collections; // ★追加: コルーチンのために必要

public class NoteLeds : MonoBehaviour
{
    private const int NUM_MUGU_LEDS = 64;
    private const int NUM_CON_LEDS = 32;
    private const int NUM_MUGU = 40;
    private const int NUM_CON = 40;
    private const int NUM_DEVICES = 8;
    private const int NUM_TOUTCH = 5; // (※NUM_TOUCH の typo)
    
    // ★追加: 譜面レーン数 (0-16 と仮定)
    private const int NUM_LANES = 17; 

    private Color32[,] ledColors;

    // --- ★ここから追加 (デバッグモード用) ---
    private bool isDebugMode = false;
    private Color32 debugLoopColor = Color.green;  // ループで光る色
    private Color32 debugTouchColor = Color.red;   // タッチで光る色
    private Color32 debugOffColor = Color.black;   // 消灯
    // private Color32 debugAllOnColor = Color.white; // 全点灯の色 (現在未使用)

    // 各レーン(0-16)のタッチ状態
    private bool[] debugTouchStates = new bool[NUM_LANES];
    // 現在ループで光っているレーン(0-16)
    private int currentDebugLoopLane = -1;
    
    // --- ★ここから追加 (GC対策) ---
    // WaitForSecondsをキャッシュして、newの回数を減らす
    private WaitForSeconds waitHalfSecond = new WaitForSeconds(0.5f);
    private WaitForSeconds waitOneSecond = new WaitForSeconds(1.0f);
    private WaitForSeconds waitPointOneSecond = new WaitForSeconds(0.1f);
    // --- ★追加ここまで ---


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Initialize the ledColors array
        ledColors = new Color32[NUM_DEVICES, (NUM_MUGU_LEDS + NUM_CON_LEDS) * 5];
        
        // ★追加: デバッグ用配列の初期化
        for (int i = 0; i < NUM_LANES; i++)
        {
            debugTouchStates[i] = false;
        }
    }

    // --- ★ここから追加 (デバッグモード用) ---
    /// <summary>
    /// 毎フレーム、デバッグモードのLED色を更新する
    /// </summary>
    private void Update()
    {
        // デバッグモードでなければ何もしない
        if (!isDebugMode) return;

        // 0-16 の全レーンをチェック
        for (int lane = 0; lane < NUM_LANES; lane++)
        {
            Color32 targetColor;
            
            // 1. タッチ状態を最優先
            if (debugTouchStates[lane])
            {
                targetColor = debugTouchColor;
            }
            // 2. ループ点灯
            else if (currentDebugLoopLane == lane)
            {
                targetColor = debugLoopColor;
            }
            // 3. 消灯
            else
            {
                targetColor = debugOffColor;
            }
            
            // レーンID (0-16) を 物理MUGU ID (0-39) に変換
            int muguId = ConvertNoteIdToHard(lane);
            
            if (muguId != -1) // -1 は無効なレーン
            {
                 SetAllMuguColors(muguId, targetColor);
            }
        }
    }

    /// <summary>
    /// GameManagerからデバッグモードの開始を指示される
    /// </summary>
    public void StartDebugMode()
    {
        isDebugMode = true;
        // デバッグループを開始
        StartCoroutine(DebugLEDLoop());
    }

    /// <summary>
    /// GameManagerからタッチ状態を受け取る
    /// </summary>
    public void SetDebugTouchState(int lane, bool isTouching)
    {
        if (lane >= 0 && lane < NUM_LANES)
        {
            debugTouchStates[lane] = isTouching;
        }
    }

    /// <summary>
    /// デバッグ用のLED点灯ループ
    /// </summary>
    private IEnumerator DebugLEDLoop()
    {
        Debug.Log("--- デバッグモード開始 ---");

        // 1. まず全レーンを点灯
        currentDebugLoopLane = -1; // ループ点灯はなし
        // 全レーンをタッチ扱いで点灯させる
        for(int i=0; i<NUM_LANES; i++) { debugTouchStates[i] = true; }
        // Update() が呼ばれて色が反映されるのを待つ
        yield return waitPointOneSecond; // ★GC対策版
        // 1秒間待機
        yield return waitOneSecond; // ★GC対策版
        // 全レーンのタッチ状態をリセット
        for(int i=0; i<NUM_LANES; i++) { debugTouchStates[i] = false; }
        
        Debug.Log("デバッグ: 全点灯 終了");

        // 2. ループ点灯開始
        while (isDebugMode)
        {
            // 0 から 16 まで順番に
            for (int lane = 0; lane < NUM_LANES; lane++)
            {
                if (!isDebugMode) yield break; // モードが終了したら抜ける
                
                currentDebugLoopLane = lane;
                // Debug.Log($"デバッグ: レーン {lane} 点灯");
                
                // 0.5秒待機
                yield return waitHalfSecond; // ★GC対策版
                
                // タッチされていなければ消灯 (Updateで処理される)
                currentDebugLoopLane = -1;
            }
            
            // 1周したら少し待つ
            yield return waitOneSecond; // ★GC対策版
        }
        
        Debug.Log("--- デバッグモード 終了 ---");
    }
    // --- ★デバッグモード用コード ここまで ---


    void SetMuguColor(int muguId, int raw, int cow, Color32 color)
    {
        int device_id = (int)muguId / NUM_TOUTCH;
        int index = (NUM_MUGU_LEDS + NUM_CON_LEDS) * (muguId % NUM_TOUTCH) + raw * 8 + cow;
        ledColors[device_id, index] = color;
    }

    // すべてのMUGUの色を設定する
    public void SetAllMuguColors(int muguId, Color32 color)
    {
        // ★追加: 無効なID(-1)が来たら何もしない
        if (muguId < 0 || muguId >= NUM_MUGU)
        {
            // Debug.LogWarning($"SetAllMuguColors: 無効な muguId {muguId} が指定されました。");
            return;
        }

        // デバッグ用
        //Debug.Log("muguId" + muguId);
        int device_id = (int)muguId / NUM_TOUTCH;
        //Debug.Log("device_id" + device_id);
        for (int i = 0; i < NUM_MUGU_LEDS; i++)
        {
            int index = (NUM_MUGU_LEDS + NUM_CON_LEDS) * (muguId % NUM_TOUTCH) + i;
            //Debug.Log("index" + index);
           ledColors[device_id, index] = color;
        }
    }

    void SetConColor(int conId, int raw, Color32 color)
    {
        int device_id = (int)conId / NUM_TOUTCH;
        int index = (NUM_MUGU_LEDS + NUM_CON_LEDS) * (conId % NUM_TOUTCH) + NUM_MUGU_LEDS + raw;
        // LEDのマイコンが違うので、ここで反転させる
        Color32 reverseColor = new Color32(color.g, color.r, color.b, color.a);
        ledColors[device_id, index] = reverseColor;
    }

    public Color32[,] GetLedColors()
    {
        return ledColors;
    }

    public Color GetLedColor(int deviceId, int ledIndex)
    {
        // ★ Bounds Check 追加
        if (deviceId < 0 || deviceId >= ledColors.GetLength(0) || ledIndex < 0 || ledIndex >= ledColors.GetLength(1))
        {
            // Debug.LogError($"GetLedColor: Index out of bounds. DeviceId: {deviceId}, LedIndex: {ledIndex}");
            return Color.black; // 範囲外の場合は黒を返す
        }
        return ledColors[deviceId, ledIndex];
    }

    /**
     * 譜面上のLEDのID(レーンID 0-16)を、物理的なLEDのインデックス(MUGU ID 0-39)に変換する
     * @param: id (譜面レーンID 0-16)
     * @返り値: 0～39（/5でデバイスID、%5で内部ID）
     */
    public int ConvertNoteIdToHard(int id) {
        // ノーツIDからLEDインデックスへの変換ロジックを実装
        // (GameManager.cs の laneCooldowns[11] と矛盾するが、元のコードを尊重)
        // ★ 5<=id<=6 の範囲を修正 (25->30)
        if (0 <= id && id <= 4) {
            return id + 20; // 20-24
        } else if (5 <= id && id <= 6) {
            return id + 25; // 30-31 (元のコード +30 は 35,36 になり範囲外)
        } else if (7 <= id && id <= 11) {
            return id - 7; // 0-4
        } else if (12 <= id && id <= 16) {
            return id + 3; // 15-19
        } else {
            return -1; // 無効なIDの場合 (-1 を返すように修正)
        }
    }

    /**
     * 物理的なLEDのインデックス(ハードID 0-39)を譜面のID(レーンID 0-16)に変換する
     */
    public int ConvertNoteIdToGame(int id) {
        // ★修正: ConvertNoteIdToHard の逆変換を実装
        if (20 <= id && id <= 24) {
            return id - 20; // 0-4
        } else if (30 <= id && id <= 31) {
            return id - 25; // 5-6
        } else if (0 <= id && id <= 4) {
            return id + 7; // 7-11
        } else if (15 <= id && id <= 19) {
            return id - 3; // 12-16
        }
        
        // 上記以外のハードID (5-14, 25-29, 32-39) はどのレーンにもマッピングされていない
        return -1; // 無効なIDの場合
    }
}

