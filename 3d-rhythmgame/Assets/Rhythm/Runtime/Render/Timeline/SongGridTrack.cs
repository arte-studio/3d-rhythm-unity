using UnityEngine.Timeline;

namespace Arte.Rhythm.Render
{
    /// <summary>
    /// Timeline の一番上に置く「曲（小節と区切り）」のトラック．曲のテンポと区切り（song.json）から自動で作る目印だけが入る．
    /// 手で動かさない（ロックしてある）．テンポや区切りを直すと譜面エディタの保存で作り直される．
    /// </summary>
    [TrackColor(0.6f, 0.6f, 0.7f)]
    public sealed class SongGridTrack : MarkerTrack
    {
    }
}
