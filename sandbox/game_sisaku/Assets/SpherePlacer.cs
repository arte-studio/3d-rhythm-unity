using UnityEngine;

public class SpherePlacer : MonoBehaviour
{
    public GameObject spherePrefab;
    public float radius = 0.1f;

    [HideInInspector] public GameObject[] spheres; // Å© LEDLinesPlacerÇ©ÇÁéQè∆Ç∑ÇÈÇΩÇﬂí«â¡

    void Start()
    {
        spheres = new GameObject[6];

        for (int i = 0; i < 6; i++)
        {
            float angle = Mathf.Deg2Rad * (60 * i);
            Vector3 pos = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
            spheres[i] = Instantiate(spherePrefab, pos, Quaternion.identity, transform);
            spheres[i].name = "Sphere_" + i;
        }
    }
}
