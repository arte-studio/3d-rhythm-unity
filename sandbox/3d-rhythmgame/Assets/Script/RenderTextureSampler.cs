using UnityEngine;
using UnityEngine.Rendering; // AsyncGPUReadback のために必要
using UnityEngine.Video; // VideoPlayer のために必要

/// <summary>
/// RenderTexture の内容をサンプリングし，PerfLeds のバイト配列に書き込むコンポーネント．
/// 
/// 役割:
/// 1. 指定された RenderTexture の内容を非同期GPUリードバックで Texture2D にコピーする．
/// 2. Texture2D のピクセルデータを PerfLeds.perfLedData にマッピングする．
/// 3. ストリップのジグザグ配置（偶数: 上->下, 奇数: 下->上）を処理する．
/// </summary>
public class RenderTextureSampler : MonoBehaviour
{
    [Header("ターゲット")]
    [Tooltip("サンプリング対象のRenderTexture")]
    public RenderTexture sourceRenderTexture;

    [Tooltip("書き込み先のPerfLedsコンポーネント")]
    public PerfLeds targetPerfLeds;

    [Tooltip("参照するVideoPlayer (準備完了を待つため)")]
    public VideoPlayer sourceVideoPlayer; 

    [Header("LED配置設定")]
    [Tooltip("ストリップ（テープLED）の総数 (注: PerfLeds.csのコメントに基づき90)")]
    public int totalStrips = 90;

    [Tooltip("1ストリップあたりのLEDの数")]
    public int ledsPerStrip = 120;

    // 定数
    private const int BYTES_PER_LED = 3; // RGB

    // 内部変数
    private Texture2D cpuTexture; // GPUから読み出すための一時テクスチャ
    private bool isReadbackInProgress = false;
    private bool isReadyToSample = false; // 準備完了フラグ

    void Start()
    {
        if (sourceRenderTexture == null || targetPerfLeds == null)
        {
            Debug.LogError("RenderTexture または PerfLeds が設定されていません．", this);
            this.enabled = false;
            return;
        }

        // VideoPlayerが設定されているか確認
        if (sourceVideoPlayer == null)
        {
            Debug.LogError("Source Video Player が設定されていません．インスペクタで設定してください．", this);
            this.enabled = false;
            return;
        }

        // Texture2Dの解像度が期待通りか確認
        if (sourceRenderTexture.width != totalStrips || sourceRenderTexture.height != ledsPerStrip)
        {
            Debug.LogWarningFormat(
                this,
                "RenderTextureの解像度 ({0}x{1}) が，期待されるLED配置 ({2}x{3}) と一致しません．" +
                "テクスチャのUVマッピングが (幅: totalStrips, 高さ: ledsPerStrip) になっていることを確認してください．",
                sourceRenderTexture.width, sourceRenderTexture.height, totalStrips, ledsPerStrip
            );
        }
        
        // CPU側で読み取るための一時テクスチャを初期化 (互換性の高い形式)
        cpuTexture = new Texture2D(sourceRenderTexture.width, sourceRenderTexture.height, TextureFormat.RGBA32, false);

        // PerfLeds のデータバッファを初期化
        int bytesPerStrip = ledsPerStrip * BYTES_PER_LED;
        int totalBytes = totalStrips * bytesPerStrip;
        
        // PerfLeds.cs のコメント通りのサイズ (90 * 120 * 3 = 32400) で初期化
        targetPerfLeds.perfLedData = new byte[totalBytes];
        
        Debug.Log($"PerfLeds buffer initialized: {totalBytes} bytes ({totalStrips} strips * {bytesPerStrip} bytes/strip)");

        // サンプリングはまだ開始しない
        isReadyToSample = false;
    }

