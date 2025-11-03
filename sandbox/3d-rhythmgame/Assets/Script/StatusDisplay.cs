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
    // フレームレート平滑化用 (指数移動平均)
    private float fpsAverage = 0f;

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
        float boxHeight = 250f; // 9行 * 20px + タイトル(20px) + 余白(10px)
        
        // 横幅も "First: ...s ago. LastTouch: ...s ago" が収まるように広げます
        float boxWidth = 800f; 

        // 画面の左上 (10, 10) の位置にボックスを描画
        GUI.Box(new Rect(10, 10, boxWidth, boxHeight), "Current Status");
        
        // ボックスの中身 (ラベル)
        float labelX = 10 + 10; // Boxの左端(10) + 内側パディング(10)
        float labelY = 10 + 20; // Boxの上端(10) + タイトル分の高さ(20)
        float labelWidth = boxWidth - 20; // Box幅 - 左右パディング(10*2)
        float labelHeight = boxHeight - 30; // Box高 - タイトル分(20) - 下部パディング(10)

    // FPS を更新 (unscaledDeltaTime を使って timeScale の影響を排除)
    float currentFps = 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
    if (fpsAverage <= 0f) fpsAverage = currentFps;
    fpsAverage = Mathf.Lerp(fpsAverage, currentFps, 0.1f);

    // UDP 情報を付与
    string udpInfo = "";
    string fpsInfo = string.Format("FPS: {0:F1}", fpsAverage);
        if (udpController == null)
            udpController = FindObjectOfType<UdpController>();

        if (udpController != null)
        {
            udpInfo = string.Format("\n\nUDP: {0:F1} fps (last send {1:F2}s ago)", udpController.CurrentUdpFps, udpController.TimeSinceLastSend);
        }

        // ラベルを描画
        // UdpController側で <color> タグを使用しているため、
        // デフォルトのGUIStyle (richText=true) で自動的に色付きで描画されます
        GUI.Label(new Rect(labelX, labelY, labelWidth, labelHeight), statusText + "\n\n" + fpsInfo + udpInfo);

        // -----------------------------
        // 画面中央下にゲーム操作ボタンとスコアを表示
        // -----------------------------
        var gm = GameManager.Instance;
        string scoreText = "Score: N/A";
        if (gm != null)
        {
            scoreText = $"Score: {gm.CurrentScore}";
        }

        // ボタンサイズと位置
        int btnW = 120;
        int btnH = 40;
        int spacing = 12;
        int totalW = btnW * 3 + spacing * 2;
        float startX = (Screen.width - totalW) / 2f;
        float startY = Screen.height - btnH - 20f;

        // スコア表示（ボタンのすぐ上）
        var scoreRect = new Rect(startX, startY - 30f, totalW, 24f);
        GUI.Box(scoreRect, scoreText);

        // Start / Pause / Reset ボタン
        if (GUI.Button(new Rect(startX, startY, btnW, btnH), gm != null && gm.IsPlaying && !gm.IsPaused ? "Playing" : "Start"))
        {
            if (gm != null) gm.StartGame();
        }

        if (GUI.Button(new Rect(startX + btnW + spacing, startY, btnW, btnH), "Pause"))
        {
            if (gm != null) gm.PauseGame();
        }

        if (GUI.Button(new Rect(startX + (btnW + spacing) * 2, startY, btnW, btnH), "Reset"))
        {
            if (gm != null) gm.ResetGame();
        }
    }
}
