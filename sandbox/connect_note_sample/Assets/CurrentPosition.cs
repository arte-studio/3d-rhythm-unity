using System.Collections;
using UnityEngine;

public class MousePosition : MonoBehaviour
{
    private void Start()
    {
        // 0.5秒ごとにマウス位置を表示するコルーチンを開始
        StartCoroutine(LogMousePosition());
    }

    private IEnumerator LogMousePosition()
    {
        while (true)
        {
            // マウスが押されているときのみログを出す
            if (Input.GetMouseButton(0)) // 0 = 左クリック
            {
                Vector3 mousePos = Input.mousePosition;
                // カメラから見たワールド座標に変換（Z値を適切に設定）
                mousePos.z = 10f; // カメラからの距離（必要に応じて変更）
                Vector3 worldPos = Camera.main.ScreenToWorldPoint(mousePos);

                Debug.Log($"マウス位置: {mousePos}, マウスのワールド座標: {worldPos}");
            }

            yield return new WaitForSeconds(0.5f);
        }
    }
}
