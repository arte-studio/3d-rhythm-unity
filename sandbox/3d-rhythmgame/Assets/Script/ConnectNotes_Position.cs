using System.Collections;
using UnityEngine;

public class ConnectNotes_Position : MonoBehaviour
{
    public GameObject targetPrefab; // InspectorでPrefab指定
    private GameObject targetInstance; // シーン上に生成したインスタンス
    public Vector3 localPos;

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
