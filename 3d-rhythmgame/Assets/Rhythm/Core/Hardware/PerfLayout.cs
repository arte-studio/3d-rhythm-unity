using System;
using System.Collections.Generic;

namespace Arte.Rhythm.Hardware
{
    /// <summary>演出用ストリップ1本の，物理位置と配線の情報．</summary>
    public readonly struct PerfStrip
    {
        /// <summary>演出キャンバス上の列番号（旧来の動画 90×120 の x 座標と同じ）．</summary>
        public readonly int Column;
        public readonly int Ring;
        /// <summary>リング内の角度番号（0 から angleCount-1）．</summary>
        public readonly int AngleIndex;
        public readonly float AngleDeg;
        public readonly float RadiusM;
        public readonly StripAddress Address;
        /// <summary>配線上の先頭 LED が上端にあるか（false なら下端から上へ並ぶ）．</summary>
        public readonly bool TopFirst;
        /// <summary>この設営で取り付けられているか．</summary>
        public readonly bool Installed;

        public PerfStrip(int column, int ring, int angleIndex, float angleDeg, float radiusM, StripAddress address, bool topFirst, bool installed)
        {
            Column = column;
            Ring = ring;
            AngleIndex = angleIndex;
            AngleDeg = angleDeg;
            RadiusM = radiusM;
            Address = address;
            TopFirst = topFirst;
            Installed = installed;
        }
    }

    /// <summary>
    /// 演出用 LED の配置と配線を，HardwareConfig と SetupProfile から計算したもの．
    ///
    /// 3つの座標の間を変換する:
    ///   キャンバス座標 (column, row)  … 演出を描く 2D の展開図．row = 0 が下端．
    ///   配線座標 (device, pin, slot, wireIndex) … UDP パケットとファームでの並び．
    ///   3D 座標 (x, y, z) [m]          … シミュレーションと 3D 演出の評価位置．
    ///
    /// 生成後は不変なので，複数スレッドから読んでよい．
    /// </summary>
    public sealed class PerfLayout
    {
        readonly PerfSection cfg;
        readonly PerfStrip[] strips;                  // 添字 = キャンバス列
        readonly Dictionary<StripAddress, int> columnOfAddress = new Dictionary<StripAddress, int>();

        public int ColumnCount => strips.Length;
        public int LedsPerStrip => cfg.ledsPerStrip;
        public int DeviceCount { get; }
        public int PinsPerDevice => cfg.pinsPerDevice;
        public int StripsPerPin => cfg.stripsPerPin;
        /// <summary>LED の間隔 [m]．</summary>
        public float LedPitchM => cfg.stripLengthM / cfg.ledsPerStrip;

        public PerfLayout(HardwareConfig config, SetupProfile setup)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            cfg = config.perf;
            DeviceCount = config.devices.count;
            Validate(cfg);

            var removed = new HashSet<StripAddress>(setup?.removedStrips ?? Array.Empty<StripAddress>());
            int columnCount = cfg.angleCount * cfg.pinsPerDevice;
            strips = new PerfStrip[columnCount];

            for (int pin = 0; pin < cfg.pinsPerDevice; pin++)
            {
                int ring = cfg.ringOfPin[pin];
                RingDef ringDef = cfg.rings[ring];
                for (int angle = 0; angle < cfg.angleCount; angle++)
                {
                    int device = angle / cfg.stripsPerPin;
                    int slot = angle % cfg.stripsPerPin;
                    int column = angle * cfg.pinsPerDevice + cfg.canvasColumnOffsetOfPin[pin];
                    var address = new StripAddress(device, pin, slot);
                    bool topFirst = (angle + cfg.topFirstParityOfPin[pin]) % 2 == 0;
                    float angleDeg = ringDef.angleOffsetDeg + 360f * angle / cfg.angleCount;
                    bool installed = !removed.Contains(address) && device < DeviceCount;

                    strips[column] = new PerfStrip(column, ring, angle, angleDeg, ringDef.radiusM, address, topFirst, installed);
                    columnOfAddress[address] = column;
                }
            }
        }

