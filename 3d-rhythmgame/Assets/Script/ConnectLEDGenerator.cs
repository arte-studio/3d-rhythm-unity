using UnityEngine;

public class ConnectLEDGenerator : MonoBehaviour
{
    public GameObject ledSpherePrefab;
    private int rows = 1;
    private int cols = 30;

    private GameObject[,] frontLEDs;

    void Start()
    {
        GenerateLEDMatrix();
    }

    public void GenerateLEDMatrix()
    {
        // 既存のLEDを破棄する処理は省略しません
        if (frontLEDs != null)
        {
            // ... (破棄ロジック)
        }

        frontLEDs = new GameObject[rows, cols];

        float matrixWidth = 1;

        //float matrixWidth = transform.localScale.x;
        if (matrixWidth == 0) matrixWidth = 0.5f; // スケールが0のときのための安全策

        float ledSize = 0.01f; // LEDの直径

        float spacingX = (matrixWidth - ledSize) / (cols - 1);

        float startX = -matrixWidth / 2f + ledSize / 2f;
        float startY = 0f;

        //float cubeDepth = 0.015f;
        float z = -0.5f;


        // 親Cubeのローカルスケールの逆数を取得
        // これで、子のローカルスケールに適用するとワールドスケールが打ち消される
        Vector3 parentScale = transform.localScale;
        Vector3 inverseParentScale = new Vector3(
            1f / parentScale.x,
            1f / parentScale.y,
            1f / parentScale.z
        );
        // LEDの目標ワールドスケール (ledSize, ledSize, ledSize)
        Vector3 targetWorldScale = Vector3.one * ledSize;


        for (int j = 0; j < cols; j++) // cols = 30
        {
            float x = startX + j * spacingX;
            float y = startY;

            GameObject led = Instantiate(ledSpherePrefab, transform);
            led.transform.localPosition = new Vector3(x, y, z);

            // 修正点1: 一時的に親を解除 (ワールドスケールを正確に設定するため)
            // LEDを生成直後に親から切り離します
            led.transform.SetParent(null);

            // ワールドスケールを直接 LEDの直径に設定
            led.transform.localScale = Vector3.one * ledSize;

            //  修正点2: 再度親を設定
            // ワールドスケールが確定したら、Cubeの子に戻します
            led.transform.SetParent(transform);

            Renderer r = led.GetComponent<Renderer>();
            if (r != null && ledSpherePrefab.GetComponent<Renderer>() != null)
                r.material = new Material(ledSpherePrefab.GetComponent<Renderer>().sharedMaterial);

            frontLEDs[0, j] = led;
        }
    }

    public GameObject[,] GetFrontLEDs() => frontLEDs;
}