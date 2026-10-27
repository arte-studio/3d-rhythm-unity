using System;
using System.Linq;

namespace Arte.Rhythm.Charts
{
    /// <summary>
    /// 曲の音量の移り変わり（演出の「音量に合わせて」用）．音声のサンプルから一度だけ求めておき，時刻で引く．
    ///
    /// 短い区間（WindowSec）ごとの音量（dB）を，曲の中で大きい方の音量（上位 5%）を 1，それより RangeDb 小さい音を 0 として 0〜1 にする．
    /// ぴくぴく動きすぎないよう，上がるときは速く，下がるときはゆっくり追う．
    /// </summary>
    public sealed class LoudnessEnvelope
    {
        public const double WindowSec = 0.02;
        public const double RangeDb = 24;
        const double ReleaseSec = 0.25;

        readonly float[] level;

        public double LengthSec => level.Length * WindowSec;

        LoudnessEnvelope(float[] level) => this.level = level;

        /// <summary>interleaved: 全チャンネルを交互に並べたサンプル．startSec: 曲時刻 0 の音声ファイル上の位置．</summary>
        public static LoudnessEnvelope FromSamples(float[] interleaved, int channels, int rate, double startSec = 0)
        {
            int frames = interleaved.Length / Math.Max(1, channels);
            int win = Math.Max(1, (int)(WindowSec * rate));
            int first = (int)Math.Round(startSec * rate);
            int n = Math.Max(0, (frames - Math.Max(0, first)) / win);
            var db = new double[n];
            for (int w = 0; w < n; w++)
            {
                double sum = 0;
                int s0 = first + w * win;
                for (int f = s0; f < s0 + win; f++)
                {
                    if (f < 0 || f >= frames) continue;
                    for (int c = 0; c < channels; c++)
                    {
                        double v = interleaved[f * channels + c];
                        sum += v * v;
                    }
                }
                db[w] = 10 * Math.Log10(sum / (win * channels) + 1e-12);
            }
            if (n == 0) return new LoudnessEnvelope(new float[0]);
            double top = db.OrderBy(x => x).ElementAt((int)(n * 0.95));
            var level = new float[n];
            double y = 0, release = Math.Exp(-WindowSec / ReleaseSec);
            for (int w = 0; w < n; w++)
            {
                double x = Math.Max(0, Math.Min(1, (db[w] - (top - RangeDb)) / RangeDb));
                y = x > y ? x : y * release + x * (1 - release);
                level[w] = (float)y;
            }
            return new LoudnessEnvelope(level);
        }

        /// <summary>曲時刻 t の音量（0〜1）．範囲外は 0．</summary>
        public float At(double t)
        {
            if (level.Length == 0 || t < 0) return 0;
            double i = t / WindowSec;
            int a = (int)i;
            if (a >= level.Length - 1) return a < level.Length ? level[a] : 0;
            return (float)(level[a] + (level[a + 1] - level[a]) * (i - a));
        }
    }
}
