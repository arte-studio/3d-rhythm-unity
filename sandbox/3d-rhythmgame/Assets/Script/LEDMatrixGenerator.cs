using UnityEngine;

public class LEDMatrixGenerator : MonoBehaviour
{
    public GameObject ledSpherePrefab; // InspectorでLEDSphere.prefabを指定
    private int rows = 8;
    private int cols = 8;
    //private float spacing = 0.65f / 8f; // LEDMatrixサイズ6.5cmを8分割

    private GameObject[,] frontLEDs;

    void Start()
    {
        GenerateLEDMatrix(); // 実行開始時にLEDを生成
    }

    public void GenerateLEDMatrix()
    {
        frontLEDs = new GameObject[rows, cols];

        float matrixSize = /*0.065f*/1; // 6.5cm
        float ledSize = 0.1f; // LED直径
        float spacingX = (matrixSize - ledSize) / (cols - 1);
        float spacingY = (matrixSize - ledSize) / (rows - 1);

        float startX = -matrixSize / 2f;
        float startY = -matrixSize / 2f;

        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                float x = startX + j * spacingX;
                float y = startY + i * spacingY;
                float z = -0.5f;

                GameObject led = Instantiate(ledSpherePrefab, transform);
                led.transform.localPosition = new Vector3(x, y, z);
                led.transform.localScale = Vector3.one * ledSize;

                Renderer r = led.GetComponent<Renderer>();
                if (r != null && ledSpherePrefab.GetComponent<Renderer>() != null)
                    r.material = new Material(ledSpherePrefab.GetComponent<Renderer>().sharedMaterial);

                frontLEDs[i, j] = led;
            }
        }
    }



    public GameObject[,] GetFrontLEDs() => frontLEDs;
}
