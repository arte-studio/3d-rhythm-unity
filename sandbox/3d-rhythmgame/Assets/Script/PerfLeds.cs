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
    // (RenderTextureSampler.cs によって初期化および毎フレーム更新されます)
    public byte[] perfLedData;

    // RenderTextureSampler.cs が perfLedData の初期化を行うため，
    // PerfLeds.cs 側での Start() や Awake() での初期化は必須ではありません．
    // (もし PerfLeds 側で独自に行う他の処理があれば，ここに追加します)

    // void Start()
    // {
    //     // (RenderTextureSampler が初期化するので，ここでは不要)
    // }

    // void Update()
    // {
    //     // (RenderTextureSampler が更新するので，ここでは不要)
    // }
}
