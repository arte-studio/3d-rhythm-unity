using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Arte.Rhythm.Render
{
    /// <summary>LED 演出のクリップ 1 個．</summary>
    [Serializable]
    public sealed class LedEffectClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("演出（Arte/LED/... のシェーダーのマテリアル．Assets/Rhythm/Effects/Templates を複製して使う）")]
        public Material material;
        public LedEffectBehaviour template = new LedEffectBehaviour();

        public ClipCaps clipCaps => ClipCaps.Blending;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<LedEffectBehaviour>.Create(graph, template);
            playable.GetBehaviour().material = material;
            return playable;
        }
    }

    /// <summary>クリップの設定（不透明度はクリップのカーブで動かせる）．</summary>
    [Serializable]
    public sealed class LedEffectBehaviour : PlayableBehaviour
    {
        [Tooltip("重ね方: 普通 / 足す / 掛ける / 明るい方")]
        public LedCanvas.Blend blend = LedCanvas.Blend.Normal;
        [Range(0, 1), Tooltip("不透明度")]
        public float opacity = 1f;

        [NonSerialized] public Material material;
    }
}
