using UnityEngine;
using System.Collections;

public class LEDTest : MonoBehaviour
{
    public LEDMatrixController matrix;
    public float waveDuration = 3f;     // 波が外から内へ進む全体時間
    public float fadeDuration = 0.5f;   // 1つのLEDが光りきるまでの時間

    private void Start()
    {
        StartCoroutine(DelayedStart());
    }

    IEnumerator DelayedStart()
    {
        yield return null; // LED生成待ち
        yield return PlayWaveAnimation();
    }

    IEnumerator PlayWaveAnimation()
    {
        int rows = matrix.rows;
        int cols = matrix.cols;

        // 固定色（薄い黄色）
        Color baseColor = new Color(1f, 1f, 0.6f);

        Vector2 center = new Vector2((cols - 1) / 2f, (rows - 1) / 2f);

        // 最大距離を計算（角が一番遠い）
        float maxDist = 0f;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                float dist = Vector2.Distance(center, new Vector2(x, y));
                if (dist > maxDist) maxDist = dist;
            }
        }

        float elapsed = 0f;
        while (elapsed < waveDuration + fadeDuration)
        {
            elapsed += Time.deltaTime;

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    float dist = Vector2.Distance(center, new Vector2(x, y));

                    // このLEDが光り始めるタイミング
                    float startTime = (1f - dist / maxDist) * waveDuration;

                    // 0 → 1 の点灯率（明暗のみ変化）
                    float intensity = Mathf.InverseLerp(startTime, startTime + fadeDuration, elapsed);

                    // 輝度をかけ合わせ
                    Color current = baseColor * intensity;

                    matrix.SetLED(y, x,
                        (byte)(current.r * 255),
                        (byte)(current.g * 255),
                        (byte)(current.b * 255));
                }
            }

            yield return null;
        }

        // 中央まで光ったら3回点滅
        for (int i = 0; i < 3; i++)
        {
            SetAll((byte)(baseColor.r * 255), (byte)(baseColor.g * 255), (byte)(baseColor.b * 255));
            yield return new WaitForSeconds(0.2f);
            SetAll(0, 0, 0);
            yield return new WaitForSeconds(0.2f);
        }

        SetAll(0, 0, 0);
    }

    void SetAll(byte r, byte g, byte b)
    {
        for (int y = 0; y < matrix.rows; y++)
        {
            for (int x = 0; x < matrix.cols; x++)
            {
                matrix.SetLED(y, x, r, g, b);
            }
        }
    }
}
