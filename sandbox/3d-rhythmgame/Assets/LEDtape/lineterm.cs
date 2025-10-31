using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering; // ★追加
using Unity.Collections;     // ★追加

/// <summary>
/// 複数のnewlineスクリプトからバイトデータを集約し、管理するクラス
/// (★変更: 書き込み先を PerfLeds.perfLedData に変更)
/// </summary>
public class lineterm : MonoBehaviour
{
    // 各newlineスクリプトからのバイト配列を格納するためのジャグ配列 (byte[]の配列)
    // public byte[][] bytes; // ★修正: 単一のバイト配列で管理

    /// <summary>
    /// ★削除: 全LEDのデータを格納する単一のバイト配列
    /// (PerfLeds.perfLedData に書き込むため不要に)
    /// </summary>
    // public byte[] finalLedData;
    
    /// <summary>
    /// 書き込み対象の PerfLeds コンポーネント
    /// </summary>
    private PerfLeds targetPerfLeds;
    
    /// <summary>
    /// 全カメラの描画結果をまとめる単一のRenderTexture (90x120)
    /// </summary>
    [HideInInspector] // Inspectorには表示しない (Startで生成するため)
    public RenderTexture combinedRd;

    // LEDマトリクスの設定
    private int ledStrips = 90; // ストリップ数
    private int ledsPerStrip = 120; // 1ストリップあたりのLED数
    // 定数 (ProcessPixelData, GetBytes で使用)
    private const int BYTES_PER_STRIP = 360; // (120 * 3)
    private const int TOTAL_STRIPS = 90;


    // 初期化が完了し、データの受け入れ準備ができたことを示すフラグ
    public bool ready = false;

    //  GPU読み出しが進行中かを示すフラグ
    private bool readbackInProgress = false;

    // 読み出し頻度の設定 (10fps)
    private float updateInterval = 1.0f / 10.0f; // 10fps
    private float timeSinceLastUpdate = 0.0f;
    
    // ストリップ反転の要否をキャッシュする配列
    private bool[] reversedMap;

    // デバッグモード設定
    [Header("Debug Settings")]
    [Tooltip("デバッグ時にプレイヤーが見るメインカメラ")]
    [SerializeField] private Camera mainCamera;
    [Tooltip("チェックを外すと、起動時にメインカメラを無効化します")]
    [SerializeField] private bool isDebugMode = false;

    /// <summary>
    /// 初期化処理
    /// </summary>
    void Start()
    {
        // 90個のバイト配列を格納できる領域を確保 (★削除)
        // bytes = new byte[90][];

        // 書き込み対象の PerfLeds コンポーネントをシーンから探す
        targetPerfLeds = FindFirstObjectByType<PerfLeds>();
        if (targetPerfLeds == null)
        {
            Debug.LogError("PerfLeds コンポーネントが見つかりません．lineterm を無効化します．");
            enabled = false;
            return;
        }

        // 全カメラの描画をまとめるRenderTextureを生成
        // (スクリプト実行順序の設定により、newline.Start() より先に実行される)
        
        // ★修正: ワーニング解消のため、深度バッファ(depth)を 0 から 16 (または 24) に変更
        combinedRd = new RenderTexture(ledStrips, ledsPerStrip, 16);
        combinedRd.Create();
        
        // (RenderTextureSamplerが先に初期化している可能性もあるため)
        if (targetPerfLeds.perfLedData == null || targetPerfLeds.perfLedData.Length != TOTAL_STRIPS * BYTES_PER_STRIP)
        {
            targetPerfLeds.perfLedData = new byte[TOTAL_STRIPS * BYTES_PER_STRIP];
            Debug.Log($"lineterm が PerfLeds.perfLedData ({TOTAL_STRIPS * BYTES_PER_STRIP} bytes) を初期化しました．");
        }
        
        // 反転マップの事前計算
        reversedMap = new bool[ledStrips];
        for (int i = 0; i < ledStrips; i++)
        {
            // ProcessPixelData で使うロジック (ConvertID(x) % 2 != 0) をここで計算
            reversedMap[i] = (ConvertID(i) % 2 != 0);
        }

        // デバッグモードでないならメインカメラを無効化
        if (mainCamera != null)
        {
            mainCamera.gameObject.SetActive(isDebugMode);
            if (!isDebugMode)
            {
                Debug.Log("メインカメラを無効化しました。");
            }
        }

        // 初期化完了フラグを立てる
        ready = true;

        // テスト用の関数呼び出し (現在はコメントアウトされている)
        // Invoke("get1to4", 1f);
    }
    
