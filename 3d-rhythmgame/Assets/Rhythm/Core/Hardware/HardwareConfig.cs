using System;

namespace Arte.Rhythm.Hardware
{
    /// <summary>
    /// 設営に依存しない，ハードウェアそのものの構成（寸法・配線・LED数・出力特性）．
    ///
    /// Assets/Rhythm/Data/Hardware/hardware.json から HardwareJson.LoadConfig で読み込む．
    /// ゲーム・演出・シミュレーション・UDP送信はすべてこのデータから配置や配線を引くので，
    /// 定数をコードに直接書かないこと．設営ごとに変わるもの（外したストリップ，レーン割り当て，明るさ）は
    /// SetupProfile に置く．
    ///
    /// 座標系: Unity と同じ（Y が上，単位はメートル）．角度は真上から見て +Z 方向を 0°，時計回り（+X 方向）を正とする．
    /// </summary>
    [Serializable]
    public sealed class HardwareConfig
    {
        /// <summary>このファイル形式の版．形式を変えたら上げる．</summary>
        public int version = 1;
        public DeviceSection devices = new DeviceSection();
        public PerfSection perf = new PerfSection();
        public NoteSection noteModules = new NoteSection();
        public OutputSection output = new OutputSection();
    }

    /// <summary>ESP32 デバイスと UDP 通信の設定．</summary>
    [Serializable]
    public sealed class DeviceSection
    {
        /// <summary>デバイス台数（ID は 0 から count-1）．</summary>
        public int count = 8;
        /// <summary>ESP32 が待ち受けるポート（PC → ESP32 の LED データ）．</summary>
        public int devicePort = 8888;
        /// <summary>PC が待ち受けるポート（ESP32 → PC のタッチ入力・ACK）．</summary>
        public int hostPort = 9999;
    }

    /// <summary>
    /// 演出用 LED（円筒状に並んだテープ LED）の寸法と配線．
    ///
    /// 構造: 半径の違う同心円（リング）が複数あり，各リングに angleCount 本のストリップが等間隔で立つ．
    /// 配線: デバイス d の出力ピン pin に stripsPerPin 本のストリップが直列につながり，
    ///       slot 番目のストリップは角度番号 angleIndex = d * stripsPerPin + slot の位置にある．
    /// </summary>
    [Serializable]
    public sealed class PerfSection
    {
        /// <summary>ストリップ1本あたりの LED 数．</summary>
        public int ledsPerStrip = 120;
        /// <summary>ストリップ1本の長さ [m]（2m・60LED/m）．</summary>
        public float stripLengthM = 2f;
        /// <summary>ストリップ下端（最下段 LED の外側の端）の床からの高さ [m]．</summary>
        public float bottomYM = 0f;
        /// <summary>1リングあたりのストリップ本数（角度方向の分割数）．</summary>
        public int angleCount = 30;
        /// <summary>リングの定義．並びは内側から外側．</summary>
        public RingDef[] rings = Array.Empty<RingDef>();
        /// <summary>1デバイスが演出用に使う出力ピン数．ピン番号は UDP パケットの type（0 から）と同じ．</summary>
        public int pinsPerDevice = 3;
        /// <summary>1ピンに直列につながるストリップ本数．</summary>
        public int stripsPerPin = 4;
        /// <summary>ピンごとに，そのピンがつながっているリングの番号（rings の添字）．</summary>
        public int[] ringOfPin = Array.Empty<int>();
        /// <summary>
        /// 演出キャンバス（横 = ストリップ，縦 = LED）での列番号の付け方．
        /// 列 = angleIndex * pinsPerDevice + canvasColumnOffsetOfPin[pin]．
        /// 旧来の動画（90×120）と同じ並びにするための値．
        /// </summary>
        public int[] canvasColumnOffsetOfPin = Array.Empty<int>();
        /// <summary>
        /// ジグザグ配線の向き．(angleIndex + topFirstParityOfPin[pin]) が偶数のストリップは
        /// 配線上の先頭 LED が上端，奇数のストリップは下端にある．
        /// </summary>
        public int[] topFirstParityOfPin = Array.Empty<int>();
        /// <summary>出典や未確認事項のメモ（読み込み時には使わない）．</summary>
        public string notes = "";
    }

    /// <summary>1つのリング（同心円）．</summary>
    [Serializable]
    public sealed class RingDef
    {
        public string name = "";
        /// <summary>中心からストリップまでの水平距離 [m]．</summary>
        public float radiusM;
        /// <summary>角度番号 0 のストリップの角度 [deg]．</summary>
        public float angleOffsetDeg;
    }

    /// <summary>
    /// ノーツ用モジュール（タッチ判定付きの 8×8 マトリクス「ムギュ」と，Connect 用の LED テープ）．
    /// 各デバイスの noteOutputPin に，モジュールが modulesPerDevice 個直列につながる．
    /// モジュール内の LED の並びは [ムギュ muguLeds 個][Connect connectLeds 個]．
    /// </summary>
    [Serializable]
    public sealed class NoteSection
    {
        public int modulesPerDevice = 5;
        public int muguColumns = 8;
        public int muguRows = 8;
        /// <summary>むぎゅっとの端の LED の中心から反対の端の LED の中心まで（m）．旧版の LEDMatrix は一辺約 6cm．</summary>
        public float muguSizeM = 0.056f;
        public int connectLeds = 30;
        /// <summary>モジュール1個分の LED 数（ムギュ + Connect）．配線上の先頭位置の間隔になる．</summary>
        public int moduleStrideLeds = 94;
        /// <summary>ノーツ用 LED がつながる出力ピン（UDP パケット type）．</summary>
        public int noteOutputPin = 3;
        /// <summary>ノーツ用パケットの LED 数（未使用分は黒で埋める）．</summary>
        public int notePacketLeds = 480;
        /// <summary>Connect 用テープの色順がムギュと違い，R と G を入れ替えて送る必要があるか．</summary>
        public bool connectSwapRG = true;
        public string notes = "";
    }

    /// <summary>LED の出力特性．シミュレーションと実機で同じ式を使うためにここに置く．</summary>
    [Serializable]
    public sealed class OutputSection
    {
        /// <summary>LED 1個の最大電流時の相対的な明るさの比（R, G, B）．Viewer の発光強度の基準．</summary>
        public float[] channelLuminance = { 1f, 1f, 1f };
        /// <summary>LED に送った値 → 光量の曲線の指数（LedOutputModel.OutputToLinear）．1 = 比例（PWM のデューティ比どおり．未計測）．</summary>
        public float firmwareGamma = 1f;
        public string notes = "";
    }
}
