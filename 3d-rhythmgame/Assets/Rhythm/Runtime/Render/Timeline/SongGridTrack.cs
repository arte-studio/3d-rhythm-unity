using UnityEngine.Timeline;

namespace Arte.Rhythm.Render
{
    /// <summary>
    /// Timeline の一番上に置く「曲（小節と区切り）」のトラック．曲のテンポと区切り（song.json）から自動で作る目印だけが入る．
    /// 手で動かしても，譜面エディタの保存で作り直される（テンポや区切りは譜面エディタで直す）．
    /// バインドを持たない（トラックの左に名前が出る）．
    /// </summary>
    [TrackColor(0.6f, 0.6f, 0.7f)]
    public sealed class SongGridTrack : TrackAsset
    {
    }
}
