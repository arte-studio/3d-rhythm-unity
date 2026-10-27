using System;
using Arte.Rhythm.Hardware;

namespace Arte.Rhythm.Net
{
    /// <summary>
    /// 演出用キャンバス（GPU で描いたテクスチャを読み戻したもの）を LedFrame の演出用ピンに配線順で詰める．
    ///
    /// キャンバスは幅 = 列数（PerfLayout.ColumnCount），高さ = 1 本の LED 数．1 列 = 1 本のストリップ，
    /// y = 0 が下端．テクスチャを読み戻した並び（RGBA 4 バイト，行ごと，下の行から）のまま渡す．
    /// ジグザグ（上から配線されているストリップ）と外したストリップ（黒）は PerfLayout に従う．
    /// </summary>
    public static class CanvasPacker
    {
        public static void Pack(ReadOnlySpan<byte> rgba, PerfLayout perf, LedFrame frame)
        {
            int columns = perf.ColumnCount, rows = perf.LedsPerStrip;
            if (rgba.Length < columns * rows * 4) throw new ArgumentException("キャンバスの大きさが列数 × LED 数と合わない");
            for (int column = 0; column < columns; column++)
            {
                var s = perf.GetStrip(column);
                if (s.Address.device >= frame.DeviceCount) continue;
                var pin = frame.Pin(s.Address.device, s.Address.pin);
                int stripStart = s.Address.slot * rows * 3;
                if (stripStart + rows * 3 > pin.Length) continue;
                var dest = pin.Slice(stripStart, rows * 3);
                if (!s.Installed)
                {
                    dest.Clear();
                    continue;
                }
                for (int row = 0; row < rows; row++)
                {
                    int src = (row * columns + column) * 4;
                    int w = (s.TopFirst ? rows - 1 - row : row) * 3;
                    dest[w] = rgba[src];
                    dest[w + 1] = rgba[src + 1];
                    dest[w + 2] = rgba[src + 2];
                }
            }
        }
    }
}
