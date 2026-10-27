using System.Collections.Generic;
using System.IO;
using Arte.Rhythm.Hardware;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arte.Rhythm.Editor
{
    /// <summary>
    /// 旧版の配置ファイル（Assets/Resources/object_positions_*.json）から，設営データ（setups/*.json）の modules を作る．
    /// レーンとの対応は旧版に無いので仮（ModuleGeometry.FromLegacy）．実際の置き場所が分かったら設営データを直接直す．
    /// コマンドライン: -executeMethod Arte.Rhythm.Editor.ModulePlacementImporter.ImportDefault
    /// </summary>
    public static class ModulePlacementImporter
    {
        const string HardwarePath = "Assets/Rhythm/Data/Hardware/hardware.json";
        const string DefaultSetup = "Assets/Rhythm/Data/Hardware/setups/kodaisai-1101.json";
        const string DefaultLegacy = "Assets/Resources/object_positions_kokasai.json";

        [System.Serializable]
        class LegacyFile
        {
            public List<LegacyRow> objects;
        }

        [System.Serializable]
        class LegacyRow
        {
            public string prefabName;
            public float x, y, z, rx, ry, rz;
        }

        [MenuItem("Tools/Arte Rhythm/Import Module Placement (kodaisai-1101 ← object_positions_kokasai)")]
        public static void ImportDefault() => Import(DefaultSetup, DefaultLegacy);

        public static void Import(string setupPath, string legacyPath)
        {
            var config = HardwareJson.LoadConfig(File.ReadAllText(HardwarePath));
            string setupText = File.ReadAllText(setupPath);
            var setup = HardwareJson.LoadSetup(setupText);
            var legacy = JsonUtility.FromJson<LegacyFile>(File.ReadAllText(legacyPath));
            var objects = new List<LegacyPlacedObject>();
            foreach (var o in legacy.objects)
                objects.Add(new LegacyPlacedObject { prefabName = o.prefabName, x = o.x, y = o.y, z = o.z, rx = o.rx, ry = o.ry, rz = o.rz });

            var placements = ModuleGeometry.FromLegacy(objects, new PerfLayout(config, setup), new NoteLayout(config, setup));

            // ほかの項目の書き方を崩さないよう，modules だけ差し替える
            var root = JObject.Parse(setupText);
            var array = new JArray();
            foreach (var p in placements)
            {
                array.Add(new JObject
                {
                    ["lane"] = p.lane,
                    ["mugyu"] = Round(p.mugyu),
                    ["connectFrom"] = Round(p.connectFrom),
                    ["connectTo"] = Round(p.connectTo),
                });
            }
            root["modules"] = array;
            root["modulesNote"] = $"仮配置: {Path.GetFileName(legacyPath)}（旧版のシーンの配置）から ModulePlacementImporter で作った．" +
                                  "旧版はレーンと置き場所の対応を持っていなかったので，各レーンのモジュールがつながるデバイスの演出用ストリップに近い場所を割り当てた．" +
                                  "実際の置き場所と，つながるノーツ用テープの向き（0 番目の LED の端）は未確認．";
            File.WriteAllText(setupPath, root.ToString(Formatting.Indented) + "\n");
            AssetDatabase.ImportAsset(setupPath);
            Debug.Log($"[ModulePlacementImporter] {setupPath} に {placements.Count} 個");
        }

        static JArray Round(float[] v) => new JArray(System.Math.Round(v[0], 4), System.Math.Round(v[1], 4), System.Math.Round(v[2], 4));
    }
}
