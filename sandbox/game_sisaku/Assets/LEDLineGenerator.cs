using UnityEngine;

public class LEDLineGenerator : MonoBehaviour
{
    public GameObject ledPrefab;
    public GameObject startSphere;
    public GameObject endSphere;
    public int count = 10; // ï¿Ç◊ÇÈêî

    void Start()
    {
        if (ledPrefab == null || startSphere == null || endSphere == null) return;

        Vector3 startPos = startSphere.transform.position;
        Vector3 endPos = endSphere.transform.position;

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / (count - 1);
            Vector3 pos = Vector3.Lerp(startPos, endPos, t);
            Instantiate(ledPrefab, pos, Quaternion.identity, transform);
        }
    }
}
