using UnityEngine;

public class Note : MonoBehaviour
{
    public float targetTime;
    private bool isHit = false;
    public bool IsHit => isHit;  // GameManager‚©‚çŽQÆ—p

    void OnMouseDown()
    {
        if (!isHit && targetTime > 0)
        {
            isHit = true;
            double now = GameManager.Instance.GetSongTime();
            float diff = Mathf.Abs((float)now - targetTime);

            if (diff < GameManager.Instance.perfectRange)
                Debug.Log("PERFECT!");
            else if (diff < GameManager.Instance.goodRange)
                Debug.Log("GOOD!");
            else
                Debug.Log("MISS!"); // © ’x‰Ÿ‚µ‚Ìê‡‚à‚±‚±‚Å MISS

            GetComponent<Renderer>().material.color = Color.green;
        }
    }

    public void ResetHit()
    {
        isHit = false;
        targetTime = 0;
    }
}
