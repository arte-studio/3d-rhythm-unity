using UnityEngine.Timeline;

namespace Arte.Rhythm.Render
{
    /// <summary>曲の区切り（前奏，A メロ…）の目印（SongGridTrack に自動で置く）．名前は Timeline 上に表示される．</summary>
    public sealed class SongSectionMarker : Marker
    {
        public string sectionName = "";
    }
}
