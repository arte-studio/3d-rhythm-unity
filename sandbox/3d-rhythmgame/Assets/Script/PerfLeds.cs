using UnityEngine;

/// <summary>
/// 演出用LEDストリップの最終的なバイトデータを格納するデータコンポーネント．
/// 
/// 役割:
/// 1. RenderTextureSampler が，このコンポーネントの 'perfLedData' にサンプリング結果を書き込む．
/// 2. UdpController が，このコンポーネントの 'perfLedData' を読み出してUDP送信する．
/// </summary>
public class PerfLeds : MonoBehaviour
{
    [Header("LED Data Buffer")]
    [Tooltip("全LEDストリップの最終的なRGBデータ (RenderTextureSamplerによって書き込まれ，UdpControllerによって読み出される)")]
    // この配列の構造: [物理ストリップID 0 のデータ (360 bytes)][物理ストリップID 1 のデータ (360 bytes)]...
    // 合計 90 ストリップ * 360 bytes/ストリップ = 32400 bytes
    // RGB 各色 1 byte として計算
    // 第1層: 0-29 ストリップ (0-10799 bytes)
    // 第2層: 30-59 ストリップ (10800-21599 bytes)
    // 第3層: 60-89 ストリップ (21600-32399 bytes)
    // 偶数ストリップ: 上から下の順で配列が並ぶ
    // 奇数ストリップ: 下から上の順で配列が並ぶ
    // 原点から時計回りにストリップIDが増加する配置
    // (RenderTextureSampler.cs によって初期化および毎フレーム更新されます)
    public byte[] perfLedData;

    // (RenderTextureSampler が初期化・更新を行う)
}