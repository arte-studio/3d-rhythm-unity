using UnityEngine;
using System.Collections.Generic;

public class SphereSpawner : MonoBehaviour
{
    public GameObject spherePrefab;
    public int wallCount = 12;
    public int insideCount = 6;
    public float cylinderRadius = 1.0f; // ”¼Œa1m (’¼Œa2m)
    public float cylinderHeight = 2.0f;
    public float sphereDiameter = 0.07f; // 70mm
    public float minHeight = 1.2f;
    public float maxHeight = 1.8f;
    public float minDistance = 0.5f; // 500mm = 0.5m

    private List<Vector3> placedPositions = new List<Vector3>();

    void Start()
    {
        SpawnSpheres();
    }

    void SpawnSpheres()
    {
        // ‚Ü‚¸•Ç‚É12ŒÂ”z’u
        for (int i = 0; i < wallCount; i++)
        {
            Vector3 pos = GetWallPosition();
            if (pos != Vector3.zero)
            {
                Instantiate(spherePrefab, pos, Quaternion.identity, transform);
                placedPositions.Add(pos);
            }
        }

        // “à•”‚É6ŒÂ”z’u
        for (int i = 0; i < insideCount; i++)
        {
            Vector3 pos = GetInsidePosition();
            if (pos != Vector3.zero)
            {
                Instantiate(spherePrefab, pos, Quaternion.identity, transform);
                placedPositions.Add(pos);
            }
        }
    }

    Vector3 GetWallPosition()
    {
        for (int attempts = 0; attempts < 1000; attempts++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float height = Random.Range(minHeight, maxHeight);
            float x = Mathf.Cos(angle) * cylinderRadius;
            float z = Mathf.Sin(angle) * cylinderRadius;
            Vector3 candidate = new Vector3(x, height, z);

            if (IsFarEnough(candidate))
                return candidate;
        }
        Debug.LogWarning("•Ç‚Ö‚Ì”z’u‚ÉŽ¸”s‚µ‚Ü‚µ‚½");
        return Vector3.zero;
    }

    Vector3 GetInsidePosition()
    {
        for (int attempts = 0; attempts < 1000; attempts++)
        {
            float r = Random.Range(0f, cylinderRadius - sphereDiameter);
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float x = Mathf.Cos(angle) * r;
            float z = Mathf.Sin(angle) * r;
            float height = Random.Range(minHeight, maxHeight);
            Vector3 candidate = new Vector3(x, height, z);

            if (IsFarEnough(candidate))
                return candidate;
        }
        Debug.LogWarning("“à•”‚Ö‚Ì”z’u‚ÉŽ¸”s‚µ‚Ü‚µ‚½");
        return Vector3.zero;
    }

    bool IsFarEnough(Vector3 candidate)
    {
        foreach (var pos in placedPositions)
        {
            if (Vector3.Distance(candidate, pos) < minDistance)
                return false;
        }
        return true;
    }
}
