using System;

namespace Arte.Rhythm.Hardware
{
    /// <summary>
    /// 設営ごとに変わる構成（どのモジュールを付けたか，どのストリップを外したか，明るさ）．
    ///
    /// Assets/Rhythm/Data/Hardware/setups/*.json から HardwareJson.LoadSetup で読み込む．
    /// 同じハードでも会場ごとに入口の位置やモジュール数が違うので，HardwareConfig とは分けて持つ．
    /// 譜面エディタはこのデータを見て「付いていないレーンへのノーツ」を警告する．
    /// </summary>
    [Serializable]
    public sealed class SetupProfile
    {
        public string name = "";
        public string description = "";
        /// <summary>外したストリップ（入口など）．データは送らず，シミュレーションでも表示しない．</summary>
        public StripAddress[] removedStrips = Array.Empty<StripAddress>();
        /// <summary>ゲーム内レーン番号と，そのレーンのモジュールの対応．ここにないレーンは実機に存在しない．</summary>
        public LaneAssignment[] lanes = Array.Empty<LaneAssignment>();
        /// <summary>演出用 LED の明るさ（0-255，PC が (c * (b + 1)) >> 8 として掛けてから送る）．</summary>
        public int brightnessPerf = 50;
        /// <summary>ノーツ用 LED の明るさ（0-255）．</summary>
        public int brightnessNote = 50;
        /// <summary>色を LED に送る値にする曲線．"linear"（旧版と同じ）か "srgb"（LedOutputModel.OutputCurve）．</summary>
        public string outputCurve = "linear";
        /// <summary>ノーツモジュールの置き場所（シミュレーション表示用．無いレーンは表示しない）．</summary>
        public ModulePlacement[] modules = Array.Empty<ModulePlacement>();
        /// <summary>置き場所の出どころなどのメモ．</summary>
        public string modulesNote = "";
    }

    /// <summary>配線上のストリップの位置（デバイス・ピン・ピン内の何本目か）．</summary>
    [Serializable]
    public struct StripAddress : IEquatable<StripAddress>
    {
        public int device;
        public int pin;
        public int slot;

        public StripAddress(int device, int pin, int slot)
        {
            this.device = device;
            this.pin = pin;
            this.slot = slot;
        }

        public bool Equals(StripAddress other) => device == other.device && pin == other.pin && slot == other.slot;
        public override bool Equals(object obj) => obj is StripAddress other && Equals(other);
        public override int GetHashCode() => (device * 31 + pin) * 31 + slot;
        public override string ToString() => $"(device {device}, pin {pin}, slot {slot})";
    }

    /// <summary>ノーツ用モジュールの位置（デバイスと，そのデバイス内の何個目か）．</summary>
    [Serializable]
    public struct ModuleAddress : IEquatable<ModuleAddress>
    {
        public int device;
        public int module;

        public ModuleAddress(int device, int module)
        {
            this.device = device;
            this.module = module;
        }

        public bool Equals(ModuleAddress other) => device == other.device && module == other.module;
        public override bool Equals(object obj) => obj is ModuleAddress other && Equals(other);
        public override int GetHashCode() => device * 31 + module;
        public override string ToString() => $"(device {device}, module {module})";
    }

    /// <summary>レーン1本分の割り当て．</summary>
    [Serializable]
    public struct LaneAssignment
    {
        public int lane;
        public int device;
        public int module;
    }
}
