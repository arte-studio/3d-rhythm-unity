using UnityEngine;

public class LEDTest : MonoBehaviour
{
    public LEDMatrixController matrix;
    private float timer;

    void Update()
    {
        timer += Time.deltaTime;
        int index = (int)(timer * 10) % 64; // ‡”Ô‚É“_“”
        matrix.SetLED(index, 255, 0, 0);    // Ô
    }
}
