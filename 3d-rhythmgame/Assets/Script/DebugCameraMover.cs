using UnityEngine;
public class DebugCameraMover : MonoBehaviour
{
    // インスペクタで速度を調整可能にする
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float sensitivity = 2f;

    // 回転の内部状態
    private float rotationX = 0f;
    private float rotationY = 0f;

    void Start()
    {
        // 初期回転値を現在のカメラから取得
        rotationX = transform.localEulerAngles.y;
        rotationY = transform.localEulerAngles.x;
    }

    void Update()
    {
        //  確認用デバッグログ
        // Debug.Log("DebugCameraMover Update is running.");

        // 重要な点: 特定のキー (例: Right Shift, Ctrlなど) が押されている間だけ操作を有効にする
        // これにより、ゲームの通常入力 (タッチ判定など) と競合を防ぎます。
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftShift))
        {
            // 視点の回転 (マウス移動)
            // 右クリックを押している間だけ回転させることで、Gameビュー内のマウス操作と分離する
            if (Input.GetMouseButton(1))
            {
                // マウスカーソルを非表示にし、中央に固定する (オプション)
                Cursor.lockState = CursorLockMode.Locked;

                rotationX += Input.GetAxis("Mouse X") * sensitivity;
                rotationY -= Input.GetAxis("Mouse Y") * sensitivity;
                rotationY = Mathf.Clamp(rotationY, -90f, 90f); // 上下の回転制限

                // カメラに回転を適用
                transform.localRotation = Quaternion.Euler(rotationY, rotationX, 0);
            }
            else
            {
                // マウスボタンが離されたらカーソルを元に戻す
                Cursor.lockState = CursorLockMode.None;
            }

            // カメラの移動 (WASD)
            float translationX = Input.GetAxis("Horizontal") * moveSpeed * Time.deltaTime;
            float translationZ = Input.GetAxis("Vertical") * moveSpeed * Time.deltaTime;

            // 上下移動 (Q/Eキーで代替)
            float translationY = 0f;
            if (Input.GetKey(KeyCode.E)) translationY = moveSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.Q)) translationY = -moveSpeed * Time.deltaTime;

            transform.Translate(translationX, translationY, translationZ);
        }
        else
        {
            // デバッグキーが押されていないときは、カーソルを解放
            Cursor.lockState = CursorLockMode.None;
        }
    }
}