        static void Validate(PerfSection c)
        {
            if (c.ringOfPin.Length != c.pinsPerDevice) throw new FormatException("perf.ringOfPin の要素数が pinsPerDevice と一致しません");
            if (c.canvasColumnOffsetOfPin.Length != c.pinsPerDevice) throw new FormatException("perf.canvasColumnOffsetOfPin の要素数が pinsPerDevice と一致しません");
            if (c.topFirstParityOfPin.Length != c.pinsPerDevice) throw new FormatException("perf.topFirstParityOfPin の要素数が pinsPerDevice と一致しません");
            foreach (int r in c.ringOfPin)
                if (r < 0 || r >= c.rings.Length) throw new FormatException($"perf.ringOfPin に存在しないリング番号 {r} があります");
            var offsets = new HashSet<int>(c.canvasColumnOffsetOfPin);
            if (offsets.Count != c.pinsPerDevice) throw new FormatException("perf.canvasColumnOffsetOfPin に重複があります");
        }

        public PerfStrip GetStrip(int column) => strips[column];

        /// <summary>配線位置からキャンバス列を引く．存在しない位置なら -1．</summary>
        public int ColumnOf(StripAddress address) => columnOfAddress.TryGetValue(address, out int c) ? c : -1;

        /// <summary>キャンバスの行（0 = 下端）を，ストリップの配線上の LED 番号に変換する．</summary>
        public int WireIndexOf(int column, int row) => strips[column].TopFirst ? cfg.ledsPerStrip - 1 - row : row;

        /// <summary>LED の中心の 3D 座標 [m]．</summary>
        public void GetLedPosition(int column, int row, out float x, out float y, out float z)
        {
            PerfStrip s = strips[column];
            double rad = s.AngleDeg * Math.PI / 180.0;
            x = (float)(s.RadiusM * Math.Sin(rad));
            z = (float)(s.RadiusM * Math.Cos(rad));
            y = cfg.bottomYM + LedPitchM * (row + 0.5f);
        }

        /// <summary>
        /// 1デバイス・1ピン分の LED データを，キャンバスから配線順に詰める．
        ///
        /// canvasRgb: キャンバス全体の RGB（1 LED 3 バイト）．並びは [column][row]，つまり
        ///            列ごとに row = 0（下端）から上へ ledsPerStrip 個．
        /// dest: 書き込み先．(slot の数) * ledsPerStrip * 3 バイト以上．外したストリップは 0 で埋める．
        /// 戻り値: 送るべきバイト数（その device/pin に存在する最後のストリップまで）．
        /// </summary>
        public int PackPin(ReadOnlySpan<byte> canvasRgb, int device, int pin, Span<byte> dest)
        {
            int stripBytes = cfg.ledsPerStrip * 3;
            int written = 0;
            for (int slot = 0; slot < cfg.stripsPerPin; slot++)
            {
                int column = ColumnOf(new StripAddress(device, pin, slot));
                if (column < 0) break;   // この device にはもうストリップがない（例: 最後のデバイスは 2 本）

                Span<byte> outStrip = dest.Slice(slot * stripBytes, stripBytes);
                PerfStrip s = strips[column];
                if (!s.Installed)
                {
                    outStrip.Clear();
                }
                else
                {
                    ReadOnlySpan<byte> src = canvasRgb.Slice(column * stripBytes, stripBytes);
                    for (int row = 0; row < cfg.ledsPerStrip; row++)
                    {
                        int w = (s.TopFirst ? cfg.ledsPerStrip - 1 - row : row) * 3;
                        outStrip[w] = src[row * 3];
                        outStrip[w + 1] = src[row * 3 + 1];
                        outStrip[w + 2] = src[row * 3 + 2];
                    }
                }
                written = (slot + 1) * stripBytes;
            }
            return written;
        }
    }
}
