using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Video;

namespace Arte.Rhythm.Song
{
    /// <summary>
    /// 演出動画と Timeline を曲時刻に合わせる．
    ///
    /// 旧版の演出動画はシーン読み込みと同時にループ再生が始まり，曲とは別々に進んでいた．
    /// ここでは動画を外部時刻（VideoTimeReference.ExternalTime）で動かし，曲時刻 + videoOffsetSec を毎フレーム渡す．
    /// Timeline は手動更新にして，曲時刻をそのまま director.time に入れて評価する．
    /// どちらも LED に出す演出なので，SongConductor.OutputTime（LED の遅れを見込んだ時刻）を使う．
    ///
    /// Timeline に音声トラックを置く場合は編集時の目安用とし，ミュートしておく（音は SongConductor が鳴らす）．
    /// </summary>
    public sealed class SongVisualSync : MonoBehaviour
    {
        public SongConductor conductor;

        [Tooltip("演出動画（任意）．曲の SongAsset.video を再生する")]
        public VideoPlayer videoPlayer;

        [Tooltip("演出の Timeline（任意）．曲の SongAsset.timeline を再生する")]
        public PlayableDirector director;

        void OnEnable()
        {
            if (conductor != null) conductor.Started += OnStarted;
        }

        void OnDisable()
        {
            if (conductor != null) conductor.Started -= OnStarted;
        }

        void OnStarted(double fromSec)
        {
            var song = conductor.song;
            if (videoPlayer != null)
            {
                videoPlayer.playOnAwake = false;
                videoPlayer.isLooping = false;
                if (song.video != null) videoPlayer.clip = song.video;
                videoPlayer.timeReference = VideoTimeReference.ExternalTime;
                videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
                videoPlayer.Play();
            }
            if (director != null)
            {
                // 曲に Timeline が無ければ，シーンの director に入っているもの（見本など）をそのまま使う
                if (song.timeline != null) director.playableAsset = song.timeline;
                director.timeUpdateMode = DirectorUpdateMode.Manual;
                director.extrapolationMode = DirectorWrapMode.Hold;
                director.RebuildGraph();
            }
        }

        void LateUpdate()
        {
            if (conductor == null || !conductor.IsStarted) return;
            double t = conductor.OutputTime;
            if (double.IsNaN(t)) return;

            if (videoPlayer != null && videoPlayer.clip != null)
            {
                double vt = t + conductor.Data.VideoOffsetSec;
                videoPlayer.externalReferenceTime = vt < 0 ? 0 : vt;
            }
            if (director != null && director.playableAsset != null)
            {
                director.time = t < 0 ? 0 : t;
                director.Evaluate();
            }
        }
    }
}
