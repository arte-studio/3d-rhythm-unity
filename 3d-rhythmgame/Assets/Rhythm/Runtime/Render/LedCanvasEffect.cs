using UnityEngine;

namespace Arte.Rhythm.Render
{
    /// <summary>
    /// シェーダー（マテリアル）でキャンバス全面を毎フレーム描いて重ねる（Timeline を使わないとき用）．
    /// シェーダーに渡す値は LedCanvas の説明（Shaders/ArteLed.cginc）．
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class LedCanvasEffect : MonoBehaviour
    {
        public LedCanvas canvas;
        public Material material;
        public LedCanvas.Blend blend = LedCanvas.Blend.Normal;
        [Range(0, 1)] public float opacity = 1f;

        void LateUpdate()
        {
            if (canvas != null && material != null) canvas.Composite(material, blend, opacity);
        }
    }
}
