using UnityEngine;

/// <summary>
/// 落下するボールにアタッチするシンプルなスクリプト。
/// 一定時間後に自動で消滅する。
/// </summary>
public class BallController : MonoBehaviour
{
    [Tooltip("ボールが消えるまでの時間（秒）")]
    public float lifeTime = 5.0f;

    void Start()
    {
        // 作成されてから lifeTime 秒後に、このゲームオブジェクトを破壊する
        Destroy(gameObject, lifeTime);
    }
}
