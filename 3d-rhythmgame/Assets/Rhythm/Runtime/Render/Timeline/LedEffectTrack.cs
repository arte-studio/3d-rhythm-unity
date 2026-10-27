using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Arte.Rhythm.Render
{
    /// <summary>
    /// Timeline の「LED 演出」トラック．クリップ（LedEffectClip）ごとに演出のマテリアルを選び，キャンバスに重ねる．
    /// - 同じトラックで重なったクリップは，重なりの間で入れ替わる（クロスフェード）．クリップの端を引くとフェードイン・アウト
    /// - トラックを増やすと上に重ねていく（Timeline の上のトラックから順に重ねる）
    /// - 時間は曲の時刻（SongVisualSync が Timeline を曲に合わせて進める）
    /// 重ねる先はシーンの LedCanvas（バインドは無い．トラックの左にはトラックの名前が出る）．
    /// </summary>
    [TrackColor(1f, 0.55f, 0.1f)]
    [TrackClipType(typeof(LedEffectClip))]
    public sealed class LedEffectTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount) =>
            ScriptPlayable<LedEffectMixer>.Create(graph, inputCount);
    }

    /// <summary>トラックの中のクリップを重みに合わせてキャンバスに重ねる．</summary>
    public sealed class LedEffectMixer : PlayableBehaviour
    {
        static readonly int ClipTimeId = Shader.PropertyToID("_ArteClipTime");
        static readonly int ClipProgressId = Shader.PropertyToID("_ArteClipProgress");

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var canvas = LedCanvas.Current;
            if (canvas == null) return;
            int n = playable.GetInputCount();
            for (int i = 0; i < n; i++)
            {
                float w = playable.GetInputWeight(i);
                if (w <= 0f) continue;
                var input = (ScriptPlayable<LedEffectBehaviour>)playable.GetInput(i);
                var b = input.GetBehaviour();
                if (b?.material == null) continue;
                double t = input.GetTime(), d = input.GetDuration();
                Shader.SetGlobalFloat(ClipTimeId, (float)t);
                Shader.SetGlobalFloat(ClipProgressId, d > 0 ? (float)(t / d) : 0f);
                canvas.Composite(b.material, b.blend, b.opacity * w);
            }
        }
    }
}
