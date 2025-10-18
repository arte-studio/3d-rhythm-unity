using System.Collections;
using UnityEngine;

/// <summary>
/// GameManagerの制御下で、上からボールをランダムに降らせる。
/// モード2 (Strobe) の時に有効化される。
/// </summary>
public class BallSpawner : MonoBehaviour
{
    [Header("生成するボールの設定")]
    [Tooltip("落下させるボールのプレハブ")]
    public GameObject ballPrefab;
    [Tooltip("ボールを生成する間隔（秒）")]
    public float spawnInterval = 0.5f;

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
        // このコンポーネントが有効になった時にボール生成を開始
        StartCoroutine(SpawnBallsCoroutine());
    }

    void OnDisable()
    {
        // 無効になった時にボール生成を停止
        StopAllCoroutines();
    }

    IEnumerator SpawnBallsCoroutine()
    {
        if (ballPrefab == null || centerTransform == null)
        {
            UnityEngine.Debug.LogError("BallSpawnerにPrefabまたはCenter Transformが設定されていません。");
            yield break; // コルーチンを終了
        }

        // このオブジェクトが有効である限り、無限にボールを生成
        while (true)
        {
            Vector3 centerPos = centerTransform.position;

            // ★ 修正点: UnityEngine.Random を明示
            float angle = UnityEngine.Random.Range(0f, 2f * Mathf.PI);
            // ★ 修正点: UnityEngine.Random を明示
            float radius = UnityEngine.Random.Range(spawnInnerRadius, spawnOuterRadius);

            float x = centerPos.x + radius * Mathf.Cos(angle);
            float z = centerPos.z + radius * Mathf.Sin(angle);
            float y = centerPos.y + spawnHeight;

            Vector3 spawnPosition = new Vector3(x, y, z);

            Instantiate(ballPrefab, spawnPosition, Quaternion.identity);

            yield return new WaitForSeconds(spawnInterval);
        }
    }
}

