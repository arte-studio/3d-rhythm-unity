using System;

namespace Arte.Rhythm.Charts
{
    /// <summary>
    /// 曲の周波数ごとの大きさの移り変わり（演出の「低い音で下から波」「周波数ごとの棒」用）．音声のサンプルから一度だけ求めておき，時刻で引く．
    ///
    /// 短い区間（WindowSec）ごとに FFT し，BandCount 本の帯（MinHz〜MaxHz を対数で等分．低い音が 0 番）の大きさ（dB）を求める．
    /// 帯ごとに，曲の中で大きい方（上位 5%）を 1，それより RangeDb 小さい音を 0 として 0〜1 にする（低い音と高い音で大きさの差があっても，どの帯もよく動く）．
    /// ただし一番大きい帯より MaxBoostDb 以上小さい帯は，その分だけ小さく出す．
    /// 上がるときは速く，下がるときはゆっくり追う（ReleaseSec）．
    /// </summary>
    public sealed class SpectrumEnvelope
    {
        public const int BandCount = 16;
        public const double WindowSec = 0.02;
        public const double MinHz = 40, MaxHz = 12000;
        public const double RangeDb = 30;
        /// <summary>帯ごとにそろえるとき，一番大きい帯よりこれ以上小さい帯は引き上げない（音がほとんど無い帯が 1 にならないように）．</summary>
        public const double MaxBoostDb = 36;
        const double ReleaseSec = 0.18;
        const int FftSize = 2048;

        readonly float[] bands;    // [区間 * BandCount + 帯]
        readonly int frames;

        public double LengthSec => frames * WindowSec;

        SpectrumEnvelope(float[] bands, int frames)
        {
            this.bands = bands;
            this.frames = frames;
        }

        /// <summary>帯 b の下端と上端の周波数（Hz）．</summary>
        public static (double lo, double hi) BandRange(int b)
        {
            double r = Math.Log(MaxHz / MinHz);
            return (MinHz * Math.Exp(r * b / BandCount), MinHz * Math.Exp(r * (b + 1) / BandCount));
        }

        /// <summary>interleaved: 全チャンネルを交互に並べたサンプル．startSec: 曲時刻 0 の音声ファイル上の位置．</summary>
        public static SpectrumEnvelope FromSamples(float[] interleaved, int channels, int rate, double startSec = 0)
        {
            channels = Math.Max(1, channels);
            int total = interleaved.Length / channels;
            int hop = Math.Max(1, (int)(WindowSec * rate));
            int first = (int)Math.Round(startSec * rate);
            int n = Math.Max(0, (total - Math.Max(0, first)) / hop);
            if (n == 0) return new SpectrumEnvelope(new float[0], 0);

            // 帯ごとの FFT の番号の範囲
            var binLo = new int[BandCount];
            var binHi = new int[BandCount];
            for (int b = 0; b < BandCount; b++)
            {
                var (lo, hi) = BandRange(b);
                binLo[b] = Math.Max(1, (int)Math.Floor(lo * FftSize / rate));
                binHi[b] = Math.Max(binLo[b] + 1, (int)Math.Ceiling(hi * FftSize / rate));
                binHi[b] = Math.Min(binHi[b], FftSize / 2);
            }
            var window = new double[FftSize];
            for (int i = 0; i < FftSize; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));
            var re = new double[FftSize];
            var im = new double[FftSize];
            var db = new double[n * BandCount];
            for (int w = 0; w < n; w++)
            {
                int center = first + w * hop + hop / 2;
                for (int i = 0; i < FftSize; i++)
                {
                    int f = center - FftSize / 2 + i;
                    double v = 0;
                    if (f >= 0 && f < total)
                        for (int c = 0; c < channels; c++) v += interleaved[f * channels + c];
                    re[i] = v / channels * window[i];
                    im[i] = 0;
                }
                Fft(re, im);
                for (int b = 0; b < BandCount; b++)
                {
                    double sum = 0;
                    for (int k = binLo[b]; k < binHi[b]; k++) sum += re[k] * re[k] + im[k] * im[k];
                    db[w * BandCount + b] = 10 * Math.Log10(sum / (binHi[b] - binLo[b]) + 1e-12);
                }
            }

            var bands = new float[n * BandCount];
            var col = new double[n];
            var tops = new double[BandCount];
            double release = Math.Exp(-WindowSec / ReleaseSec);
            for (int b = 0; b < BandCount; b++)
            {
                for (int w = 0; w < n; w++) col[w] = db[w * BandCount + b];
                Array.Sort(col);
                tops[b] = col[Math.Min(n - 1, (int)(n * 0.95))];
            }
            double loudest = double.MinValue;
            foreach (double t in tops) loudest = Math.Max(loudest, t);
            for (int b = 0; b < BandCount; b++)
            {
                for (int w = 0; w < n; w++) col[w] = db[w * BandCount + b];
                double top = Math.Max(tops[b], loudest - MaxBoostDb);
                double y = 0;
                for (int w = 0; w < n; w++)
                {
                    double x = Math.Max(0, Math.Min(1, (col[w] - (top - RangeDb)) / RangeDb));
                    y = x > y ? x : y * release + x * (1 - release);
                    bands[w * BandCount + b] = (float)y;
                }
            }
            return new SpectrumEnvelope(bands, n);
        }

        /// <summary>曲時刻 t の帯ごとの大きさ（0〜1）を output（長さ BandCount 以上）に書く．範囲外は 0．</summary>
        public void At(double t, float[] output)
        {
            if (frames == 0 || t < 0)
            {
                Array.Clear(output, 0, BandCount);
                return;
            }
            double i = t / WindowSec;
            int a = (int)i;
            if (a >= frames - 1)
            {
                for (int b = 0; b < BandCount; b++) output[b] = a < frames ? bands[a * BandCount + b] : 0;
                return;
            }
            float k = (float)(i - a);
            for (int b = 0; b < BandCount; b++)
            {
                float v0 = bands[a * BandCount + b], v1 = bands[(a + 1) * BandCount + b];
                output[b] = v0 + (v1 - v0) * k;
            }
        }

        // 長さが 2 のべき乗の FFT（その場で書き換える）
        static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                        double ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = ncr;
                    }
                }
            }
        }
    }
}
