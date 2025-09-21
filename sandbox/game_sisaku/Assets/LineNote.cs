using UnityEngine;

public class LineNote : MonoBehaviour
{
    public GameObject fromSphere;
    public GameObject toSphere;
    public float targetTime;

    public bool IsHit { get; private set; }

    private bool isDragging = false;
    private Vector3 dragStartPos;

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            // fromSphere ÇâüÇµÇΩÇ©ÅH
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.collider.gameObject == fromSphere)
                {
                    isDragging = true;
                    dragStartPos = hit.point;
                    IsHit = false;
                }
            }
        }

        if (isDragging && Input.GetMouseButtonUp(0))
        {
            // toSphere Ç≈ó£ÇµÇΩÇ©ÅH
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.collider.gameObject == toSphere)
                {
                    IsHit = true;
                    Debug.Log("LINE HIT!");
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
