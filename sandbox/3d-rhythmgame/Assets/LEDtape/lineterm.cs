using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering; // ★追加
using Unity.Collections;     // ★追加

/// <summary>
/// 複数のnewlineスクリプトからバイトデータを集約し、管理するクラス
/// </summary>
public class lineterm : MonoBehaviour
{
    // 各newlineスクリプトからのバイト配列を格納するためのジャグ配列 (byte[]の配列)
    // public byte[][] bytes; // ★修正: 単一のバイト配列で管理

    /// <summary>
    /// ★追加: 全LEDのデータを格納する単一のバイト配列
    /// [strip 0 (120*3 bytes)][strip 1 (120*3 bytes)]...
    /// </summary>
    public byte[] finalLedData;
    
    /// <summary>
    /// ★追加: 全カメラの描画結果をまとめる単一のRenderTexture (90x120)
    /// </summary>
    [HideInInspector] // Inspectorには表示しない (Startで生成するため)
    public RenderTexture combinedRd;

    // ★追加: LEDマトリクスの設定
    private int ledStrips = 90;
    private int ledsPerStrip = 120;

    // 初期化が完了し、データの受け入れ準備ができたことを示すフラグ
    public bool ready = false;

    // ★追加: GPU読み出しが進行中かを示すフラグ
    private bool readbackInProgress = false;

    /// <summary>
    /// 初期化処理
    /// </summary>
    void Start()
    {
        // 90個のバイト配列を格納できる領域を確保 (★削除)
        // bytes = new byte[90][];

        // ★追加: 全カメラの描画をまとめるRenderTextureを生成
        // (スクリプト実行順序の設定により、newline.Start() より先に実行される)
        combinedRd = new RenderTexture(ledStrips, ledsPerStrip, 16);
        combinedRd.Create();
        
        // ★追加: 全LEDデータを格納する単一のバイト配列を確保
        finalLedData = new byte[ledStrips * ledsPerStrip * 3];

        // 初期化完了フラグを立てる
        ready = true;

        // テスト用の関数呼び出し (現在はコメントアウトされている)
        // Invoke("get1to4", 1f);
    }
    
    // ★追加: newlineが初期化状態を確認するためのメソッド
    // (スクリプト実行順序を設定すれば不要だが、念のため)
    public bool IsReady()
    {
        return ready;
    }

    // ★追加: フレームごとの更新処理 (読み出しリクエスト)
    private void Update()
    {
        // 準備ができており、かつ読み出し中でなければ
        if (ready && !readbackInProgress)
        {
            readbackInProgress = true;
            
            // ★追加: 共有RenderTextureに対して1回だけ読み出しリクエスト
            AsyncGPUReadback.Request(combinedRd, 0, OnReadbackComplete);
        }
        
        /*
        // デバッグ用のUpdate処理 (★元の処理はコメントアウト)
        // (デバッグログは OnReadbackComplete の最後や
        //  別のキー入力などで呼び出すことを推奨)
        */
    }

    /// <summary>
    /// ★追加: GPUからのデータ読み出しが完了したときのコールバック
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

        // データを NativeArray<Color32> として取得 (アロケーションなし)
        NativeArray<Color32> data = request.GetData<Color32>();

        // ピクセルデータを finalLedData (byte[]) に変換
        ProcessPixelData(data);
    }
    
    /// <summary>
    /// ★追加: NativeArray<Color32> を finalLedData (byte[]) に変換する
    /// </summary>
    /// <param name="data">GPUから読み出した90x120のピクセルデータ</param>
    void ProcessPixelData(NativeArray<Color32> data)
    {
        // dataは (y * width + x) の順で格納されている (width = ledStrips = 90)
        
        for (int x = 0; x < ledStrips; x++) // x = ストリップID (0-89)
        {
            // newline.cs にあった反転ロジックをここで実行
            bool reversed = (ConvertID(x) % 2 != 0);

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

                finalLedData[baseByteIndex + 0] = color.r;
                finalLedData[baseByteIndex + 1] = color.g;
                finalLedData[baseByteIndex + 2] = color.b;
            }
        }
        
        // (デバッグ用)
        // if (finalLedData.Length > 0) Debug.Log("R値[0]: " + finalLedData[0]);
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
        
        if (finalLedData == null || finalLedData.Length < (endStrip + 1) * stripLengthInBytes)
        {
            Debug.LogWarning("finalLedData がまだ準備できていません。");
            return new byte[0];
        }

        // 1. 最終的なサイズの配列を1つだけ確保する
        byte[] result = new byte[totalBytesToCopy];
        
        // 2. コピー元のオフセット
        int sourceOffset = beginStrip * stripLengthInBytes;
        
        // 3. 高速なブロックコピー
        Array.Copy(finalLedData, sourceOffset, result, 0, totalBytesToCopy);

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
            if (finalLedData != null && finalLedData.Length > (n * ledsPerStrip * 3))
            {
                // (nは 0..89 の範囲外になる可能性があるため、チェックが必要かもしれない)
                // (ConvertIDのロジックだと最大 29*3+2 = 89 なので大丈夫そう)
                log += finalLedData[n * ledsPerStrip * 3] + " ";
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
