using UnityEngine;

/// <summary>
/// ゲーム全体の表示モードと、モードが切り替わるまでの時間を管理する。
/// シーンに一つだけ配置する空のGameObjectにアタッチする。
/// </summary>
public class GameManager : MonoBehaviour
{
    // 表示モードを定義する
    public enum DisplayMode
    {
        Calm,        // モード1: 穏やかな星
        Strobe,      // モード2: 激しいストロボ
        FallingBall  // ★ モード3: ボール落下
    }

    // 現在のモード（他のスクリプトから参照できるように static にする）
    public static DisplayMode CurrentMode { get; private set; }

    [Header("モード設定")]
    [Tooltip("有効にすると、指定時間でモードが自動的に切り替わります")]
    public bool enableModeSwitching = true;
    [Tooltip("自動切り替えが無効な場合、またはゲーム開始時の初期モード")]
    public DisplayMode initialMode = DisplayMode.Calm;
    [Tooltip("モードが切り替わるまでの時間（秒）")]
    public float modeDuration = 15.0f;

    [Header("参照")]
    [Tooltip("シーン内の StarSpawner オブジェクト")]
    public StarSpawner starSpawner;
    [Tooltip("シーン内の BallSpawner オブジェクト")]
    public BallSpawner ballSpawner;

    private float modeTimer; // モード切り替え用のタイマー

    void Start()
    {
        if (starSpawner == null || ballSpawner == null)
        {
            UnityEngine.Debug.LogError("GameManagerにStarSpawnerまたはBallSpawnerが設定されていません。");
            return;
        }

        // 初期モードを設定
        CurrentMode = initialMode;
        UpdateModeFeatures();
        modeTimer = modeDuration;

        // 最初の星を生成する
        starSpawner.RespawnStars();
    }

    void Update()
    {
        if (enableModeSwitching)
        {
            modeTimer -= Time.deltaTime;

            if (modeTimer <= 0)
            {
                SwitchMode();
                modeTimer = modeDuration;
            }
        }
    }

    /// <summary>
    /// モードを順番に切り替える
    /// </summary>
    void SwitchMode()
    {
        // ★ 3つのモードを順番に切り替える
        switch (CurrentMode)
        {
            case DisplayMode.Calm:
                CurrentMode = DisplayMode.Strobe;
                break;
            case DisplayMode.Strobe:
                CurrentMode = DisplayMode.FallingBall;
                break;
            case DisplayMode.FallingBall:
                CurrentMode = DisplayMode.Calm;
                break;
        }

        UpdateModeFeatures();
        starSpawner.RespawnStars();
    }

    /// <summary>
    /// 現在のモードに応じて、BallSpawnerなどの機能を有効/無効にする
    /// </summary>
    void UpdateModeFeatures()
    {
        switch (CurrentMode)
        {
            case DisplayMode.Calm:
                ballSpawner.gameObject.SetActive(false); // ボール落下は無効
                break;
            case DisplayMode.Strobe:
                ballSpawner.gameObject.SetActive(true);  // ボール落下を有効化
                break;
            case DisplayMode.FallingBall:
                ballSpawner.gameObject.SetActive(true);  // ボール落下を有効化
                break;
        }
    }
}

