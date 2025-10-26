using UnityEngine;

/// <summary>
/// StarSpawnerによって生成される際に、指定されたモードに応じて
/// 色や大きさの変化を制御する。個々の星プレハブにアタッチする。
/// </summary>
public class Blinker : MonoBehaviour
{
    [Header("モード1, 3 の設定")]
    [Tooltip("メインカラー")]
    public Color mainColor = Color.white;
    [Range(0, 100)] public int mainColorWeight = 75;
    [Tooltip("サブカラー")]
    public Color subColor = new Color(0.7f, 0.9f, 1f);
    [Range(0, 100)] public int subColorWeight = 20;
    [Tooltip("アクセントカラー")]
    public Color accentColor = new Color(1f, 0.8f, 0.8f);
    [Range(0, 100)] public int accentColorWeight = 5;

    // --- 内部で使う変数 ---
    private Material starMaterial;
    private Color baseColor;
    private float timeOffset;
    private Vector3 initialScale;
    private int albedoColorID;

    // モードごとの挙動パラメータ
    private float currentMinScale;
    private float currentMaxScale;
    private float currentBlinkSpeed;

    void Awake()
    {
        albedoColorID = Shader.PropertyToID("_BaseColor");
        initialScale = transform.localScale;
        timeOffset = UnityEngine.Random.Range(0f, 10f);

        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            starMaterial = renderer.material;
        }
    }

    /// <summary>
    /// StarSpawnerから呼び出される。指定されたモードに応じて星の挙動を初期化する。
    /// </summary>
    public void InitializeForMode(GameManager.DisplayMode mode)
    {
        switch (mode)
        {
            // --- モード1: Calm ---
            case GameManager.DisplayMode.Calm:
                ChooseColorByWeight();
                currentMinScale = 1.0f;
                currentMaxScale = 1.2f;
                currentBlinkSpeed = 1.0f;
                break;

            // --- モード2: Strobe ---
            case GameManager.DisplayMode.Strobe:
                baseColor = Color.white; // 全ての色を白に
                currentMinScale = 0.5f;
                currentMaxScale = 1.5f;
                currentBlinkSpeed = 10.0f; // 速く点滅させる
                break;

            // ★ --- モード3: FallingBall --- ★
            //    背景の星は穏やかなモード (Calm) と同じ挙動にする
            case GameManager.DisplayMode.FallingBall:
                ChooseColorByWeight();
                currentMinScale = 1.0f;
                currentMaxScale = 1.2f;
                currentBlinkSpeed = 1.0f;
                break;
        }

        if (starMaterial != null)
        {
            starMaterial.SetColor(albedoColorID, baseColor);
        }
    }

    void Update()
    {
        float time = (Time.time * currentBlinkSpeed) + timeOffset;
        float pingPongValue = Mathf.PingPong(time, 1.0f);
        float scaleMultiplier = Mathf.Lerp(currentMinScale, currentMaxScale, pingPongValue);
        transform.localScale = initialScale * scaleMultiplier;
    }

    void ChooseColorByWeight()
    {
        int totalWeight = mainColorWeight + subColorWeight + accentColorWeight;
        if (totalWeight <= 0)
        {
            baseColor = mainColor;
            return;
        }
        int randomValue = UnityEngine.Random.Range(0, totalWeight);

        if (randomValue < mainColorWeight) baseColor = mainColor;
        else if (randomValue < mainColorWeight + subColorWeight) baseColor = subColor;
        else baseColor = accentColor;
    }
}

