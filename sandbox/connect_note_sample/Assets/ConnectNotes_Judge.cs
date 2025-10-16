using System.Collections;
using UnityEngine;

public class ConnectNotes_Judge : MonoBehaviour
{
    public ConnectNotes_Position notesPosition; // InspectorでConnectNotes_Positionを指定
    public GameObject ConnectNotes_prefab;         // InspectorでCubeプレハブを指定
    public GameObject ConnectNotes_JudgeResion_prefab;         // Inspectorで判定の範囲を指定
    private GameObject ConnectNote;
    private GameObject ConnectNotes_JudgeResion;

    public float targetTime_start;  // スタートする時間
    public float targetTime_goal;   // ゴールする時間
    private float targetTime_connect;       // “つなげる”を何秒でやるかを指定する
    public float notesignalTime = 3f;    // スタートする時間の何秒前から合図を合図を出すか

    bool isplaying = false;

    //  状態管理変数
    private bool dragStarted = false;
    private bool dragEnded = false;
    private bool cubeTouched = false;
    private float elapsed = 0f;
    private float totalJudgeTime = 0f;
    private float requiredTime = 3f;
    private float judgeEndOffset = 1f;
    private float judgeEndTime;

    private void Start()
    {
        if (ConnectNotes_prefab != null && ConnectNotes_JudgeResion_prefab != null) //インスペクタ上でプレハブを設定されていたら
        { 
            // 指定したプレハブを複製する
            ConnectNote = Instantiate(ConnectNotes_prefab, Vector3.zero, Quaternion.identity);
            ConnectNotes_JudgeResion = Instantiate(ConnectNotes_JudgeResion_prefab, Vector3.zero, Quaternion.identity);
        }
        else if(ConnectNotes_JudgeResion = null )
        {
            Debug.LogError("targetCubePrefab が未設定です。");
            return;
        }

        // 5秒後に“つなげる”の判定開始
        StartCoroutine(Before_ConnectJudge());
    }

    private void FixedUpdate()
    {
        if (isplaying == true)
        {
            JudgeMouseDrag();
        }
        
    }

    private IEnumerator Before_ConnectJudge()
    {
        yield return new WaitForSeconds(3f);

        Renderer rend = ConnectNote.GetComponent<Renderer>();
        if (rend != null) rend.material.color = Color.red;

        yield return new WaitForSeconds(notesignalTime);
        if (rend != null) rend.material.color = Color.yellow;

        Debug.Log("=== 判定開始 ===");

        // 状態リセット
        dragStarted = false;
        dragEnded = false;
        cubeTouched = false;
        elapsed = 0f;
        totalJudgeTime = 0f;
        requiredTime = 3f;
        judgeEndOffset = 1f;
        judgeEndTime = requiredTime + judgeEndOffset;

        isplaying = true;
    }

    private void JudgeMouseDrag()
    {
        // 判定全体の経過時間を更新
        totalJudgeTime += Time.deltaTime;

        // マウス位置取得（ローカル座標）
        notesPosition.GetMouseXOnCubeMM(ConnectNote);
        float x_m = notesPosition.localPos.x;

        // Cubeに一度でも触れたか
        if (IsMouseOnJudgeArea()) cubeTouched = true;


        // ドラッグ開始（左端）
        if (!dragStarted && x_m <= -0.25f)
        {
            dragStarted = true;
            elapsed = 0f;
            Debug.Log($"ドラッグ開始: {totalJudgeTime:F2} 秒 (local x = {x_m:F2})");
        }

        // ドラッグ中：経過時間を積算
        if (dragStarted && !dragEnded)
        {
            elapsed += Time.deltaTime;
        }

        // ドラッグ終了（右端）
        if (dragStarted && x_m >= 0.25f)
        {
            dragEnded = true;
            Debug.Log($"ドラッグ終了: {totalJudgeTime:F2} 秒 (local x = {x_m:F2})");
            Debug.Log($"ドラッグ時間: {elapsed:F2} 秒");

            string result;
            if (elapsed < 2.5f) result = "Miss";
            else if (elapsed < 2.9f) result = "Good";
            else if (elapsed < 3.1f) result = "Perfect";
            else if (elapsed < 3.6f) result = "Good";
            else result = "Miss";

            Debug.Log($"判定: {result}（目標 {requiredTime:F2} 秒）");
            EndJudge();
        }

        // 時間切れ判定
        if (totalJudgeTime >= judgeEndTime && !dragEnded)
        {
            if (!cubeTouched)
                Debug.Log($"Cubeに一度も触れなかったため Miss {totalJudgeTime:F2} 秒");
            else
                Debug.Log("時間内に右端へ到達できず Miss");

            Debug.Log("判定: Miss");
            EndJudge();
        }
    }

    private void EndJudge()
    {
        Debug.Log("=== 判定終了 ===");
        isplaying = false;
    }

    private bool IsMouseOnJudgeArea()
    {
        if (!Input.GetMouseButton(0)) return false;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            if (hit.collider != null && hit.collider.gameObject == ConnectNotes_JudgeResion)
            {
                return true;
            }
        }
        return false;
    }

}
