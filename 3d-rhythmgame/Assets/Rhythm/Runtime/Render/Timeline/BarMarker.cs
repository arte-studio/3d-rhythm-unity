using UnityEngine.Timeline;

namespace Arte.Rhythm.Render
{
    /// <summary>小節の頭の目印（SongGridTrack に自動で置く）．クリップの端を寄せて拍に合わせるのに使う．</summary>
    public sealed class BarMarker : Marker
    {
        public int bar;
    }
}
