using UnityEngine;

/// <summary>
/// UdpController から受け取った statusText を IMGUI で画面に表示するクラス
/// </summary>
public class StatusDisplay : MonoBehaviour
{
    // UdpController からこの値が毎フレーム更新されます
    public string statusText = "Initialized";

    // Inspectorで表示/非表示を切り替える
    public bool showStatus = true; 

    // UdpController の参照をキャッシュ
    private UdpController udpController;

    /// <summary>
    /// IMGUIを描画するためのメソッド (毎フレーム呼ばれる)
    /// </summary>
    void OnGUI()
    {
        if (!showStatus)
        {
            return;
        }

        // UdpController が 8台 + ヘッダー1行 = 9行 のテキストを生成します。
        // それらが収まるように、BoxとLabelのサイズを調整します。
        
        // 1行あたり約20ピクセルと仮定し、マージンを含めて高さを決定
        float boxHeight = 210f; // 9行 * 20px + タイトル(20px) + 余白(10px)
        
        // 横幅も "First: ...s ago. LastTouch: ...s ago" が収まるように広げます
        float boxWidth = 500f; 

        // 画面の左上 (10, 10) の位置にボックスを描画
        GUI.Box(new Rect(10, 10, boxWidth, boxHeight), "Current Status");
        
        // ボックスの中身 (ラベル)
        float labelX = 10 + 10; // Boxの左端(10) + 内側パディング(10)
        float labelY = 10 + 20; // Boxの上端(10) + タイトル分の高さ(20)
        float labelWidth = boxWidth - 20; // Box幅 - 左右パディング(10*2)
        float labelHeight = boxHeight - 30; // Box高 - タイトル分(20) - 下部パディング(10)

        // UDP 情報を付与
        string udpInfo = "";
        if (udpController == null)
            udpController = FindObjectOfType<UdpController>();

        if (udpController != null)
        {
            udpInfo = string.Format("\n\nUDP: {0:F1} fps (last send {1:F2}s ago)", udpController.CurrentUdpFps, udpController.TimeSinceLastSend);
        }

        // ラベルを描画
        // UdpController側で <color> タグを使用しているため、
        // デフォルトのGUIStyle (richText=true) で自動的に色付きで描画されます
        GUI.Label(new Rect(labelX, labelY, labelWidth, labelHeight), statusText + udpInfo);
    }
}
