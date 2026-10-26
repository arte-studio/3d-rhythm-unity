using System;

namespace Arte.Rhythm.Hardware
{
    /// <summary>
    /// PC が送った 8bit の色が，実機の LED でどれだけの明るさになるかのモデル．
    ///
    /// 実機（ファーム）は受け取った値に明るさ b を (c * (b + 1)) >> 8 で掛けてから LED に出す．
    /// シミュレーションも同じ式を通すことで，暗い色が潰れる・段差が出るといった実機の見え方を再現する．
    /// </summary>
    public static class LedOutputModel
    {
        /// <summary>ファームと同じ整数演算で明るさを掛ける．戻り値が LED に実際に出る PWM 値（0-255）．</summary>
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
        public static float EmittedLinear(byte value, int brightness, float firmwareGamma = 1f)
        {
            float pwm = ApplyBrightness(value, brightness) / 255f;
            return firmwareGamma == 1f ? pwm : (float)Math.Pow(pwm, firmwareGamma);
        }

        /// <summary>明るさ設定で潰れずに残る階調の数（例: b=50 なら 0-50 の 51 段）．</summary>
        public static int DistinctLevels(int brightness) => ApplyBrightness(255, brightness) + 1;
    }
}
