using UnityEngine;

public class Note : MonoBehaviour
{
    public float targetTime;
    private bool isHit = false;
    public bool IsHit => isHit;  // GameManagerから参照用

    // このSphereが属するレーン番号（GameManagerが設定する）
    public int laneIndex;

    void OnMouseDown()
    {
        if (!isHit && targetTime > 0)
        {
            double now = GameManager.Instance.GetSongTime();
            float diff = (float)(now - targetTime);

            // 判定範囲チェック
            if (Mathf.Abs(diff) < GameManager.Instance.perfectRange)
                Debug.Log($"PERFECT! lane {laneIndex}");
            else if (Mathf.Abs(diff) < GameManager.Instance.goodRange)
                Debug.Log($"GOOD! lane {laneIndex}");
            else
                Debug.Log($"MISS! lane {laneIndex}");

            isHit = true;
            GetComponent<Renderer>().material.color = Color.green;
        }
    }

    public void ResetHit()
    {
        isHit = false;
        targetTime = 0;
    }
}
