using UnityEngine;

/// <summary>
/// 指定された範囲内に、星のプレハブを複数生成・配置するスクリプト。
/// Hierarchyに作成した空のGameObjectにアタッチします。
/// </summary>
public class StarSpawner : MonoBehaviour
{
    [Header("生成する星の設定")]
    [Tooltip("星のプレハブ（Blinker.csをアタッチしたもの）")]
    public GameObject starPrefab;
    [Tooltip("生成する星の総数")]
    public int numberOfStars = 100;

    [Header("星のサイズ（ランダム）")]
    [Tooltip("生成される星の最小スケール")]
    public float minSize = 0.8f;
    [Tooltip("生成される星の最大スケール")]
    public float maxSize = 1.2f;

    [Header("配置範囲の設定")]
    [Tooltip("星を配置する中心の基準となるオブジェクト")]
    public Transform cylinderTransform;
    [Tooltip("星を配置する空間の内側の半径")]
    public float innerRadius = 2.0f;
    [Tooltip("星を配置する空間の外側の半径")]
    public float outerRadius = 4.0f;
    [Tooltip("星を配置する高さの範囲")]
    public float cylinderHeight = 5.0f;

    void Start()
    {
        // 必要なオブジェクトが設定されているか確認
        if (starPrefab == null || cylinderTransform == null)
        {
            // ★ エラー修正: UnityEngine.Debug を明示的に指定
            UnityEngine.Debug.LogError("Star Prefab または Cylinder Transform が Inspector で設定されていません。");
            return;
        }

        // 半径の大小関係を自動補正（入力ミスがあっても動作するように）
        if (innerRadius > outerRadius)
        {
            float temp = innerRadius;
            innerRadius = outerRadius;
            outerRadius = temp;
        }

        Vector3 cylinderCenter = cylinderTransform.position;

        // numberOfStars の数だけループして星を生成
        for (int i = 0; i < numberOfStars; i++)
        {
            // 1. ランダムな角度を決める (0°〜360°)
            // ★ エラー修正: UnityEngine.Random を明示的に指定
            float angle = UnityEngine.Random.Range(0f, 2f * Mathf.PI);

            // 2. 内側と外側の半径の間で、ランダムな半径（距離）を決める
            // ★ エラー修正: UnityEngine.Random を明示的に指定
            float radius = UnityEngine.Random.Range(innerRadius, outerRadius);

            // 3. 半径と角度を使ってX, Z座標を計算
            float x = cylinderCenter.x + radius * Mathf.Cos(angle);
            float z = cylinderCenter.z + radius * Mathf.Sin(angle);

            // 4. ランダムな高さを決める
            // ★ エラー修正: UnityEngine.Random を明示的に指定
            float y = cylinderCenter.y + UnityEngine.Random.Range(-cylinderHeight / 2f, cylinderHeight / 2f);

            Vector3 starPosition = new Vector3(x, y, z);

            // 5. 星のプレハブを、計算した座標に生成する
            GameObject newStar = Instantiate(starPrefab, starPosition, Quaternion.identity, this.transform);

            // 6. ランダムなサイズを計算
            // ★ エラー修正: UnityEngine.Random を明示的に指定
            float randomScale = UnityEngine.Random.Range(minSize, maxSize);

            // 7. 生成した星のスケールを変更
            newStar.transform.localScale = new Vector3(randomScale, randomScale, randomScale);
        }
    }
}