    void Update()
    {
        // VideoPlayer の準備が完了するまで待機
        if (!isReadyToSample)
        {
            // VideoPlayerが再生準備完了(最初のフレームがデコードされた)か確認
            if (sourceVideoPlayer.isPrepared)
            {
                isReadyToSample = true;
                Debug.Log("VideoPlayer is prepared. サンプリングを開始します．");
            }
            else
            {
                // まだ準備できていないので待機
                return;
            }
        }

        if (isReadbackInProgress || sourceRenderTexture == null || targetPerfLeds == null)
        {
            return;
        }

        // 非同期GPUリードバックをリクエスト (Updateのブロッキング回避)
        isReadbackInProgress = true;
        AsyncGPUReadback.Request(sourceRenderTexture, 0, OnGpuReadbackComplete);
    }

    /// <summary>
    /// GPUリードバック完了時のコールバック
    /// </summary>
    void OnGpuReadbackComplete(AsyncGPUReadbackRequest request)
    {
        if (!Application.isPlaying) // 再生停止時にコールバックが来ることがある
        {
            isReadbackInProgress = false;
            return;
        }

        if (request.hasError)
        {
            Debug.LogError("GPUリードバックに失敗しました．");
            isReadbackInProgress = false;
            return;
        }

        // ネイティブデータをTexture2Dにロード
        if (cpuTexture != null)
        {
            try
            {
                cpuTexture.LoadRawTextureData(request.GetData<byte>());
                cpuTexture.Apply();

                // ピクセルデータを perfLedData にマッピング
                MapPixelsToLedData();
            }
            catch (UnityException ex)
            {
                 Debug.LogError($"GPUリードバックデータのロードに失敗しました: {ex.Message}");
            }
        }

        isReadbackInProgress = false;
    }

    /// <summary>
    /// Texture2D のピクセルを perfLedData (byte[]) にマッピングする．
    /// </summary>
    private void MapPixelsToLedData()
    {
        // GetPixels32() はテクスチャの左下 (y=0) から右上に向かって読み込む
        Color32[] pixels = cpuTexture.GetPixels32();
        
        int textureWidth = cpuTexture.width;   // = totalStrips (90)
        int textureHeight = cpuTexture.height; // = ledsPerStrip (120)

        byte[] ledData = targetPerfLeds.perfLedData;
        int bytesPerStrip = ledsPerStrip * BYTES_PER_LED; // 360 bytes

        // テクスチャのX座標 (ストリップID) でループ
        for (int stripId = 0; stripId < textureWidth; stripId++)
        {
            // このストリップのデータが格納される配列の開始インデックス
            int stripStartIndex = stripId * bytesPerStrip;
            
            // ストリップIDが偶数か奇数か (ジグザグ配置のため)
            bool isEvenStrip = (stripId % 2 == 0);

            // テクスチャのY座標 (LED ID) でループ
            for (int y = 0; y < textureHeight; y++)
            {
                // テクスチャ(pixels)配列から対応するピクセルを取得
                // (y * width + x)
                int pixelIndex = y * textureWidth + stripId;
                Color32 pixelColor = pixels[pixelIndex];

                //--- データ格納位置の計算 ---
                int ledIndex; // 0 (配列先頭) から 119 (配列末尾) までのインデックス

                if (isEvenStrip)
                {
                    // 偶数ストリップ: 上から下の順 (配列0=上, 配列119=下)
                    // テクスチャ Y=0 (下) -> LED 119 (下)
                    // テクスチャ Y=119 (上) -> LED 0 (上)
                    ledIndex = (textureHeight - 1) - y;
                }
                else
                {
                    // 奇数ストリップ: 下から上の順 (配列0=下, 配列119=上)
                    // テクスチャ Y=0 (下) -> LED 0 (下)
                    // テクスチャ Y=119 (上) -> LED 119 (上)
                    ledIndex = y;
                }

                // byte 配列内のインデックス
                int byteIndex = stripStartIndex + (ledIndex * BYTES_PER_LED);

                // RGBの順でバイト配列に格納
                ledData[byteIndex + 0] = pixelColor.r;
                ledData[byteIndex + 1] = pixelColor.g;
                ledData[byteIndex + 2] = pixelColor.b;
            }
        }
    }

    void OnDestroy()
    {
        // クリーンアップ
        if (cpuTexture != null)
        {
            Destroy(cpuTexture);
        }
    }
}
