using Arte.Rhythm.Charts;
using UnityEngine;
using UnityEngine.Timeline;
using UnityEngine.Video;

namespace Arte.Rhythm.Song
{
    /// <summary>
    /// 1曲分のアセットのまとめ．譜面などのデータ（song.json）と，音声・動画・Timeline を結び付ける．
    /// ビルドでもそのまま読める（旧版のようにファイルを直接読まない）．
    /// 作り方: Project ビューで右クリック → Create/Arte Rhythm/Song．
    /// </summary>
    [CreateAssetMenu(menuName = "Arte Rhythm/Song", fileName = "Song")]
    public sealed class SongAsset : ScriptableObject
    {
        [Tooltip("song.json（譜面・テンポ・同期の補正値）")]
        public TextAsset songJson;

        [Tooltip("曲の音声")]
        public AudioClip audio;

        [Tooltip("旧方式の演出動画（任意）．Timeline の演出へ移行したら外す")]
        public VideoClip video;

        [Tooltip("演出の Timeline（任意）")]
        public TimelineAsset timeline;

        SongData cached;
        string cachedText;

        /// <summary>song.json を読む．エディタで書き換えられたら読み直す．</summary>
        public SongData Data
        {
            get
            {
                if (songJson == null) return null;
                if (cached == null || !ReferenceEquals(cachedText, songJson.text))
                {
                    cachedText = songJson.text;
                    cached = SongJson.Load(cachedText);
                }
                return cached;
            }
        }

        /// <summary>曲の長さ（秒）．song.json に無ければ音声の長さから求める．</summary>
        public double LengthSec
        {
            get
            {
                var d = Data;
                if (d != null && d.LengthSec > 0) return d.LengthSec;
                return audio != null ? audio.length - (d?.AudioStartSec ?? 0) : 0;
            }
        }
    }
}
