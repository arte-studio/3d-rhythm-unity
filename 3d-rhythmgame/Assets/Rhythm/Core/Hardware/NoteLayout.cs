using System;
using System.Collections.Generic;

namespace Arte.Rhythm.Hardware
{
    /// <summary>
    /// ノーツ用モジュールの配線と，ゲーム内レーンとの対応を HardwareConfig と SetupProfile から計算したもの．
    ///
    /// ゲームは「レーン番号」，ファームとタッチ入力は「(デバイス, モジュール番号)」で考えるので，その間を変換する．
    /// ノーツ用パケット内の LED 番号もここで計算する（モジュール m の先頭は m * moduleStrideLeds）．
    /// </summary>
    public sealed class NoteLayout
    {
        readonly NoteSection cfg;
        readonly int deviceCount;
        readonly Dictionary<int, ModuleAddress> moduleOfLane = new Dictionary<int, ModuleAddress>();
        readonly Dictionary<ModuleAddress, int> laneOfModule = new Dictionary<ModuleAddress, int>();
        readonly int[] lanes;

        public int MuguLeds => cfg.muguColumns * cfg.muguRows;
        public int ConnectLeds => cfg.connectLeds;
        public int ModulesPerDevice => cfg.modulesPerDevice;
        public int NoteOutputPin => cfg.noteOutputPin;
        public int NotePacketLeds => cfg.notePacketLeds;
        public bool ConnectSwapRG => cfg.connectSwapRG;
        public int MuguRows => cfg.muguRows;
        public int MuguColumns => cfg.muguColumns;
        public float MuguSizeM => cfg.muguSizeM;
        /// <summary>この設営で使えるレーン番号（昇順）．</summary>
        public IReadOnlyList<int> Lanes => lanes;

        public NoteLayout(HardwareConfig config, SetupProfile setup)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            cfg = config.noteModules;
            deviceCount = config.devices.count;
            if (cfg.moduleStrideLeds < MuguLeds + cfg.connectLeds)
                throw new FormatException("noteModules.moduleStrideLeds がムギュと Connect の LED 数の合計より小さいです");
            if (cfg.moduleStrideLeds * cfg.modulesPerDevice > cfg.notePacketLeds)
                throw new FormatException("noteModules.notePacketLeds にモジュールが収まりません");

            foreach (LaneAssignment a in setup?.lanes ?? Array.Empty<LaneAssignment>())
            {
                var m = new ModuleAddress(a.device, a.module);
                if (a.device < 0 || a.device >= deviceCount || a.module < 0 || a.module >= cfg.modulesPerDevice)
                    throw new FormatException($"レーン {a.lane} の割り当て {m} は存在しないモジュールです");
                if (moduleOfLane.ContainsKey(a.lane)) throw new FormatException($"レーン {a.lane} が二重に割り当てられています");
                if (laneOfModule.ContainsKey(m)) throw new FormatException($"モジュール {m} が二重に割り当てられています");
                moduleOfLane[a.lane] = m;
                laneOfModule[m] = a.lane;
            }
            lanes = new List<int>(moduleOfLane.Keys).ToArray();
            Array.Sort(lanes);
            foreach (var p in setup?.modules ?? Array.Empty<ModulePlacement>()) placements[p.lane] = p;
        }

        readonly Dictionary<int, ModulePlacement> placements = new Dictionary<int, ModulePlacement>();

        /// <summary>レーンのモジュールの置き場所（設営データに無ければ false）．</summary>
        public bool TryGetPlacement(int lane, out ModulePlacement placement) => placements.TryGetValue(lane, out placement);

        /// <summary>
        /// 正面（リングの中心から +z の向き．VR では「リングの中心に戻る」で向く方）に一番近いむぎゅっとのレーン．置き場所が無ければ -1．
        /// </summary>
        public int FrontLane
        {
            get
            {
                int best = -1;
                double bestAngle = double.MaxValue;
                foreach (int lane in lanes)
                {
                    if (!placements.TryGetValue(lane, out var p) || p.mugyu == null || p.mugyu.Length < 3) continue;
                    double angle = Math.Abs(Math.Atan2(p.mugyu[0], p.mugyu[2]));
                    if (angle < bestAngle) { bestAngle = angle; best = lane; }
                }
                return best;
            }
        }

        public bool HasLane(int lane) => moduleOfLane.ContainsKey(lane);

        /// <summary>レーンのモジュールを引く．この設営にないレーンなら false．</summary>
        public bool TryGetModule(int lane, out ModuleAddress module) => moduleOfLane.TryGetValue(lane, out module);

        /// <summary>モジュール（タッチ入力の送り元）からレーンを引く．割り当てがなければ -1．</summary>
        public int LaneOf(ModuleAddress module) => laneOfModule.TryGetValue(module, out int lane) ? lane : -1;

        /// <summary>ノーツ用パケット内での，ムギュの (row, column) の LED 番号．</summary>
        public int MuguLedIndex(int module, int row, int column) => module * cfg.moduleStrideLeds + row * cfg.muguColumns + column;

        /// <summary>ノーツ用パケット内での，Connect テープの i 番目の LED 番号．</summary>
        public int ConnectLedIndex(int module, int i) => module * cfg.moduleStrideLeds + MuguLeds + i;
    }
}
