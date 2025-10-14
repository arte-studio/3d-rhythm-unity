using System.Collections;
using UnityEngine;

public class ConnectNotes_Position : MonoBehaviour
{
    public GameObject targetPrefab; // InspectorでPrefab指定
    private GameObject targetInstance; // シーン上に生成したインスタンス
    public Vector3 localPos;


    /*private IEnumerator LogMouseOnCube()
    {
        while (true)
        {
            if (Input.GetMouseButton(0) && targetInstance != null)
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit))
                {
                    if (hit.collider.gameObject == targetInstance)
                    {
                        Vector3 worldPos = hit.point;
                        Vector3 localPos = targetInstance.transform.InverseTransformPoint(worldPos);

                        float x_mm = (localPos.x + 0.25f) * 1000f;
                        float y_mm = (localPos.y + 0.01f) * 1000f;
                        float z_mm = (localPos.z + 0.0015f) * 1000f;

                        Debug.Log($"ワールド座標: {worldPos}, Cube上のX位置: {x_mm:F1} mm, Y: {y_mm:F1} mm, Z: {z_mm:F1} mm");
                    }
                }
            }

            yield return new WaitForSeconds(0.5f);
        }
    }*/

    /*public void GetMouseXOnCubeMM(GameObject target)
    {
        if (Input.GetMouseButton(0) && target != null)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit))
            {
                if (hit.collider.gameObject == target)
                {
                    localPos = target.transform.InverseTransformPoint(hit.point);
                }
            }
            
        }       

    }*/

    public void GetMouseXOnCubeMM(GameObject target)
    {
        if (Input.GetMouseButton(0) && target != null)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            // Rayが何かに当たった場合
            if (Physics.Raycast(ray, out hit))
            {
                // Cubeに直接当たった場合（これまで通り）
                if (hit.collider.gameObject == target)
                {
                    localPos = target.transform.InverseTransformPoint(hit.point);
                    Debug.Log("当たってる");
                }
                else
                {
                    // Cubeに当たらなくても、中心との距離が10cm以内なら有効とする
                    float distance = Vector3.Distance(hit.point, target.transform.position);
                    if (distance <= 0.1f)
                    {
                        localPos = target.transform.InverseTransformPoint(hit.point);
                        Debug.Log("外れた");
                    }
                }
            }
        }
    }

}
