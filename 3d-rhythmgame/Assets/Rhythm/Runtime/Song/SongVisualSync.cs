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

        /// <summary>今の演出の名前（Timeline の名前）．F9 で切り替える．</summary>
        public static string CurrentEffects { get; private set; }

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
                var effects = song.AllEffects();
                string wanted = CurrentEffects ?? Arg("-effects");
                var pick = effects.Find(e => e.name == wanted) ?? (effects.Count > 0 ? effects[0] : null);
                if (wanted != null && (pick == null || pick.name != wanted)) Debug.LogWarning($"[SongVisualSync] 演出 {wanted} が無い（{song.name} の effectVariants）");
                if (pick != null) director.playableAsset = pick;
                CurrentEffects = director.playableAsset != null ? director.playableAsset.name : null;
                director.timeUpdateMode = DirectorUpdateMode.Manual;
                director.extrapolationMode = DirectorWrapMode.Hold;
                director.RebuildGraph();
            }
        }

        static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        void Update()
        {
            // F9: 曲の別の演出（SongAsset.effectVariants）に切り替える
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || !kb.f9Key.wasPressedThisFrame || director == null || conductor == null || conductor.song == null) return;
            var effects = conductor.song.AllEffects();
            if (effects.Count < 2) return;
            int i = effects.FindIndex(e => e == director.playableAsset);
            var next = effects[(i + 1) % effects.Count];
            director.playableAsset = next;
            director.RebuildGraph();
            CurrentEffects = next.name;
            Debug.Log($"[SongVisualSync] 演出: {next.name}");
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