    // newlineが初期化状態を確認するためのメソッド
    // (スクリプト実行順序を設定すれば不要だが、念のため)
    public bool IsReady()
    {
        return ready;
    }

    // フレームごとの更新処理 (読み出しリクエスト)
    private void Update()
    {
        // ★修正: 読み出し頻度を間引くタイマー
        timeSinceLastUpdate += Time.deltaTime;
        
        // 準備ができており、かつ読み出し中でなく、かつ指定時間が経過していたら
        if (ready && !readbackInProgress && timeSinceLastUpdate >= updateInterval)
        {
            // タイマーリセット
            // (経過時間からIntervalを引く方がズレは少ないが、簡潔さを優先)
            timeSinceLastUpdate = 0.0f; 
            
            readbackInProgress = true;
            
            // 共有RenderTextureに対して1回だけ読み出しリクエスト
            AsyncGPUReadback.Request(combinedRd, 0, OnReadbackComplete);
        }
        
        /*
        // デバッグ用のUpdate処理 (★元の処理はコメントアウト)
        // (デバッグログは OnReadbackComplete の最後や
        //  別のキー入力などで呼び出すことを推奨)
        */
    }

    /// <summary>
    /// GPUからのデータ読み出しが完了したときのコールバック
    /// </summary>
    void OnReadbackComplete(AsyncGPUReadbackRequest request)
    {
        // 次のフレームでリクエストできるようフラグを戻す
        readbackInProgress = false;

        if (request.hasError)
        {
            Debug.LogError("GPUデータの読み込みに失敗しました。");
            return;
        }

        // targetPerfLeds が見つからない場合は処理中断
        if (targetPerfLeds == null)
        {
            Debug.LogWarning("targetPerfLeds が null のため，OnReadbackComplete をスキップします．");
            return;
        }

        // データを NativeArray<Color32> として取得 (アロケーションなし)
        NativeArray<Color32> data = request.GetData<Color32>();

        // ピクセルデータを finalLedData (byte[]) に変換
        ProcessPixelData(data);
    }
    
    /// <summary>
    /// NativeArray<Color32> を finalLedData (byte[]) に変換する
    /// </summary>
    /// <param name="data">GPUから読み出した90x120のピクセルデータ</param>
    void ProcessPixelData(NativeArray<Color32> data)
    {
        // dataは (y * width + x) の順で格納されている (width = ledStrips = 90)
        byte[] ledData = targetPerfLeds.perfLedData;
        if (ledData == null) return; // 安全装置
        
        for (int x = 0; x < ledStrips; x++) // x = ストリップID (0-89)
        {
            // ★修正: ConvertID() の呼び出しを、事前計算した配列の参照に変更
            bool reversed = reversedMap[x];

            for (int y = 0; y < ledsPerStrip; y++) // y = ピクセルインデックス (0-119)
            {
                // (x, y) のピクセル色を取得
                // NativeArrayは (y * width + x) でアクセス
                Color32 color = data[y * ledStrips + x];

                // このピクセルを格納すべきインデックスを計算
                int pixelIndexInColumn;
                if (!reversed)
                {
                    // 通常順 (0, 1, 2...)
                    pixelIndexInColumn = y;
                }
                else
                {
                    // 反転順 (119, 118, 117...)
                    pixelIndexInColumn = (ledsPerStrip - 1) - y;
                }

                // finalLedData内の書き込み先インデックスを計算
                // (ストリップID * 1ストリップのバイト長) + (ピクセル位置 * 3)
                int baseByteIndex = (x * ledsPerStrip * 3) + (pixelIndexInColumn * 3);

                ledData[baseByteIndex + 0] = color.r;
                ledData[baseByteIndex + 1] = color.g;
                ledData[baseByteIndex + 2] = color.b;
            }
        }
        
        // (デバッグ用)
        // if (finalLedData.Length > 0) Debug.Log("R値[0]: " + finalLedData[0]);
        // if (ledData.Length > 0) Debug.Log("R値[0]: " + ledData[0]);
    }

