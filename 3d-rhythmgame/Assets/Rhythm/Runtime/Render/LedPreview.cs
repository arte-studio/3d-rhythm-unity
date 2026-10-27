using System.Net;
using Arte.Rhythm.Hardware;
using Arte.Rhythm.Net;
using Arte.Rhythm.Viewer;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arte.Rhythm.Render
{
    /// <summary>
    /// LED のシミュレーション表示（Viewer）．演出用 LED と，置き場所のあるノーツモジュールを描き，部屋を照らす光源も出す．
    ///
    /// 描くのは「LED に送った値」（明るさ・曲線を掛けた後）を光量（LedOutputModel.OutputToLinear）にしたもので，
    /// 画面への出し方は Unity のリニア色空間・トーンマップ・ブルームに任せる．値の取り方は 2 通り:
    /// - Local: 同じシーンの DeviceLink.Frame を，実機に送るのと同じ計算で LED に送る値にする
    /// - Mirror: 別の PC の LinkHost が送る PIXELS を受け取る（LinkOptions.MirrorTargets → MirrorReceiver）
    ///
    /// LED はリングの内側を向いていて，正面から横へ cos で暗くなり裏からは見えない（LedSceneModel）．
    /// 部屋を照らす光源（近くの LED をまとめたもの）は global の _ArteLedLight* に入れ，Arte/Viewer/LitByLeds が使う．
    /// 位置は PerfLayout（リングの中心が原点，m）にこの GameObject の Transform を掛ける．全 LED を 1 回の描画で描く．
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class LedPreview : MonoBehaviour
    {
        public enum Source { Local, Mirror }

        static readonly int PositionsId = Shader.PropertyToID("_Positions");
        static readonly int ColorsId = Shader.PropertyToID("_Colors");
        static readonly int SizeId = Shader.PropertyToID("_LedSize");
        static readonly int LocalToWorldId = Shader.PropertyToID("_LocalToWorld");
        static readonly int LightPosId = Shader.PropertyToID("_ArteLedLightPos");
        static readonly int LightColorId = Shader.PropertyToID("_ArteLedLightColor");
        static readonly int LightDirId = Shader.PropertyToID("_ArteLedLightDir");
        static readonly int NormalsId = Shader.PropertyToID("_Normals");
        static readonly int LightCountId = Shader.PropertyToID("_ArteLedLightCount");
        static readonly int LightScaleId = Shader.PropertyToID("_ArteLedLightScale");

        public Source source = Source.Local;
        [Tooltip("Local のときの送信元")]
        public DeviceLink link;
        [Tooltip("Mirror のときのハード構成と設営（Local なら DeviceLink のものを使う）")]
        public TextAsset hardwareJson;
        public TextAsset setupJson;
        public int mirrorPort = MirrorReceiver.DefaultPort;

        [Tooltip("Arte/LedPreview/Point のマテリアル")]
        public Material material;
        [Tooltip("LED 1 個の見た目の直径（m）")]
        public float ledSize = 0.012f;
        [Tooltip("LED の光の強さの倍率．LED は小さく強く光るので 1 より大きくしてブルームを効かせる")]
        public float exposure = 6f;
        [Tooltip("床や暗幕を照らす光の強さの倍率")]
        public float roomLightScale = 0.05f;
        [Tooltip("演出用ストリップ 1 本を何個の光源にまとめるか（部屋を照らす計算の細かさ）")]
        [Range(1, 24)] public int segmentsPerStrip = 6;

        public MirrorReceiver Mirror { get; private set; }
        public HardwareConfig Hardware { get; private set; }
        public PerfLayout Perf { get; private set; }
        public NoteLayout Notes { get; private set; }
        public SetupProfile Setup { get; private set; }

        LedSceneModel model;
        LedFrame output;
        GraphicsBuffer positions, normals, colors, lightPos, lightColor, lightDir;
        float[] linear, clusters;
        Vector4[] colorData, lightPosData, lightColorData, lightDirData;
        MaterialPropertyBlock props;
        Matrix4x4 lastMatrix;

        void Start()
        {
            SetupProfile setup;
            if (source == Source.Local)
            {
                if (link == null || link.Perf == null)
                {
                    Debug.LogError("[LedPreview] Local では DeviceLink を設定する");
                    enabled = false;
                    return;
                }
                Hardware = link.Hardware;
                setup = link.Setup;
            }
            else
            {
                if (hardwareJson == null || setupJson == null)
                {
                    Debug.LogError("[LedPreview] Mirror では hardwareJson と setupJson を設定する");
                    enabled = false;
                    return;
                }
                Hardware = HardwareJson.LoadConfig(hardwareJson.text);
                setup = HardwareJson.LoadSetup(setupJson.text);
            }
            if (material == null)
            {
                Debug.LogError("[LedPreview] マテリアルを設定する");
                enabled = false;
                return;
            }
            Setup = setup;
            Perf = new PerfLayout(Hardware, setup);
            Notes = new NoteLayout(Hardware, setup);
            int devices = Hardware.devices.count;
            output = LedFrame.FromLayout(Perf, Notes, devices);
            if (source == Source.Mirror)
            {
                Mirror = new MirrorReceiver(output, mirrorPort);
                Debug.Log($"[LedPreview] ポート {Mirror.Port} で LED のデータを待受（送る側は LinkOptions.MirrorTargets にこの PC を入れる）");
            }

            model = new LedSceneModel(Perf, Notes, devices, segmentsPerStrip);
            int n = model.LedCount, c = model.ClusterCount;
            var pos = new Vector3[n];
            for (int k = 0; k < n; k++) pos[k] = new Vector3(model.Positions[k * 3], model.Positions[k * 3 + 1], model.Positions[k * 3 + 2]);
            positions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 12);
            positions.SetData(pos);
            var nrm = new Vector3[n];
            for (int k = 0; k < n; k++) nrm[k] = new Vector3(model.Normals[k * 3], model.Normals[k * 3 + 1], model.Normals[k * 3 + 2]);
            normals = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 12);
            normals.SetData(nrm);
            colors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 16);
            lightPos = new GraphicsBuffer(GraphicsBuffer.Target.Structured, c, 16);
            lightColor = new GraphicsBuffer(GraphicsBuffer.Target.Structured, c, 16);
            lightDir = new GraphicsBuffer(GraphicsBuffer.Target.Structured, c, 16);
            lightDirData = new Vector4[c];
            linear = new float[n * 3];
            clusters = new float[c * 3];
            colorData = new Vector4[n];
            lightPosData = new Vector4[c];
            lightColorData = new Vector4[c];
            props = new MaterialPropertyBlock();
            lastMatrix = new Matrix4x4();
        }

        void OnDestroy()
        {
            Mirror?.Dispose();
            positions?.Release();
            normals?.Release();
            lightDir?.Release();
            colors?.Release();
            lightPos?.Release();
            lightColor?.Release();
            Shader.SetGlobalInt(LightCountId, 0);
        }

        void LateUpdate()
        {
            if (model == null || model.LedCount == 0) return;
            if (source == Source.Local) EncodeLocal();
            else Mirror.Poll();

            model.ComputeLinear(output, Hardware.output, 1f, linear);
            for (int k = 0; k < model.LedCount; k++)
                colorData[k] = new Vector4(linear[k * 3] * exposure, linear[k * 3 + 1] * exposure, linear[k * 3 + 2] * exposure, 1);
            colors.SetData(colorData);

            model.ComputeClusters(linear, clusters);
            var m = transform.localToWorldMatrix;
            if (m != lastMatrix)
            {
                lastMatrix = m;
                var cp = model.ClusterPositions;
                var cn = model.ClusterNormals;
                for (int i = 0; i < model.ClusterCount; i++)
                {
                    lightPosData[i] = m.MultiplyPoint3x4(new Vector3(cp[i * 3], cp[i * 3 + 1], cp[i * 3 + 2]));
                    lightDirData[i] = m.MultiplyVector(new Vector3(cn[i * 3], cn[i * 3 + 1], cn[i * 3 + 2])).normalized;
                }
                lightPos.SetData(lightPosData);
                lightDir.SetData(lightDirData);
            }
            for (int i = 0; i < model.ClusterCount; i++) lightColorData[i] = new Vector4(clusters[i * 3], clusters[i * 3 + 1], clusters[i * 3 + 2], 0);
            lightColor.SetData(lightColorData);
            Shader.SetGlobalBuffer(LightPosId, lightPos);
            Shader.SetGlobalBuffer(LightColorId, lightColor);
            Shader.SetGlobalBuffer(LightDirId, lightDir);
            Shader.SetGlobalInt(LightCountId, model.ClusterCount);
            Shader.SetGlobalFloat(LightScaleId, roomLightScale);

            props.SetBuffer(PositionsId, positions);
            props.SetBuffer(NormalsId, normals);
            props.SetBuffer(ColorsId, colors);
            props.SetFloat(SizeId, ledSize);
            props.SetMatrix(LocalToWorldId, m);
            var rp = new RenderParams(material)
            {
                worldBounds = new Bounds(transform.position, Vector3.one * 20f),
                matProps = props,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, model.LedCount * 6, 1);
        }

        // 実機に送るのと同じ値にする
        void EncodeLocal()
        {
            var frame = link.Frame;
            var opt = link.Host?.Options;
            var curve = opt != null ? opt.Curve : LedOutputModel.ParseCurve(link.Setup.outputCurve);
            byte brightPerf = opt != null ? opt.Config.BrightnessPerf : (byte)Mathf.Clamp(link.Setup.brightnessPerf, 0, 255);
            byte brightNote = opt != null ? opt.Config.BrightnessNote : (byte)Mathf.Clamp(link.Setup.brightnessNote, 0, 255);
            for (int d = 0; d < frame.DeviceCount; d++)
            {
                for (int p = 0; p < LedFrame.PinsPerDevice; p++)
                {
                    if (frame.LedCount(d, p) == 0) continue;
                    PixelCodec.ApplyOutput(frame.Pin(d, p), output.Pin(d, p), p == frame.NotePin ? brightNote : brightPerf, curve);
                }
            }
        }
    }
}
