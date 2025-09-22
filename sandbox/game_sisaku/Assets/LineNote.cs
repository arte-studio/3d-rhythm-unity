using UnityEngine;

public class LineNote : MonoBehaviour
{
    public GameObject fromSphere;
    public GameObject toSphere;
    public bool IsHit { get; private set; }

    private bool isDragging = false;
    private LEDLineGenerator gen;

    void Start()
    {
        gen = GetComponent<LEDLineGenerator>();
    }

    void Update()
    {
        // ドラッグ開始
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.collider.gameObject == fromSphere)
                {
                    isDragging = true;
                    IsHit = false;
                }
            }
        }

        // ドラッグ終了
        if (isDragging && Input.GetMouseButtonUp(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.collider.gameObject == toSphere)
                {
                    IsHit = true;
                    Debug.Log("LINE HIT!");

                    // 緑に変化
                    if (gen != null)
                    {
                        foreach (Transform child in gen.transform)
                        {
                            var r = child.GetComponent<Renderer>();
                            if (r != null) r.material.color = Color.green;
                        }
                    }
                }
                else
                {
                    Debug.Log("LINE MISS (wrong end)");
                }
            }
            isDragging = false;
        }
    }

    public void ResetHit()
    {
        IsHit = false;
        isDragging = false;
    }
}