    /// <summary>
    /// テスト用の関数
    /// </summary>
    void get1to4()
    {
        // IDが0から3までのバイトデータを結合するテストを実行
        GetBytes(0, 3);
    }

    /// <summary>
    /// 指定された範囲(beginからendまで)のIDのバイトデータを結合して1つのバイト配列として返す
    /// </summary>
    /// <param name="begin">結合を開始するID</param>
    /// <param name="end">結合を終了するID</param>
    /// <returns>結合されたバイト配列</returns>
    public byte[] GetBytes(int begin, int end)
    {
        // ★修正: ArrayList (非効率) を使わず、Array.Copyで finalLedData から直接コピー

        // 範囲チェック
        int beginStrip = Mathf.Clamp(begin, 0, ledStrips - 1);
        int endStrip = Mathf.Clamp(end, beginStrip, ledStrips - 1);

        int numStrips = (endStrip - beginStrip) + 1;
        int stripLengthInBytes = ledsPerStrip * 3;
        int totalBytesToCopy = numStrips * stripLengthInBytes;
        
        if (targetPerfLeds == null || targetPerfLeds.perfLedData == null || targetPerfLeds.perfLedData.Length < (endStrip + 1) * stripLengthInBytes)
        {
            // ★修正: 準備できていない場合のログをWarningからLogに変更 (頻繁に出る可能性があるため)
            // Debug.LogWarning("finalLedData がまだ準備できていません。");
            return new byte[totalBytesToCopy]; // ★修正: 空配列ではなく、ゼロ埋めされた配列を返す（エラー防止）
        }

        // 1. 最終的なサイズの配列を1つだけ確保する
        byte[] result = new byte[totalBytesToCopy];
        
        // 2. コピー元のオフセット
        int sourceOffset = beginStrip * stripLengthInBytes;
        
        // 3. 高速なブロックコピー
        Array.Copy(targetPerfLeds.perfLedData, sourceOffset, result, 0, totalBytesToCopy);

        // デバッグ用: 結合後のバイト配列の長さをログに出力
        // Debug.Log("結合後のバイト配列の長さ: " + result.Length);
        
        return result;

        /* (↓ 元のArrayListロジックは削除 ↓)
        ArrayList list = new ArrayList(bytes[begin]);
        ...
        return bt;
        */
    }

    private int ConvertID(int i)
    {
        int n = i % 30 * 3;
        if ((int)i / 30 == 0) n += 0;
        else if ((int)i / 30 == 1) n += 2;
        else if ((int)i / 30 == 2) n += 1;
        return n;
    }

    /// <summary>
    /// 指定された範囲(beginからendまで)のIDのバイトデータを結合して1つのバイト配列として返す
    /// </summary>
    /// <param name="begin">結合を開始するID</param>
    /// <param name="end">結合を終了するID</param>
    /// <returns>結合されたバイト配列</returns>
    public byte[] GetBytes2(int begin, int end)
    {
        return GetBytes(ConvertID(begin), ConvertID(end));
    }

    /*
    // デバッグ用のUpdate処理 (現在はコメントアウトされている)
    private void Update() // ★注意: Updateは既に追加済み (読み出しリクエスト用)
    {
        // (デバッグロジックは OnReadbackComplete の最後か、
        //  別のデバッグ用関数として呼び出すことを推奨)
        
        //Debug.Log(bytes[0][0]);
        // すべてのIDの最初のR値をログ1行で出力
        string log = "";
        for (int i = 0; i < 90; i++)
        {
            // int n = i % 30 * 3;
            // ... (元のID計算)
            int n = ConvertID(i); // ★修正: 元のロジックでの 'n' に合わせる

            // ★修正: finalLedData から読み出す
            if (targetPerfLeds != null && targetPerfLeds.perfLedData != null && targetPerfLeds.perfLedData.Length > (n * ledsPerStrip * 3))
            {
                // (nは 0..89 の範囲外になる可能性があるため、チェックが必要かもしれない)
                // (ConvertIDのロジックだと最大 29*3+2 = 89 なので大丈夫そう)
                log += targetPerfLeds.perfLedData[n * ledsPerStrip * 3] + " "; // ★変更
            }
            else
            {
                log += "null ";
            }
            if (i % 10 == 9)
            {
                log += ",";
            }
        }
        Debug.Log(log);
    }
    //*/
}

