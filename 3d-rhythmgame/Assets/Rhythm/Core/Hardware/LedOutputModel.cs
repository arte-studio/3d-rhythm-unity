using System;

namespace Arte.Rhythm.Hardware
{
    /// <summary>
    /// PC が送った 8bit の色が，実機の LED でどれだけの明るさになるかのモデル．
    ///
    /// PC は送る前に明るさ b を (c * (b + 1)) >> 8 で掛け（FrameEncoder），ファームは届いた値をそのまま LED に出す．
    /// シミュレーションも同じ式を通すことで，暗い色が潰れる・段差が出るといった実機の見え方を再現する．
    /// </summary>
    public static class LedOutputModel
    {
        /// <summary>
        /// 演出の色（0-255）を LED に送る値にするときの曲線．
        /// Linear: そのまま比例させる（旧版と同じ）．LED の光量は送った値にほぼ比例し，人の目は暗い所の差に敏感なので，
        ///         画面で見た絵より中間の明るさが明るく（白っぽく）見える．
        /// Srgb:   色を画面用の値（sRGB）とみなし，光量に直してから送る．LED の見た目が画面の絵に近づくが，
        ///         明るさを下げていると暗い色が 0 や 1 に潰れやすい．
        /// </summary>
        public enum OutputCurve { Linear, Srgb }

        /// <summary>設営データの文字列（"linear" / "srgb"）から．書いていなければ Srgb（2026-10-28 から既定）．</summary>
        public static OutputCurve ParseCurve(string s) =>
            string.Equals(s, "linear", StringComparison.OrdinalIgnoreCase) ? OutputCurve.Linear : OutputCurve.Srgb;

        /// <summary>
        /// 演出の色を，曲線と明るさを掛けて LED に送る値にする．Linear なら ApplyBrightness と同じ（旧版と同じ見え方）．
        /// Srgb は光量に直してから，Linear の最大値（白）と同じになるよう明るさを掛け，最後に 1 回だけ四捨五入する．
        /// </summary>
        public static byte Encode(byte content, int brightness, OutputCurve curve)
        {
            if (curve == OutputCurve.Linear) return ApplyBrightness(content, brightness);
            double v = SrgbToLinear(content / 255.0) * ApplyBrightness(255, brightness);
            return (byte)Math.Round(v, MidpointRounding.AwayFromZero);
        }

        /// <summary>sRGB の値（0-1）を光量（0-1）にする（IEC 61966-2-1）．</summary>
        public static double SrgbToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

        /// <summary>
        /// LED に送った値（明るさを掛けた後）が出す相対的な光量（0-1）．シミュレーションの表示はこれを光の強さとして使う
        /// （画面への出し方は Unity のリニア色空間とトーンマップに任せる）．
        /// </summary>
        public static float OutputToLinear(byte output, float firmwareGamma = 1f)
        {
            float pwm = output / 255f;
            return firmwareGamma == 1f ? pwm : (float)Math.Pow(pwm, firmwareGamma);
        }

        /// <summary>明るさを掛ける（送信もシミュレーションもこれを使う）．戻り値が LED に実際に出る PWM 値（0-255）．</summary>
        public static byte ApplyBrightness(byte value, int brightness)
        {
            if (brightness < 0) brightness = 0;
            else if (brightness > 255) brightness = 255;
            return (byte)((value * (brightness + 1)) >> 8);
        }

        /// <summary>
        /// LED の相対的な発光量（0-1）．WS2812 の出力は PWM のデューティ比に比例するので，
        /// PWM 値に firmwareGamma を掛けた値をそのまま線形の光量として扱う．
        /// </summary>
        public static float EmittedLinear(byte value, int brightness, float firmwareGamma = 1f) =>
            OutputToLinear(ApplyBrightness(value, brightness), firmwareGamma);

        /// <summary>明るさ設定で潰れずに残る階調の数（例: b=50 なら 0-50 の 51 段）．</summary>
        public static int DistinctLevels(int brightness) => ApplyBrightness(255, brightness) + 1;
    }
}
