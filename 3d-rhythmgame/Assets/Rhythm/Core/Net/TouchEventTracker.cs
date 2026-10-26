using System;

namespace Arte.Rhythm.Net
{
    /// <summary>
    /// 1 台分のタッチイベントの重複を取り除く．
    /// デバイスは直近 8 個のイベントを毎回送り直す（パケットが落ちても次で届くように）ので，
    /// 通し番号で初めて見たものだけを通す．
    /// 接続し直した直後は前回までの番号が分からないので，届いた時点から StartupWindowUs 以内のものだけを新しいとみなす．
    /// </summary>
    public sealed class TouchEventTracker
    {
        public const ulong StartupWindowUs = 500_000;

        bool primed;
        ushort lastSeq;

        /// <summary>デバイスが（再）接続したときに呼ぶ．</summary>
        public void Reset() => primed = false;

        /// <summary>新しいイベントを fresh に古い順で書き，個数を返す．</summary>
        public int Accept(in TouchHeader header, ReadOnlySpan<TouchEvent> events, Span<TouchEvent> fresh)
        {
            int n = 0;
            for (int i = 0; i < events.Length; i++)
            {
                var e = events[i];
                bool isNew = primed
                    ? (short)(ushort)(e.Seq - lastSeq) > 0
                    : header.DeviceTimeUs >= e.TimeUs && header.DeviceTimeUs - e.TimeUs <= StartupWindowUs;
                if (primed && !isNew) continue;
                if (isNew) fresh[n++] = e;
                lastSeq = e.Seq;
            }
            if (events.Length > 0) primed = true;
            return n;
        }
    }
}
