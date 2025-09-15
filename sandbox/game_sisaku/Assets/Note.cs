using UnityEngine;

public class Note : MonoBehaviour
{
    public float targetTime;
    private bool isHit = false;
    public bool IsHit => isHit;  // GameManagerから参照用

    void OnMouseDown()
    {
        if (!isHit && targetTime > 0)
        {
            double now = GameManager.Instance.GetSongTime();
            float diff = (float)(now - targetTime);

            // 判定範囲チェック（赤になっていなくてもOK）
            if (Mathf.Abs(diff) < GameManager.Instance.perfectRange)
                Debug.Log("PERFECT!");
            else if (Mathf.Abs(diff) < GameManager.Instance.goodRange)
                Debug.Log("GOOD!");
            else
                Debug.Log("MISS!");

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
