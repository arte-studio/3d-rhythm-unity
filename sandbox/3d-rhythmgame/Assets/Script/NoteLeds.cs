using UnityEngine;
using System.Collections; // ★追加: コルーチンのために必要

public class NoteLeds : MonoBehaviour
{
    private const int NUM_MUGU_LEDS = 64;
    private const int NUM_CON_LEDS = 32;
    private const int NUM_MUGU = 40;
    private const int NUM_CON = 40;
    private const int NUM_DEVICES = 8;
    // private const int NUM_TOUTCH = 5; // (※NUM_TOUCH の typo)
    public const int NUM_TOUTCH = 5; // (※NUM_TOUCH の typo) - public にして HardId で参照可能に
    
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

        // /*
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

            // レーンID (0-16) を 物理MUGU ID (HardId) に変換
            SetAllMuguColors(lane, targetColor);
        }
        //*/

        /*
        // ハードのIDでループ
        for (int deviceId = 0; deviceId < NUM_DEVICES; deviceId++)
        {
            for (int innerId = 0; innerId < NUM_TOUTCH; innerId++)
            {
                HardId hid = new HardId(deviceId, innerId);

                Color32 targetColor;

                // 1. タッチ状態を最優先
                if (lane >= 0 && lane < NUM_LANES && debugTouchStates[lane])
                {
                    targetColor = debugTouchColor;
                }
                // 2. ループ点灯
                else if (lane == currentDebugLoopLane)
                {
                    targetColor = debugLoopColor;
                }
                // 3. 消灯
                else
                {
                    targetColor = debugOffColor;
                }

                // 色を設定
                SetAllMuguColors(hid, targetColor);
            }
        }
        //*/
    }

    /// <summary>
    /// GameManagerからデバッグモードの開始を指示される
    /// </summary>
    public void StartDebugMode()
    {
        isDebugMode = true;

        // 変換が正しいかをチェック
        for (int lane = 0; lane < NUM_LANES; lane++)
        {
            HardId muguId = ConvertNoteIdToHard(lane);
            int backLane = ConvertNoteIdToGame(muguId);
            Debug.Log($"DebugMode: Lane {lane} -> MuguID device:{muguId.deviceId} inner:{muguId.innerId} -> BackLane {backLane}");
            if (lane != backLane)
            {
                Debug.LogError($"変換エラー: Lane {lane} が MuguID device:{muguId.deviceId} inner:{muguId.innerId} を経由して BackLane {backLane} に変換されました！");
            }
        }

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

    // --- ★ HardId 構造体サポート ---
    /// <summary>
    /// 物理ハードIDを deviceId + innerId の組として表現する構造体
    /// </summary>
    public struct HardId
    {
        public int deviceId; // 0～7
        public int innerId;  // 0～4

        public HardId(int deviceId, int innerId)
        {
            this.deviceId = deviceId;
            this.innerId = innerId;
        }

        public int ToInt()
        {
            return deviceId * NUM_TOUTCH + innerId;
        }

        public static HardId FromInt(int val)
        {
            if (val < 0) return new HardId(-1, -1);
            return new HardId(val / NUM_TOUTCH, val % NUM_TOUTCH);
        }
    }



    void SetMuguColor(int muguId, int raw, int cow, Color32 color)
    {
        int device_id = (int)muguId / NUM_TOUTCH;
        int index = (NUM_MUGU_LEDS + NUM_CON_LEDS) * (muguId % NUM_TOUTCH) + raw * 8 + cow;
        ledColors[device_id, index] = color;
    }

    /// <summary>
    /// すべてのMUGUの色を設定する
    /// </summary>
    /// <param name="muguId">MUGUのID（譜面上のID）</param>
    /// <param name="color">設定する色</param>
    public void SetAllMuguColors(int muguId, Color32 color)
    {
        // 無効なID(-1)が来たら何もしない
        if (muguId < 0 || muguId >= NUM_MUGU)
        {
            // Debug.LogWarning($"SetAllMuguColors: 無効な muguId {muguId} が指定されました。");
            return;
        }

        HardId hid = ConvertNoteIdToHard(muguId);
        if (hid.deviceId < 0 || hid.innerId < 0)
        {
            // Debug.LogWarning($"SetAllMuguColors: muguId {muguId} の変換結果が無効です。");
            return;
        }

        //Debug.Log("muguId" + muguId);
        //Debug.Log("device_id" + device_id);
        for (int i = 0; i < NUM_MUGU_LEDS; i++)
        {
            int index = (NUM_MUGU_LEDS + NUM_CON_LEDS - 2) * hid.innerId + i;
            //Debug.Log("index" + index);
            ledColors[hid.deviceId, index] = color;
        }
    }

    /// 
    public void SetAllMuguColors(HardId hid, Color32 color)
    {
        for (int i = 0; i < NUM_MUGU_LEDS; i++)
        {
            int index = (NUM_MUGU_LEDS + NUM_CON_LEDS - 2) * hid.innerId + i;
            //Debug.Log("index" + index);
            ledColors[hid.deviceId, index] = color;
        }
    }

    /// <summary>
    /// 接続ノーツの色を設定する
    /// </summary>
    /// <param name="conId">接続ノーツのID（譜面上のID）</param>
    /// <param name="raw">接続ノーツ内の行番号</param>
    public void SetConColor(int conId, int raw, Color32 color)
    {
        HardId hid = ConvertNoteIdToHard(conId);
        int index = (NUM_MUGU_LEDS + NUM_CON_LEDS - 2) * hid.innerId + NUM_MUGU_LEDS + raw;
        // LEDのマイコンが違うので、ここで反転させる
        Color32 reverseColor = new Color32(color.g, color.r, color.b, color.a);
        ledColors[hid.deviceId, index] = reverseColor;
    }

    /// <summary>
    /// 接続ノーツのすべての色を設定する
    /// </summary>
    /// <param name="conId">接続ノーツのID（譜面上のID）</param>
    /// <param name="color">設定する色</param>
    public void SetAllConColors(int conId, Color32 color)
    {
        HardId hid = ConvertNoteIdToHard(conId);
        for (int i = 0; i < NUM_CON_LEDS; i++)
        {
            int index = (NUM_MUGU_LEDS + NUM_CON_LEDS - 2) * hid.innerId + NUM_MUGU_LEDS + i;
            // LEDのマイコンが違うので、ここで反転させる
            Color32 reverseColor = new Color32(color.g, color.r, color.b, color.a);
            ledColors[hid.deviceId, index] = reverseColor;
        }
    }

    /// 
    // public void void SetAllConColors(HardId conId, Color32 color)

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
    // 既存の int 版は残す（互換性のため）
    // ConvertNoteIdToHard を HardId を返すように変更しました。
    public HardId ConvertNoteIdToHard(int id) {
        // ノーツIDからLEDインデックスへの変換ロジックを実装
        // (GameManager.cs の laneCooldowns[11] と矛盾するが、元のコードを尊重)
        // ★ 5<=id<=6 の範囲を修正 (25->30)
        HardId muguId = new HardId();
        // if (0 <= id && id <= 4) {
        //     muguId = new HardId(4, id); // 20-24
        // } else if (5 <= id && id <= 6) {
        //     muguId = new HardId(7, id - 5); // 30-31 (元のコード +30 は 35,36 になり範囲外)
        // } else if (7 <= id && id <= 11) {
        //     muguId = new HardId(0, id - 7); // 0-4
        // } else if (12 <= id && id <= 16) {
        //     muguId = new HardId(3, id - 12); // 15-19
        // } else {
        //     return HardId.FromInt(-1); // 無効なIDの場合
        // }
        switch (id)
        {
            case 1:
                muguId = new HardId(5, 0);
                break;
            case 2:
                muguId = new HardId(4, 0);
                break;
            case 3:
                muguId = new HardId(6, 0);
                break;
            case 5:
                muguId = new HardId(7, 0);
                break;
            case 7:
                muguId = new HardId(7, 1);
                break;
            case 9:
                muguId = new HardId(0, 0);
                break;
            case 11:
                muguId = new HardId(3, 0);
                break;
            case 13:
                muguId = new HardId(3, 1);
                break;
            case 15:
                muguId = new HardId(3, 2);
                break;
            default:
                return HardId.FromInt(-1); // 無効なIDの場合
        }

        return muguId;
    }

    // 互換用: int を返すラッパー。名前を変えて二重定義の衝突を避ける。
    public int ConvertNoteIdToHardInt(int id)
    {
        HardId hid = ConvertNoteIdToHard(id);
        return hid.ToInt();
    }

    /**
     * 物理的なLEDのインデックス(ハードID 0-39)を譜面のID(レーンID 0-16)に変換する
     */
    public int ConvertNoteIdToGame(HardId hid) {
        if (hid.deviceId < 0 || hid.innerId < 0) return -1;
        // switch (hid.deviceId) {
        //     case 0:
        //         if (0 <= hid.innerId && hid.innerId <= 4) {
        //             return hid.innerId + 7; // 0-4 -> 7-11
        //         }
        //         break;
        //     case 3:
        //         if (0 <= hid.innerId && hid.innerId <= 4) {
        //             return hid.innerId + 12; // 15-19 -> 12-16
        //         }
        //         break;
        //     case 4:
        //         if (0 <= hid.innerId && hid.innerId <= 4) {
        //             return hid.innerId; // 20-24 -> 0-4
        //         }
        //         break;
        //     case 7:
        //         if (0 <= hid.innerId && hid.innerId <= 1) {
        //             return hid.innerId + 5; // 30-31 -> 5-6
        //         }
        //         break;
        // }
        switch (hid.deviceId)
        {
            case 4:
                if (hid.innerId == 0) return 2;
                break;
            case 5:
                if (hid.innerId == 0) return 1;
                break;
            case 6:
                if (hid.innerId == 0) return 3;
                break;
            case 7:
                if (hid.innerId == 0) return 5;
                if (hid.innerId == 1) return 7;
                break;
            case 0:
                if (hid.innerId == 0) return 9;
                break;
            case 3:
                if (hid.innerId == 0) return 11;
                if (hid.innerId == 1) return 13;
                if (hid.innerId == 2) return 15;
                break;
            default:
                break;
        }
        
        // 上記以外のハードID (5-14, 25-29, 32-39) はどのレーンにもマッピングされていない
        return -1; // 無効なIDの場合
    }

    /// <summary>
    /// HardId から譜面レーンIDに変換するオーバーロード
    /// </summary>
    public int ConvertNoteIdToGame(int hid)
    {
        return ConvertNoteIdToGame(HardId.FromInt(hid));
    }
}

