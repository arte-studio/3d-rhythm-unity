using System;
using Arte.Rhythm.Net;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace Arte.Rhythm.Render
{
    /// <summary>
    /// 演出用 LED のキャンバス．GPU で描いた絵を読み戻して，DeviceLink.Frame の演出用ピンに配線順で詰める．
    ///
    /// キャンバスは幅 = ストリップの列数，高さ = 1 本の LED 数（kodaisai-1101 なら 90 × 120）の RenderTexture で，
    /// 1 ピクセル = LED 1 個．y = 0 が下端．色は画面で見える値（sRGB の 8bit）で，LED に送るときに
    /// 設営データの outputCurve と明るさを掛ける（LedOutputModel.Encode）．
    ///
    /// 描き方は何でもよい: canvasCamera にレイヤーを分けたオブジェクトを撮らせる，video に動画を流す，
    /// LedCanvasEffect でシェーダーを全面に描く，など（同じフレームに描かれたものを読む）．
    /// 読み戻しは非同期なので，LED に出るのは描いてから 1〜2 フレーム後．
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class LedCanvas : MonoBehaviour
    {
        public DeviceLink link;
        [Tooltip("キャンバスを撮るカメラ（任意）．targetTexture をこのキャンバスにする")]
        public Camera canvasCamera;
        [Tooltip("キャンバスに流す動画（任意）")]
        public VideoPlayer video;
        [Tooltip("読み戻して LED に送る．切ると LedTestPattern など別のものが書いた内容をそのまま送る")]
        public bool writeToLeds = true;

        public RenderTexture Texture { get; private set; }
        /// <summary>直近の読み戻しにかかった時間（要求してから届くまで，秒）．</summary>
        public float LastReadbackLatency { get; private set; }

        bool pending;
        float requestedAt;

        void Start()
        {
            if (link == null || link.Perf == null)
            {
                Debug.LogError("[LedCanvas] DeviceLink を設定する");
                enabled = false;
                return;
            }
            Texture = new RenderTexture(link.Perf.ColumnCount, link.Perf.LedsPerStrip, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "LedCanvas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            Texture.Create();
            if (canvasCamera != null) canvasCamera.targetTexture = Texture;
            if (video != null)
            {
                video.renderMode = VideoRenderMode.RenderTexture;
                video.targetTexture = Texture;
            }
        }

        void OnDestroy()
        {
            if (Texture != null) Texture.Release();
        }

        void LateUpdate()
        {
            if (!writeToLeds || pending || Texture == null) return;
            pending = true;
            requestedAt = Time.realtimeSinceStartup;
            AsyncGPUReadback.Request(Texture, 0, TextureFormat.RGBA32, OnReadback);
        }

        void OnReadback(AsyncGPUReadbackRequest request)
        {
            pending = false;
            if (this == null || request.hasError || !writeToLeds || link == null || link.Frame == null) return;
            LastReadbackLatency = Time.realtimeSinceStartup - requestedAt;
            ReadOnlySpan<byte> rgba = request.GetData<byte>().AsReadOnlySpan();
            CanvasPacker.Pack(rgba, link.Perf, link.Frame);
        }
    }
}
