using System.Collections;
using UnityEngine;

/// <summary>
/// GameManagerの制御下で、上からボールをランダムに降らせる。
/// </summary>
public class BallSpawner : MonoBehaviour
{
    [Header("生成するボールの設定")]
    [Tooltip("落下させるボールのプレハブ")]
    public GameObject ballPrefab;
    [Tooltip("ボールを生成する間隔（秒）")]
    public float spawnInterval = 0.5f;
    // ★★★ ここから追加 ★★★
    [Tooltip("ボールの落下速度を調整します。数値が大きいほどゆっくりになります。")]
    [Range(0f, 5f)] // 0から5の範囲でスライダー表示にする
    public float fallDrag = 0.5f;
    // ★★★ ここまで追加 ★★★

    [Header("落下範囲の設定")]
    [Tooltip("落下範囲の中心（通常はCylinderと同じでOK）")]
    public Transform centerTransform;
    [Tooltip("ボールが出現する内側の半径")]
    public float spawnInnerRadius = 2.0f;
    [Tooltip("ボールが出現する外側の半径")]
    public float spawnOuterRadius = 4.0f;
    [Tooltip("ボールが出現する高さ")]
    public float spawnHeight = 10.0f;

    // OnEnable/OnDisableでコルーチンを管理
    void OnEnable()
    {
        StartCoroutine(SpawnBallsCoroutine());
    }

    void OnDisable()
    {
        StopAllCoroutines();
    }

    IEnumerator SpawnBallsCoroutine()
    {
        if (ballPrefab == null || centerTransform == null)
        {
            UnityEngine.Debug.LogError("BallSpawnerにPrefabまたはCenter Transformが設定されていません。");
            yield break;
        }

        while (true)
        {
            Vector3 centerPos = centerTransform.position;

            float angle = UnityEngine.Random.Range(0f, 2f * Mathf.PI);
            float radius = UnityEngine.Random.Range(spawnInnerRadius, spawnOuterRadius);

            float x = centerPos.x + radius * Mathf.Cos(angle);
            float z = centerPos.z + radius * Mathf.Sin(angle);
            float y = centerPos.y + spawnHeight;

            Vector3 spawnPosition = new Vector3(x, y, z);

            // ★★★ ここから修正 ★★★
            // 生成したボールの情報を newBall 変数に格納
            GameObject newBall = Instantiate(ballPrefab, spawnPosition, Quaternion.identity);

            // newBall から Rigidbody コンポーネントを取得
            Rigidbody rb = newBall.GetComponent<Rigidbody>();

            // Rigidbody があれば、インスペクターで設定した空気抵抗 (drag) を設定
            if (rb != null)
            {
                rb.linearDamping = fallDrag;
            }
            // ★★★ ここまで修正 ★★★

            yield return new WaitForSeconds(spawnInterval);
        }
    }
}

