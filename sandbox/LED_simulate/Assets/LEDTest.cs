using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using static UnityEngine.Rendering.DebugUI.Table;

public class LEDTest : MonoBehaviour
{
    public LEDMatrixController matrix;
    public float waveDuration = 2f;     // 演出A: 外から内へ
    public float fadeDuration = 0.3f;   // 演出A: フェード時間

    public float clockwiseDuration = 4f; // 演出B: 時計回り全体の時間
    public float clockwiseFade = 0.3f;   // 演出B: 1つのLEDが光る時間

    /// <summary>
    /// 初期化とLED配置
    /// </summary>
    private void Start()
    {
        StartCoroutine(DelayedStart());
    }

    /// <summary>
    /// 演出開始を少し遅らせる
    /// </summary>
    IEnumerator DelayedStart()
    {
        yield return null; // LED生成待ち
        // yield return PlayWaveAnimation();      // 演出A
        // yield return new WaitForSeconds(1f);   // 少し間を空ける
        yield return PlaySquareAnimation();    // 演出C
        yield return new WaitForSeconds(1f);   // 少し間を空ける
        // yield return PlayClockwiseAnimation(); // 演出B
    }

    /// <summary>
    /// 演出A: 外から中心に向かって点灯 → 点滅
    /// </summary>
    IEnumerator PlayWaveAnimation()
    {
        int rows = matrix.rows;
        int cols = matrix.cols;

        Color baseColor = new Color(1f, 1f, 0.6f); // 薄い黄色
        Vector2 center = new Vector2((cols - 1) / 2f, (rows - 1) / 2f);

        // 最大距離を計算
        float maxDist = 0f;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
                maxDist = Mathf.Max(maxDist, Vector2.Distance(center, new Vector2(x, y)));

        float elapsed = 0f;
        while (elapsed < waveDuration + fadeDuration)
        {
            elapsed += Time.deltaTime;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    float dist = Vector2.Distance(center, new Vector2(x, y));
                    float startTime = (1f - dist / maxDist) * waveDuration;
                    float intensity = Mathf.InverseLerp(startTime, startTime + fadeDuration, elapsed);

                    Color current = baseColor * intensity;
                    matrix.SetLED(y, x,
                        (byte)(current.r * 255),
                        (byte)(current.g * 255),
                        (byte)(current.b * 255));
                }
            }
            yield return null;
        }

        // 点滅
        for (int i = 0; i < 3; i++)
        {
            SetAll(baseColor);
            yield return new WaitForSeconds(0.2f);
            SetAll(Color.black);
            yield return new WaitForSeconds(0.2f);
        }

        SetAll(Color.black);
    }

    /// <summary>
    /// 演出C: 外側から，四角形に点灯 → 点滅
    /// </summary>
    IEnumerator PlaySquareAnimation()
    {
        int rows = matrix.rows;
        int cols = matrix.cols;

        Color baseColor = new Color(1f, 1f, 0.6f); // 薄い黄色
        Vector2 center = new Vector2((cols - 1) / 2f, (rows - 1) / 2f); // 中心

        // 最初全灯
        SetAll(baseColor);
        yield return new WaitForSeconds(1.0f);
        // 消灯
        SetAll(Color.black);

        // 外側から内側に向かって四角形に点灯
        float elapsed = 0f;
        while (elapsed < waveDuration + fadeDuration)
        {
            elapsed += Time.deltaTime;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    // 中心からの距離ではなく，四角形の距離を計算
                    float distX = Mathf.Abs(x - center.x);
                    float distY = Mathf.Abs(y - center.y);
                    float dist = Mathf.Max(distX, distY); // 四角形距離

                    float maxDist = Mathf.Max(center.x, cols - 1 - center.x, center.y, rows - 1 - center.y);
                    float startTime = (1f - dist / maxDist) * waveDuration;
                    float intensity = Mathf.InverseLerp(startTime, startTime + fadeDuration, elapsed);

                    Color current = baseColor * intensity;
                    matrix.SetLED(y, x,
                        (byte)(current.r * 255),
                        (byte)(current.g * 255),
                        (byte)(current.b * 255));
                }
            }
            yield return null;
        }

        // 点滅
        for (int i = 0; i < 3; i++)
        {
            SetAll(baseColor);
            yield return new WaitForSeconds(0.2f);
            SetAll(Color.black);
            yield return new WaitForSeconds(0.2f);
        }

        SetAll(Color.black);
    }

    /// <summary>
    /// 演出B: 時計回りに点灯
    /// </summary>
    IEnumerator PlayClockwiseAnimation()
    {
        int rows = matrix.rows;
        int cols = matrix.cols;

        Color baseColor = new Color(1f, 1f, 0.6f); // 薄い黄色
        Vector2 center = new Vector2((cols - 1) / 2f, (rows - 1) / 2f);

        float elapsed = 0f;

        while (elapsed < clockwiseDuration)
        {
            elapsed += Time.deltaTime;

            // 現在の針の角度 (0~360)
            float progress = Mathf.Clamp01(elapsed / clockwiseDuration);
            float currentAngle = progress * 360f;

            // x,y の原点は左上なので注意
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    // 中心からの角度（0~360）
                    float angle = Mathf.Atan2(y - center.y, x - center.x) * Mathf.Rad2Deg;
                    if (angle < 0) angle += 360f;

                    // 基準を12時方向（真上）にするため +90°
                    angle = (angle + 90f) % 360f;

                    // 針が通過したLEDだけ点灯
                    if (angle <= currentAngle)
                    {
                        float diff = currentAngle - angle;
                        float intensity = Mathf.InverseLerp(0, 30f, diff); // 30°くらいで光り切る

                        Color current = baseColor * intensity;
                        matrix.SetLED(y, x,
                            (byte)(current.r * 255),
                            (byte)(current.g * 255),
                            (byte)(current.b * 255));
                    }
                    //  通過してないLEDは触らない（=消灯処理しない）
                }
            }

            yield return null;
        }

        //  最後に全点灯を保証
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                matrix.SetLED(y, x, 255, 255, 153); // 薄い黄色 (RGB: 1.0,1.0,0.6)
            }
        }

        //  全点灯をしばらく見せてから消灯
        yield return new WaitForSeconds(1.0f);
        SetAll(Color.black);
    }




    /// <summary>
    /// 全LEDを指定色に設定
    /// </summary>
    void SetAll(Color c)
    {
        for (int y = 0; y < matrix.rows; y++)
            for (int x = 0; x < matrix.cols; x++)
                matrix.SetLED(y, x,
                    (byte)(c.r * 255),
                    (byte)(c.g * 255),
                    (byte)(c.b * 255));
    }
}
