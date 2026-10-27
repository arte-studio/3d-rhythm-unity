using Arte.Rhythm.Hardware;
using Arte.Rhythm.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arte.Rhythm.Render
{
    /// <summary>
    /// 演出用 LED のシミュレーション表示（P6 の Viewer までのつなぎ）．
    ///
    /// DeviceLink.Frame を，実機に送るのと同じ曲線と明るさ（LedOutputModel.Encode）で LED に送る値にし，
    /// それを光量（LedOutputModel.OutputToLinear）として HDR で描く．画面への出し方は Unity のリニア色空間・
    /// トーンマップ・ブルームに任せるので，実機の LED と同じく「送った値に比例した光」として見える．
    /// 外したストリップは描かない．位置は PerfLayout（リングの中心が原点，m）にこの GameObject の Transform を掛ける．
    /// 全 LED を 1 回の描画（GPU で四角形を作る）で描く．
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class LedPreview : MonoBehaviour
    {
        static readonly int PositionsId = Shader.PropertyToID("_Positions");
        static readonly int ColorsId = Shader.PropertyToID("_Colors");
        static readonly int SizeId = Shader.PropertyToID("_LedSize");
        static readonly int LocalToWorldId = Shader.PropertyToID("_LocalToWorld");

        public DeviceLink link;
        [Tooltip("Arte/LedPreview/Point のマテリアル")]
        public Material material;
        [Tooltip("LED 1 個の見た目の直径（m）")]
        public float ledSize = 0.012f;
        [Tooltip("光の強さの倍率．LED は小さく強く光るので 1 より大きくしてブルームを効かせる")]
        public float exposure = 6f;

        GraphicsBuffer positions, colors;
        Vector4[] colorData;
        int[] ledDevice, ledPin, ledOffset;
        byte[][] output;
        int count;
        MaterialPropertyBlock props;

        void Start()
        {
            if (link == null || link.Perf == null || material == null)
            {
                Debug.LogError("[LedPreview] DeviceLink とマテリアルを設定する");
                enabled = false;
                return;
            }
            var perf = link.Perf;
            int rows = perf.LedsPerStrip;
            int max = perf.ColumnCount * rows;
            var pos = new Vector3[max];
            ledDevice = new int[max];
            ledPin = new int[max];
            ledOffset = new int[max];
            for (int column = 0; column < perf.ColumnCount; column++)
            {
                var s = perf.GetStrip(column);
                if (!s.Installed || s.Address.device >= link.Frame.DeviceCount) continue;
                for (int row = 0; row < rows; row++)
                {
                    perf.GetLedPosition(column, row, out float x, out float y, out float z);
                    pos[count] = new Vector3(x, y, z);
                    ledDevice[count] = s.Address.device;
                    ledPin[count] = s.Address.pin;
                    ledOffset[count] = (s.Address.slot * rows + perf.WireIndexOf(column, row)) * 3;
                    count++;
                }
            }
            positions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 12);
            positions.SetData(pos, 0, 0, count);
            colors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
            colorData = new Vector4[count];
            output = new byte[link.Frame.DeviceCount * LedFrame.PinsPerDevice][];
            props = new MaterialPropertyBlock();
        }

        void OnDestroy()
        {
            positions?.Release();
            colors?.Release();
        }

        void LateUpdate()
        {
            if (count == 0) return;
            var frame = link.Frame;
            var opt = link.Host?.Options;
            var curve = opt != null ? opt.Curve : LedOutputModel.ParseCurve(link.Setup.outputCurve);
            byte brightness = opt != null ? opt.Config.BrightnessPerf : (byte)Mathf.Clamp(link.Setup.brightnessPerf, 0, 255);
            var outCfg = link.Hardware.output;

            // ピンごとに，実機に送るのと同じ値にする
            for (int d = 0; d < frame.DeviceCount; d++)
            {
                for (int p = 0; p < LedFrame.PinsPerDevice; p++)
                {
                    if (p == frame.NotePin || frame.LedCount(d, p) == 0) continue;
                    var src = frame.Pin(d, p);
                    int i = d * LedFrame.PinsPerDevice + p;
                    if (output[i] == null || output[i].Length != src.Length) output[i] = new byte[src.Length];
                    PixelCodec.ApplyOutput(src, output[i], brightness, curve);
                }
            }
            float lr = Channel(outCfg, 0) * exposure, lg = Channel(outCfg, 1) * exposure, lb = Channel(outCfg, 2) * exposure;
            float gamma = outCfg.firmwareGamma;
            for (int k = 0; k < count; k++)
            {
                var o = output[ledDevice[k] * LedFrame.PinsPerDevice + ledPin[k]];
                int off = ledOffset[k];
                colorData[k] = new Vector4(
                    LedOutputModel.OutputToLinear(o[off], gamma) * lr,
                    LedOutputModel.OutputToLinear(o[off + 1], gamma) * lg,
                    LedOutputModel.OutputToLinear(o[off + 2], gamma) * lb, 1);
            }
            colors.SetData(colorData);

            props.SetBuffer(PositionsId, positions);
            props.SetBuffer(ColorsId, colors);
            props.SetFloat(SizeId, ledSize);
            props.SetMatrix(LocalToWorldId, transform.localToWorldMatrix);
            var rp = new RenderParams(material)
            {
                worldBounds = new Bounds(transform.position, Vector3.one * 20f),
                matProps = props,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, count * 6, 1);
        }

        static float Channel(OutputSection o, int i) =>
            o.channelLuminance != null && o.channelLuminance.Length > i ? o.channelLuminance[i] : 1f;
    }
}
