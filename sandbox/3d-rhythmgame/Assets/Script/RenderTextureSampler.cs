using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using System; // ★エラー修正: Exception のために追加
using System.Threading.Tasks; // Task (async/await) のために必要

/// <summary>
/// RenderTexture の内容をサンプリングし，PerfLeds コンポーネントの perfLedData (byte配列) に書き込む．
/// (lineterm.cs とは独立した，もう一つのサンプリング方法)
/// </summary>
[RequireComponent(typeof(PerfLeds))]
public class RenderTextureSampler : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("サンプリング対象のRenderTexture (90x120)")]
    public RenderTexture sourceTexture;

    [Header("Sampling Settings")]
    [Tooltip("読み出しのターゲットFPS (GPU Readbackの頻度)")]
    [Range(1, 60)]
    public int targetReadbackFPS = 30;

    // [Tooltip("ストリップID (X座標) を物理IDに変換するか (UdpControllerのロジックに合わせるか)")]
    // public bool convertStripId = true; // ★削除: このスクリプトではID変換しない
    
    [Tooltip("奇数番目の(論理)ストリップのピクセル (Y座標) を反転させるか")] // ★コメント変更
    public bool reverseOddStrips = true;

    // --- 内部参照 ---
    private PerfLeds targetPerfLeds; // 書き込み先のデータコンポーネント
    private float readbackInterval;
    private float timeSinceLastReadback = 0f;
    private bool readbackInProgress = false;

    // --- LED設定 (UdpController/lineterm と合わせる) ---
    private const int NUM_STRIPS = 90;
    private const int LEDS_PER_STRIP = 120;
    private const int BYTES_PER_STRIP = 360; // (120 * 3)
    private const int TOTAL_STRIPS = 90;
    private const int TOTAL_BYTES = TOTAL_STRIPS * BYTES_PER_STRIP; // 32400

    // --- 物理ID変換マップ (キャッシュ用) ---
    // mappingCache[論理X座標] = 物理ストリップID
    private int[] logicalToPhysicalMap;
    // reverseCache[物理ストリップID] = 反転するか
    private bool[] physicalReverseMap;

    void Start()
    {
        // 必須コンポーネントを取得
        targetPerfLeds = GetComponent<PerfLeds>();

        if (sourceTexture == null)
        {
            Debug.LogError("Source Texture が設定されていません．RenderTextureSampler を無効化します．", this);
            enabled = false;
            return;
        }

        // ★追加: RenderTextureが作成されているか確認
        if (!sourceTexture.IsCreated())
        {
            Debug.LogWarning("sourceTexture が .IsCreated() == false です．Create() を試みます．");
            sourceTexture.Create();
            if (!sourceTexture.IsCreated())
            {
                Debug.LogError("sourceTexture.Create() に失敗しました．RenderTextureSampler を無効化します．");
                enabled = false;
                return;
            }
        }

        // ★追加: RenderTextureの詳細情報をログに出力
        Debug.Log($"[RenderTextureSampler] sourceTexture の詳細情報:\n" +
                  $"  Format: {sourceTexture.format}\n" +
                  $"  Depth: {sourceTexture.depth}\n" +
                  $"  AntiAliasing: {sourceTexture.antiAliasing}\n" +
                  $"  enableRandomWrite: {sourceTexture.enableRandomWrite}\n" +
                  $"  dimension: {sourceTexture.dimension}", this);


        if (sourceTexture.width != NUM_STRIPS || sourceTexture.height != LEDS_PER_STRIP)
        {
            Debug.LogWarning($"Source Textureの解像度が期待値 ({NUM_STRIPS}x{LEDS_PER_STRIP}) と異なります．" +
                             $"現在の解像度: ({sourceTexture.width}x{sourceTexture.height})", this);
        }

        // targetPerfLeds のデータ配列を初期化 (linetermと競合する可能性があるためチェック)
        if (targetPerfLeds.perfLedData == null || targetPerfLeds.perfLedData.Length != TOTAL_BYTES)
        {
            targetPerfLeds.perfLedData = new byte[TOTAL_BYTES];
            Debug.Log($"RenderTextureSampler が PerfLeds.perfLedData ({TOTAL_BYTES} bytes) を初期化しました．");
        }
        else
        {
             Debug.Log("PerfLeds.perfLedData は既に初期化されています．(おそらく lineterm によって)");
        }

        readbackInterval = 1.0f / targetReadbackFPS;
        
        // マッピングと反転のキャッシュを事前計算
        BuildConversionCache();
    }

    /// <summary>
    /// ID変換と反転のルックアップテーブルを構築する
    /// </summary>
    void BuildConversionCache()
    {
        logicalToPhysicalMap = new int[NUM_STRIPS];
        physicalReverseMap = new bool[NUM_STRIPS];

        for (int logicalX = 0; logicalX < NUM_STRIPS; logicalX++)
        {
            // ★変更: ID変換ロジックを削除．logicalX をそのまま physicalId として扱う
            int physicalId = logicalX; 
            
            // ★削除: logicalX は常に 0-89 のため範囲チェック不要
            // if (physicalId < 0 || physicalId >= NUM_STRIPS)
            // {
            // ...
            // }

            logicalToPhysicalMap[logicalX] = physicalId;

            if (reverseOddStrips)
            {
                // ★変更: lineterm/UdpController とは異なり，ここでは *論理ID (physicalId=logicalX)* の奇数判定
                physicalReverseMap[physicalId] = (physicalId % 2 != 0);
            }
            else
            {
                physicalReverseMap[physicalId] = false;
            }
        }
    }

    /// <summary>
    /// ★削除: lineterm/UdpController と同じID変換ロジック
    /// </summary>
    // private int ConvertPerfLedsID(int i)
    // {
    //     if (i < 0 || i >= 90) return -1;
    //     int n = i % 30 * 3;
    //     if ((int)i / 30 == 0) n += 0;
    //     else if ((int)i / 30 == 1) n += 2;
    //     else if ((int)i / 30 == 2) n += 1;
    //     return n;
    // }

    void Update()
    {
        timeSinceLastReadback += Time.deltaTime;

        // 読み出し中でなく，かつ指定時間が経過していたら
        if (!readbackInProgress && timeSinceLastReadback >= readbackInterval)
        {
            timeSinceLastReadback = 0f; // タイマーリセット
            
            // 非同期読み出しをリクエスト
            RequestAsyncReadback();
        }
    }

