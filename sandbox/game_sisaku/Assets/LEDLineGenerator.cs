using UnityEngine;

public class LEDLineGenerator : MonoBehaviour
{
    public GameObject ledPrefab;   // ¬‚³‚È‹…‘Ì (LEDSphere)
    public GameObject startSphere;
    public GameObject endSphere;
    public int count = 30;         // •À‚×‚éŒÂ”

    private GameObject[] leds;     // ¶¬‚µ‚½LED‚ğ•Û

    void Start()
    {
        if (startSphere == null || endSphere == null || ledPrefab == null) return;

        leds = new GameObject[count];
        Vector3 start = startSphere.transform.position;
        Vector3 end = endSphere.transform.position;

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / (count - 1); // 0~1 ‚Ì•âŠÔ’l
            Vector3 pos = Vector3.Lerp(start, end, t);
            leds[i] = Instantiate(ledPrefab, pos, Quaternion.identity, transform);
            leds[i].name = $"LED_{i}";
        }
    }
}
