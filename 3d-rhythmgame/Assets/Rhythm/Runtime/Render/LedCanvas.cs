using System;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Hardware;
using Arte.Rhythm.Net;
using Arte.Rhythm.Song;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace Arte.Rhythm.Render
{
    /// <summary>
    /// 演出用 LED のキャンバス．GPU で描いた絵を読み戻して，DeviceLink.Frame の演出用ピンに配線順で詰める．
    ///
    /// キャンバスは幅 = ストリップの列数，高さ = 1 本の LED 数（kodaisai-1101 なら 90 × 120）で，1 ピクセル = LED 1 個．
    /// y = 0 が下端．色は画面で見える値（sRGB）で，LED に送るときに設営データの outputCurve と明るさを掛ける．
    ///
    /// 1 フレームの流れ:
    /// 1. 土台（BaseTexture）: canvasCamera や video が描く（無ければ黒）．これを作業用のキャンバスに写す
    /// 2. 演出を重ねる: Timeline の LED 演出トラック（LedEffectTrack）や LedCanvasEffect が Composite で重ねる
    /// 3. 描画の直前に作業用のキャンバスを Texture（sRGB 8bit）に写し，非同期で読み戻して LED へ（1〜2 フレーム遅れる）
    ///
    /// 演出のシェーダーには次の global の値を渡す（Shaders/ArteLed.cginc．テンプレートは Shaders/Templates）:
    ///   _ArteTime 曲の時刻（秒，LED の遅れ込み）/ _ArteBeat 拍（テンポが無ければ 120BPM）/ _ArteBar 小節（小数）
    ///   _ArteCanvasSize (列数, LED 数, 1/列数, 1/LED 数) / _ArtePositionTex 各ピクセルの LED の 3D 位置（m，w = 付いていれば 1）
    ///   _ArteLevel 曲の音量（0〜1）
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class LedCanvas : MonoBehaviour
    {
        public enum Blend { Normal, Add, Multiply, Lighten }

        static readonly int TimeId = Shader.PropertyToID("_ArteTime");
        static readonly int BeatId = Shader.PropertyToID("_ArteBeat");
        static readonly int BarId = Shader.PropertyToID("_ArteBar");
        static readonly int LevelId = Shader.PropertyToID("_ArteLevel");
        static readonly int SizeId = Shader.PropertyToID("_ArteCanvasSize");
        static readonly int PositionTexId = Shader.PropertyToID("_ArtePositionTex");
        static readonly int DstId = Shader.PropertyToID("_Dst");
        static readonly int ModeId = Shader.PropertyToID("_Mode");
        static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        public DeviceLink link;
        [Tooltip("曲の時刻と拍を演出に渡す")]
        public SongConductor conductor;
        [Tooltip("土台を撮るカメラ（任意）．targetTexture をこのキャンバスの土台にする")]
        public Camera canvasCamera;
        [Tooltip("土台に流す動画（任意）")]
        public VideoPlayer video;
        [Tooltip("Arte/LedCanvas/Composite のマテリアル（重ね方の計算）")]
        public Material compositeMaterial;
        [Tooltip("読み戻して LED に送る．切ると LedTestPattern など別のものが書いた内容をそのまま送る")]
        public bool writeToLeds = true;

        /// <summary>シーンの LedCanvas（Timeline のトラックにバインドが無いときに使う）．</summary>
        public static LedCanvas Current { get; private set; }

        /// <summary>LED に送る最終の絵（sRGB 8bit）．</summary>
        public RenderTexture Texture { get; private set; }
        /// <summary>カメラや動画が描く土台．</summary>
        public RenderTexture BaseTexture { get; private set; }
        public Texture2D PositionTexture { get; private set; }
        /// <summary>直近の読み戻しにかかった時間（要求してから届くまで，秒）．</summary>
        public float LastReadbackLatency { get; private set; }

        RenderTexture work, spare, layer;
        Material composite;
        bool pending, ownsComposite;
        float requestedAt;
        TempoMap tempo;
        int beatsPerBar = 4;
        LoudnessEnvelope loudness;
        SongAsset loudnessSong;

        void Start()
        {
            if (link == null || link.Perf == null)
            {
                Debug.LogError("[LedCanvas] DeviceLink を設定する");
                enabled = false;
                return;
            }
            int w = link.Perf.ColumnCount, h = link.Perf.LedsPerStrip;
            Texture = NewTarget("LedCanvas", w, h, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            BaseTexture = NewTarget("LedCanvasBase", w, h, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            work = NewTarget("LedCanvasWork", w, h, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            spare = NewTarget("LedCanvasSpare", w, h, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            layer = NewTarget("LedCanvasLayer", w, h, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            if (canvasCamera != null) canvasCamera.targetTexture = BaseTexture;
            if (video != null)
            {
                video.renderMode = VideoRenderMode.RenderTexture;
                video.targetTexture = BaseTexture;
            }
            composite = compositeMaterial;
            if (composite == null)
            {
                composite = new Material(Shader.Find("Arte/LedCanvas/Composite"));
                ownsComposite = true;
            }
            PositionTexture = BuildPositionTexture(link.Perf);
            Shader.SetGlobalTexture(PositionTexId, PositionTexture);
            Shader.SetGlobalVector(SizeId, new Vector4(w, h, 1f / w, 1f / h));
            RenderPipelineManager.beginContextRendering += OnBeginRendering;
            Current = this;
        }

        void OnDestroy()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginRendering;
            if (Current == this) Current = null;
            foreach (var rt in new[] { Texture, BaseTexture, work, spare, layer })
                if (rt != null) rt.Release();
            if (PositionTexture != null) Destroy(PositionTexture);
            if (ownsComposite && composite != null) Destroy(composite);
        }

        static RenderTexture NewTarget(string name, int w, int h, RenderTextureFormat format, RenderTextureReadWrite rw)
        {
            var rt = new RenderTexture(w, h, 0, format, rw) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            rt.Create();
            return rt;
        }

        // 各ピクセル（列, LED）の LED の 3D 位置．外したストリップは w = 0
        static Texture2D BuildPositionTexture(PerfLayout perf)
        {
            int w = perf.ColumnCount, h = perf.LedsPerStrip;
            var tex = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true) { name = "LedPositions", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int c = 0; c < w; c++)
            {
                bool installed = perf.GetStrip(c).Installed;
                for (int r = 0; r < h; r++)
                {
                    perf.GetLedPosition(c, r, out float x, out float y, out float z);
                    px[r * w + c] = new Color(x, y, z, installed ? 1 : 0);
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>このフレームの演出を始める（時刻を渡し，土台を作業用のキャンバスに写す）．LateUpdate の最初に呼ばれる．</summary>
        void LateUpdate()
        {
            if (work == null) return;
            double t = conductor != null && conductor.IsStarted ? conductor.OutputTime : Time.timeAsDouble;
            if (double.IsNaN(t)) t = 0;
            if (conductor != null && conductor.IsStarted && tempo == null && conductor.Data != null)
            {
                tempo = conductor.Data.TempoMap();
                beatsPerBar = tempo.Points[0].BeatsPerBar;
            }
            double beat = (tempo ?? DefaultTempo).BeatOf(t);
            Shader.SetGlobalFloat(LevelId, Loudness()?.At(t) ?? 0f);
            Shader.SetGlobalFloat(TimeId, (float)t);
            Shader.SetGlobalFloat(BeatId, (float)beat);
            Shader.SetGlobalFloat(BarId, (float)(beat / beatsPerBar));
            var prev = RenderTexture.active;
            if (canvasCamera != null || (video != null && video.clip != null)) Graphics.Blit(BaseTexture, work);
            else Clear(work);
            RenderTexture.active = prev;
        }

        static readonly TempoMap DefaultTempo = new TempoMap(null);

        // 曲の音量（_ArteLevel）．曲が決まったら音声から一度だけ求める（Load Type が Decompress On Load の音声だけ）
        LoudnessEnvelope Loudness()
        {
            var song = conductor != null && conductor.IsStarted ? conductor.song : null;
            if (song == null || song == loudnessSong) return loudness;
            loudnessSong = song;
            loudness = null;
            tempo = null;
            var clip = song.audio;
            if (clip == null || clip.loadType != AudioClipLoadType.DecompressOnLoad || !clip.LoadAudioData()) return null;
            var samples = new float[clip.samples * clip.channels];
            if (!clip.GetData(samples, 0)) return null;
            loudness = LoudnessEnvelope.FromSamples(samples, clip.channels, clip.frequency, conductor.Data?.AudioStartSec ?? 0);
            return loudness;
        }

        static void Clear(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = prev;
        }

        /// <summary>
        /// 演出（キャンバス全面を描くシェーダーのマテリアル）を今のキャンバスに重ねる．opacity は 0〜1．
        /// LateUpdate の間（Timeline の評価も含む）に呼ぶ．
        /// </summary>
        public void Composite(Material effect, Blend mode, float opacity)
        {
            if (work == null || effect == null || opacity <= 0f) return;
            var prev = RenderTexture.active;
            Graphics.Blit(null, layer, effect);
            composite.SetTexture(DstId, work);
            composite.SetFloat(ModeId, (float)mode);
            composite.SetFloat(OpacityId, Mathf.Clamp01(opacity));
            Graphics.Blit(layer, spare, composite);
            (work, spare) = (spare, work);
            RenderTexture.active = prev;
        }

        // すべての演出を重ね終わったあと（描画の直前）に，LED に送る絵を作って読み戻す
        void OnBeginRendering(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras)
        {
            if (work == null) return;
            // Blit は描き込み先（RenderTexture.active）を変えたままにする．戻さないと，そのあとの OnGUI（画面の文字やボタン）が
            // LED のキャンバスに描かれてしまう（2026-10-28 に見つけた．プレビューの録画のキャンバスに HUD が写っていた）
            var prev = RenderTexture.active;
            Graphics.Blit(work, Texture);
            RenderTexture.active = prev;
            if (!writeToLeds || pending) return;
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