private async void RequestAsyncReadback()
    {
        if (readbackInProgress || sourceTexture == null) return;

        readbackInProgress = true;

        try
        {
            // AsyncGPUReadback.Request を Task でラップ (C# 7.0以降が必要)
            var request = await AsyncGPUReadback.RequestAsync(sourceTexture, 0);

            if (request.hasError)
            {
                Debug.LogError("GPUデータの読み込みに失敗しました (AsyncGPUReadback.RequestAsync)");
            }
            else if (targetPerfLeds.perfLedData != null)
            {
                // 成功：NativeArray を取得し，byte配列に変換
                NativeArray<Color32> data = request.GetData<Color32>();
                ProcessPixelData(data); // L.185
            }
        }
        catch (Exception e) // ★エラー修正: System.Exception
        {
            Debug.LogError($"AsyncGPUReadback 中にエラー: {e.Message}"); // L.189 (エラー報告箇所)
        }
        finally
        {
            readbackInProgress = false;
        }
    }

    /// <summary>
    /// GPUから読み出した NativeArray<Color32> を PerfLeds.perfLedData (byte[]) に変換する
    /// </summary>
    private void ProcessPixelData(NativeArray<Color32> data)
    {
        byte[] destinationData = targetPerfLeds.perfLedData;
        int textureWidth = sourceTexture.width; // 90 (のはず)

        // NativeArray は (y * width + x) の順で格納されている

        // ★修正: ループの上限を textureWidth ではなく NUM_STRIPS にする
        //          さらに，textureWidth が NUM_STRIPS より小さい場合も考慮し，Math.Min を使う
        int loopWidth = Math.Min(NUM_STRIPS, textureWidth);
        
        if (textureWidth < NUM_STRIPS)
        {
            Debug.LogWarning($"textureWidth ({textureWidth}) が NUM_STRIPS ({NUM_STRIPS}) より小さいため，{loopWidth} ストリップ分のみ処理します．");
        }


        for (int logicalX = 0; logicalX < loopWidth; logicalX++) // logicalX = 0.. (89 または textureWidth-1)
        {
            // 1. 論理X座標 (テクスチャのX座標) を 物理ストリップID に変換
            // logicalX < NUM_STRIPS が保証されているため，logicalToPhysicalMap アクセスは安全
            int physicalId = logicalToPhysicalMap[logicalX]; // L.211 (旧)
            
            // 2. この物理IDが反転対象か取得
            // physicalId < NUM_STRIPS が保証されているため，physicalReverseMap アクセスは安全
            bool reversed = physicalReverseMap[physicalId];
            
            // 3. この物理ストリップのデータ書き込み先ベースインデックス
            // (物理ID * 360 bytes)
            int destBaseByteIndex = physicalId * BYTES_PER_STRIP;

            for (int y = 0; y < LEDS_PER_STRIP; y++) // y = 0..119
            {
                // (logicalX, y) のピクセル色を取得
                // logicalX < textureWidth が保証されているため，data アクセスは安全
                Color32 color = data[y * textureWidth + logicalX]; // L.220 (旧)

                // このピクセルを格納すべきYインデックスを計算
                int y_index;
                if (!reversed)
                {
                    y_index = y; // 通常順 (0, 1, 2...)
                }
                else
                {
                    y_index = (LEDS_PER_STRIP - 1) - y; // 反転順 (119, 118, 117...)
                }

                // 最終的な書き込み先インデックス
                int destByteOffset = destBaseByteIndex + (y_index * 3);

                // RGBA -> GRB (または RGB) 
                // UdpController側でGRB->RGB変換コメントアウトがあるため，ここではRGB順で書き込む
                // (destByteOffset + 2 < destinationData.Length (32400) は保証されている)
                destinationData[destByteOffset + 0] = color.r;
                destinationData[destByteOffset + 1] = color.g;
                destinationData[destByteOffset + 2] = color.b;
            }
        }
    }
}

