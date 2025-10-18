using UnityEngine;

/// <summary>
/// GameManagerの指示に応じて、設定された範囲に星を生成する。
/// </summary>
public class StarSpawner : MonoBehaviour
{
    [Header("生成する星の設定")]
    public GameObject starPrefab;
    public int numberOfStars = 100;

    [Header("星のサイズ（ランダム）")]
    public float minSize = 0.8f;
    public float maxSize = 1.2f;

    [Header("円筒の設定")]
    public Transform cylinderTransform;
    public float innerRadius = 2.0f;
    public float outerRadius = 4.0f;
    public float cylinderHeight = 5.0f;

    /// <summary>
    /// GameManagerから呼び出される。既存の星を全て削除し、新しいモードで星を生成し直す。
    /// </summary>
    public void RespawnStars()
    {
        // --- 1. 既存の星を全て削除 ---
        // このオブジェクトの子になっているオブジェクト（＝生成した星）を全てループ
        foreach (Transform child in transform)
        {
            // 即座にゲームオブジェクトを破壊
            Destroy(child.gameObject);
        }

        // --- 2. 新しい星を生成 ---
        if (starPrefab == null || cylinderTransform == null) return;

        if (innerRadius > outerRadius)
        {
            float temp = innerRadius;
            innerRadius = outerRadius;
            outerRadius = temp;
        }

        Vector3 cylinderCenter = cylinderTransform.position;

        for (int i = 0; i < numberOfStars; i++)
        {
            float angle = UnityEngine.Random.Range(0f, 2f * Mathf.PI);
            float radius = UnityEngine.Random.Range(innerRadius, outerRadius);
            float x = cylinderCenter.x + radius * Mathf.Cos(angle);
            float z = cylinderCenter.z + radius * Mathf.Sin(angle);
            float y = cylinderCenter.y + UnityEngine.Random.Range(-cylinderHeight / 2f, cylinderHeight / 2f);
            Vector3 starPosition = new Vector3(x, y, z);

            GameObject newStar = Instantiate(starPrefab, starPosition, Quaternion.identity, this.transform);

            // ★ Blinkerコンポーネントを取得し、現在のモードを渡して初期化させる
            Blinker blinkerComponent = newStar.GetComponent<Blinker>();
            if (blinkerComponent != null)
            {
                blinkerComponent.InitializeForMode(GameManager.CurrentMode);
            }

            float randomScale = UnityEngine.Random.Range(minSize, maxSize);
            newStar.transform.localScale = new Vector3(randomScale, randomScale, randomScale);
        }
    }
}

