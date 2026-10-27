using Arte.Rhythm.Song;
using UnityEngine;

namespace Arte.Rhythm.Render
{
    /// <summary>
    /// シェーダー（マテリアル）でキャンバス全面を毎フレーム描く．
    ///
    /// シェーダーには次の値を渡す（Shaders/CanvasSweep.shader が例）:
    ///   _ArteTime       曲の時刻（秒．conductor が再生中でなければ起動からの秒数）
    ///   _ArteCanvasSize (列数, LED 数, 1/列数, 1/LED 数)
    /// uv の x がリングの周方向（列），y が高さ（0 = 下端）．
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public sealed class LedCanvasEffect : MonoBehaviour
    {
        static readonly int TimeId = Shader.PropertyToID("_ArteTime");
        static readonly int SizeId = Shader.PropertyToID("_ArteCanvasSize");

        public LedCanvas canvas;
        public Material material;
        public SongConductor conductor;

        void LateUpdate()
        {
            if (canvas == null || canvas.Texture == null || material == null) return;
            var t = canvas.Texture;
            double time = conductor != null && conductor.IsStarted ? conductor.OutputTime : Time.timeAsDouble;
            material.SetFloat(TimeId, (float)time);
            material.SetVector(SizeId, new Vector4(t.width, t.height, 1f / t.width, 1f / t.height));
            Graphics.Blit(null, t, material);
        }
    }
}
