using UnityEngine;

public class LineNote : MonoBehaviour
{
    public float targetTime;
    private bool isHit = false;
    public bool IsHit => isHit;

    private LineRenderer lineRenderer;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
    }

    void OnMouseOver()
    {
        // マウス押下中にラインをなぞっていたら判定
        if (Input.GetMouseButton(0) && !isHit && targetTime > 0)
        {
            double now = GameManager.Instance.GetSongTime();
            float diff = (float)(now - targetTime);

            if (Mathf.Abs(diff) < GameManager.Instance.perfectRange)
                Debug.Log("LINE PERFECT!");
            else if (Mathf.Abs(diff) < GameManager.Instance.goodRange)
                Debug.Log("LINE GOOD!");
            else
                Debug.Log("LINE MISS!");

            isHit = true;

            // 色を変えてフィードバック
            lineRenderer.startColor = Color.green;
            lineRenderer.endColor = Color.green;
        }
    }

    public void ResetHit()
    {
        isHit = false;
        targetTime = 0;

        // 色をリセット
        lineRenderer.startColor = Color.white;
        lineRenderer.endColor = Color.white;
    }
}
