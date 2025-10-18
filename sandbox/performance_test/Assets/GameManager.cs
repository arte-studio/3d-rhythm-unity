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
        Calm,   // モード1: 穏やかなモード
        Strobe  // モード2: 激しいモード
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
    public BallSpawner ballSpawner; // ★ 追加

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
        UpdateModeFeatures(); // ★ モードに応じた機能を有効/無効化
        modeTimer = modeDuration;

        // 最初の星を生成する
        starSpawner.RespawnStars();
    }

    void Update()
    {
        // モード自動切り替えが有効な場合のみタイマーを処理
        if (enableModeSwitching)
        {
            modeTimer -= Time.deltaTime;

            if (modeTimer <= 0)
            {
                SwitchMode();
                modeTimer = modeDuration; // タイマーをリセット
            }
        }
    }

    /// <summary>
    /// モードを切り替える
    /// </summary>
    void SwitchMode()
    {
        // 現在のモードに応じて次のモードを決定
        CurrentMode = (CurrentMode == DisplayMode.Calm) ? DisplayMode.Strobe : DisplayMode.Calm;

        UpdateModeFeatures(); // ★ モードに応じた機能を有効/無効化
        starSpawner.RespawnStars();
    }

    /// <summary>
    /// ★ 現在のモードに応じて、BallSpawnerなどの機能を有効/無効にする
    /// </summary>
    void UpdateModeFeatures()
    {
        switch (CurrentMode)
        {
            case DisplayMode.Calm:
                ballSpawner.gameObject.SetActive(false); // ボール落下を無効化
                break;
            case DisplayMode.Strobe:
                ballSpawner.gameObject.SetActive(true); // ボール落下を有効化
                break;
        }
    }
}

