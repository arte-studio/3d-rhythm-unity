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
        Strobe  // モード2: 点滅が激しいモード
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

    private float modeTimer; // モード切り替え用のタイマー

    void Start()
    {
        if (starSpawner == null)
        {
            UnityEngine.Debug.LogError("GameManagerにStarSpawnerが設定されていません。");
            return;
        }

        // 初期モードを設定してタイマーをリセット
        CurrentMode = DisplayMode.Calm;
        modeTimer = modeDuration;

        // 最初の星を生成する
        starSpawner.RespawnStars();
    }

    void Update()
    {
        // タイマーを更新
        modeTimer -= Time.deltaTime;

        // タイマーが0になったらモードを切り替える
        if (modeTimer <= 0)
        {
            SwitchMode();
            modeTimer = modeDuration; // タイマーをリセット
        }
    }

    /// <summary>
    /// モードを切り替える
    /// </summary>
    void SwitchMode()
    {
        // 現在のモードに応じて次のモードを決定
        if (CurrentMode == DisplayMode.Calm)
        {
            CurrentMode = DisplayMode.Strobe;
        }
        else
        {
            CurrentMode = DisplayMode.Calm;
        }

        // StarSpawnerに星の再生成を指示
        starSpawner.RespawnStars();
    }
}
