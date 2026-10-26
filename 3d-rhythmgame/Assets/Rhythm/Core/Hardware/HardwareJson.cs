using Newtonsoft.Json;

namespace Arte.Rhythm.Hardware
{
    /// <summary>
    /// hardware.json / setups/*.json の読み書き．
    /// Unity 側は TextAsset.text を，ツールや単体テストはファイルの中身をそのまま渡す．
    /// 知らないキーがあればエラーにして，綴りの間違いに気付けるようにしている．
    /// </summary>
    public static class HardwareJson
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Formatting = Formatting.Indented,
        };

        public static HardwareConfig LoadConfig(string json) => JsonConvert.DeserializeObject<HardwareConfig>(json, Settings);

        public static SetupProfile LoadSetup(string json) => JsonConvert.DeserializeObject<SetupProfile>(json, Settings);

        public static string Save(object value) => JsonConvert.SerializeObject(value, Settings);
    }
}
