using System;

namespace Arte.Rhythm.Net
{
    /// <summary>
    /// PIXELS の中身を小さくする（encoding 1 = PackBits）．ファーム側の同じ処理は arte_link/PixelCodec.h．
    ///
    /// PackBits（LED 1 個 = RGB 3 バイトを単位にする）:
    ///   制御バイト c が 0〜127 なら，続く c+1 個の LED をそのまま（3(c+1) バイト）
    ///   c が 128〜255 なら，続く 1 色（3 バイト）を c−126 個（2〜129 個）並べる
    /// 同じ色が続く所が多いほど小さくなり，最悪でも 128 個ごとに 1 バイト増えるだけ．
    /// </summary>
    public static class PixelCodec
    {
        public const int MaxLiteral = 128;
        public const int MaxRun = 129;

        /// <summary>LED count 個（rgb の長さ = count*3）を書き，バイト数を返す．dest に入りきらなければ −1．</summary>
        public static int EncodePackBits(ReadOnlySpan<byte> rgb, Span<byte> dest)
        {
            int count = rgb.Length / 3;
            int o = 0, i = 0, litStart = 0;
            while (i < count)
            {
                int run = 1;
                while (i + run < count && run < MaxRun && Same(rgb, i, i + run)) run++;
                if (run >= 2)
                {
                    if (!FlushLiteral(rgb, litStart, i, dest, ref o)) return -1;
                    if (o + 4 > dest.Length) return -1;
                    dest[o++] = (byte)(run + 126);
                    rgb.Slice(i * 3, 3).CopyTo(dest.Slice(o));
                    o += 3;
                    i += run;
                    litStart = i;
                }
                else
                {
                    i++;
                    if (i - litStart == MaxLiteral)
                    {
                        if (!FlushLiteral(rgb, litStart, i, dest, ref o)) return -1;
                        litStart = i;
                    }
                }
            }
            if (!FlushLiteral(rgb, litStart, count, dest, ref o)) return -1;
            return o;
        }

        /// <summary>LED count 個に戻して rgb に書く．データが壊れていれば false．</summary>
        public static bool DecodePackBits(ReadOnlySpan<byte> src, Span<byte> rgb, int count)
        {
            int o = 0, i = 0;
            while (o < count)
            {
                if (i >= src.Length) return false;
                int c = src[i++];
                if (c < 128)
                {
                    int n = c + 1;
                    if (o + n > count || i + n * 3 > src.Length) return false;
                    src.Slice(i, n * 3).CopyTo(rgb.Slice(o * 3));
                    i += n * 3;
                    o += n;
                }
                else
                {
                    int n = c - 126;
                    if (o + n > count || i + 3 > src.Length) return false;
                    for (int k = 0; k < n; k++) src.Slice(i, 3).CopyTo(rgb.Slice((o + k) * 3));
                    i += 3;
                    o += n;
                }
            }
            return true;
        }

        static bool Same(ReadOnlySpan<byte> rgb, int a, int b) =>
            rgb[a * 3] == rgb[b * 3] && rgb[a * 3 + 1] == rgb[b * 3 + 1] && rgb[a * 3 + 2] == rgb[b * 3 + 2];

        static bool FlushLiteral(ReadOnlySpan<byte> rgb, int from, int to, Span<byte> dest, ref int o)
        {
            int n = to - from;
            if (n == 0) return true;
            if (o + 1 + n * 3 > dest.Length) return false;
            dest[o++] = (byte)(n - 1);
            rgb.Slice(from * 3, n * 3).CopyTo(dest.Slice(o));
            o += n * 3;
            return true;
        }

        /// <summary>明るさを掛ける（LedOutputModel.ApplyBrightness と同じ式を表で引く）．</summary>
        public static void ApplyBrightness(ReadOnlySpan<byte> src, Span<byte> dest, byte brightness)
        {
            var table = tables[brightness] ??= BuildTable(brightness);
            for (int i = 0; i < src.Length; i++) dest[i] = table[src[i]];
        }

        static readonly byte[][] tables = new byte[256][];

        static byte[] BuildTable(byte brightness)
        {
            var t = new byte[256];
            for (int c = 0; c < 256; c++) t[c] = Hardware.LedOutputModel.ApplyBrightness((byte)c, brightness);
            return t;
        }
    }
}
