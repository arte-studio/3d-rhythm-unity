using UnityEngine;

/// <summary>
/// オブジェクトのマテリアルの表面色（Albedo）をランダムに設定し、
/// オブジェクトの大きさをリズミカルに変化させるスクリプト。
/// 個々の星（Star）プレハブにアタッチします。
/// </summary>
public class Blinker : MonoBehaviour
{
    // [Header("点滅の明るさ")] // 光らせる設定は削除しました
    // public float minIntensity = 0.5f;
    // public float maxIntensity = 2.0f;

    [Header("大きさの変化")]
    [Tooltip("最も小さいときのスケール倍率")]
    public float minScale = 1.0f;
    [Tooltip("最も大きいときのスケール倍率")]
    public float maxScale = 1.2f;

    [Header("変化の速度")]
    [Tooltip("拡縮する速さ")]
    public float blinkSpeed = 1.0f;

    [Header("色の設定（確率）")]
    [Tooltip("メインで表示される色")]
    public Color mainColor = Color.white;
    [Tooltip("メインカラーの出現率（重み）")]
    [Range(0, 100)] public int mainColorWeight = 75;

    [Tooltip("サブで表示される色")]
    public Color subColor = new Color(0.7f, 0.9f, 1f); // Light Blue
    [Tooltip("サブカラーの出現率（重み）")]
    [Range(0, 100)] public int subColorWeight = 20;

    [Tooltip("アクセントで表示される色")]
    public Color accentColor = new Color(1f, 0.8f, 0.8f); // Light Pink
    [Tooltip("アクセントカラーの出現率（重み）")]
    [Range(0, 100)] public int accentColorWeight = 5;

    // このオブジェクト専用のマテリアルインスタンス
    private Material starMaterial;
    // この星に適用される基準色
    private Color baseColor;
    // 他の星とタイミングをずらすためのランダムなオフセット値
    private float timeOffset;
    // 元の大きさを保存するための変数
    private Vector3 initialScale;

    // マテリアルの表面色(Albedo)のプロパティIDをキャッシュする変数
    private int albedoColorID;

    void Start()
    {
        // ★ 修正点: URP/HDRPで一般的に使われる "_BaseColor" を指定します。
        //   もし標準(Standard)シェーダーをお使いの場合は、ここを "_Color" に戻してください。
        albedoColorID = Shader.PropertyToID("_Color");

        // 元の大きさを保存
        initialScale = transform.localScale;

        // 自分のRendererコンポーネントを取得
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            // .material を使うと、このオブジェクト専用のマテリアルインスタンスが作られます
            starMaterial = renderer.material;

            // 重みに基づいてランダムに色を決定する
            ChooseColorByWeight();

            // 決定した色をマテリアルの表面色(Albedo)に設定する
            starMaterial.SetColor(albedoColorID, baseColor);
        }

        // 個々の星で大きさの変化のタイミングをずらす
        timeOffset = UnityEngine.Random.Range(0f, 10f);
    }

    /// <summary>
    /// 設定された重みに基づいて baseColor を決定する
    /// </summary>
    void ChooseColorByWeight()
    {
        // 全ての重みの合計を計算
        int totalWeight = mainColorWeight + subColorWeight + accentColorWeight;
        if (totalWeight <= 0)
        {
            baseColor = mainColor; // 重みが設定されていない場合はメインカラーにする
            return;
        }

        // 0から重みの合計までのランダムな値を生成
        int randomValue = UnityEngine.Random.Range(0, totalWeight);

        // ランダムな値がどの範囲にあるかで色を決定
        if (randomValue < mainColorWeight)
        {
            baseColor = mainColor;
        }
        else if (randomValue < mainColorWeight + subColorWeight)
        {
            baseColor = subColor;
        }
        else
        {
            baseColor = accentColor;
        }
    }

    void Update()
    {
        // --- 大きさの計算と適用 ---
        float time = (Time.time * blinkSpeed) + timeOffset;
        float pingPongValue = Mathf.PingPong(time, 1.0f);
        float scaleMultiplier = Mathf.Lerp(minScale, maxScale, pingPongValue);
        transform.localScale = initialScale * scaleMultiplier;
    }
}

