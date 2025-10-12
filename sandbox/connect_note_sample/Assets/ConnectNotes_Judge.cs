using System.Collections;
using UnityEngine;

public class ConnectNotes_Judge : MonoBehaviour
{
    public ConnectNotes_Position notesPosition; // InspectorでConnectNotes_Positionを指定
    public GameObject ConnectNotes_prefab;         // InspectorでCubeプレハブを指定
    private GameObject ConnectNote;

    public float targetTime_start;  // スタートする時間
    public float targetTime_goal;   // ゴールする時間
    private float targetTime;       // “つなげる”を何秒でやるかを指定する
    public float notesignalTime = 3f;    // スタートする時間の何秒前から合図を合図を出すか

    private void Start()
    {
        if (ConnectNotes_prefab != null) //インスペクタ上でプレハブを設定されていたら
        {
            // 指定したプレハブを複製する
            ConnectNote = Instantiate(ConnectNotes_prefab, Vector3.zero, Quaternion.identity);
        }
        else
        {
            Debug.LogError("targetCubePrefab が未設定です。");
            return;
        }      

        // 5秒後に“つなげる”の判定開始
        StartCoroutine(Before_ConnectJudge());

    }

    private IEnumerator Before_ConnectJudge()
    {
        yield return new WaitForSeconds(3f); // 3秒待機　※要らなくなる

        Renderer rend = ConnectNote.GetComponent<Renderer>(); // ノーツの色コンポーネントを取得
        if (rend != null) rend.material.color = Color.red; //コンポーネントがあれば赤色に変える

        yield return new WaitForSeconds(notesignalTime); // 判定開始するよという合図を出して待機
        rend.material.color = Color.yellow; //黄色に変える

        StartCoroutine(JudgeMouseDrag()); // 判定開始
    }

    /*private IEnumerator JudgeMouseDrag()
    {
        float requiredTime = targetTime_goal - targetTime_start; // 目標時間
        bool dragStarted = false;
        bool dragEnded = false;

        float dragStartTime = 0f;
        float dragEndTime = 0f;

        while (!dragEnded)
        {
            // マウス位置取得（ローカル座標）
            notesPosition.GetMouseXOnCubeMM(ConnectNote);
            float x_m = notesPosition.localPos.x;

            // ドラッグ開始判定（左端付近）
            if (!dragStarted && x_m <= -0.25f)
            {
                dragStarted = true;
                dragStartTime = Time.time;
                Debug.Log($"ドラッグ開始: {dragStartTime:F2} 秒 (x={x_m:F2})");
            }

            // ドラッグ終了判定（右端付近）
            if (dragStarted && x_m >= 0.25f)
            {
                dragEndTime = Time.time;
                dragEnded = true;

                float elapsed = dragEndTime - dragStartTime;
                Debug.Log($"ドラッグ終了: {dragEndTime:F2} 秒 (x={x_m:F2})");
                Debug.Log($"ドラッグ時間: {elapsed:F2} 秒");

                // 判定
                string result;
                if (Mathf.Approximately(elapsed, requiredTime))
                    result = "Perfect";
                else if (elapsed < requiredTime)
                    result = "Fast";
                else
                    result = "Slow";

                Debug.Log($"判定: {result}（目標 {requiredTime:F2} 秒）");
            }

            yield return null;
        }
    }*/

    /*private IEnumerator JudgeMouseDrag()
    {
        float requiredTime = 3f;          // 理想の到達時間（ここを変更すれば基準が変わる）
        float judgeEndOffset = 1f;        // 判定終了は requiredTime + judgeEndOffset (= 4s)
        float judgeStartTime = Time.time;

        // 判定ウィンドウ（requiredTime を基準に計算）
        float a = requiredTime - 0.5f;    // 2.5
        float b = requiredTime - 0.1f;    // 2.9
        float c = requiredTime + 0.1f;    // 3.1
        float d = requiredTime + 0.2f;    // 3.2 (ここから次のGood開始)
        float e = requiredTime + 0.6f;    // 3.6

        bool dragStarted = false;
        bool dragEnded = false;
        bool cubeTouched = false;

        float dragStartTime = 0f;
        float dragEndTime = 0f;

        Debug.Log("=== 判定開始 ===");

        while (Time.time - judgeStartTime < requiredTime + judgeEndOffset && !dragEnded)
        {
            // マウス位置取得（ローカル座標）
            notesPosition.GetMouseXOnCubeMM(ConnectNote);
            float x_m = notesPosition.localPos.x;

            // マウスが押されている間にGetMouseXOnCubeMMが呼ばれていればlocalPosが更新される想定で、
            // 一度でもCube上に当たれば cubeTouched を true にする（左端到達で dragStarted を立てるのでここは冗長かも）
            if (Input.GetMouseButton(0)) cubeTouched = true;

            // 左端でドラッグ開始判定
            if (!dragStarted && x_m <= -0.25f)
            {
                dragStarted = true;
                dragStartTime = Time.time - judgeStartTime; // 判定開始からの相対時刻で保持
                Debug.Log($"ドラッグ開始: {dragStartTime:F2} 秒 (local x = {x_m:F2})");
            }

            // 右端でドラッグ終了判定
            if (dragStarted && x_m >= 0.25f)
            {
                dragEnded = true;
                dragEndTime = Time.time - judgeStartTime; // 判定開始からの相対時刻
                float elapsed = dragEndTime - dragStartTime; // 左端→右端にかかった時間

                Debug.Log($"ドラッグ終了: {dragEndTime:F2} 秒 (local x = {x_m:F2})");
                Debug.Log($"ドラッグ時間: {elapsed:F2} 秒");

                // ===== 判定（requiredTime を基準にした閾値を利用） =====
                string result;
                if (elapsed < a) result = "Miss";                  // < 2.5
                else if (elapsed < b) result = "Good";              // 2.5 ～ 2.9
                else if (elapsed < c) result = "Perfect";           // 2.9 ～ 3.1
                else if (elapsed >= d && elapsed < e) result = "Good"; // 3.2 ～ 3.6
                else result = "Miss";                               // それ以外（3.1～3.2 のギャップも Miss、>=3.6 も Miss）

                Debug.Log($"判定: {result}（目標 {requiredTime:F2} 秒）");
            }

            yield return null;
        }

        // 4秒（requiredTime + judgeEndOffset）経過しても到達しなかった場合
        if (!dragEnded)
        {
            if (!cubeTouched)
                Debug.Log("Cubeに一度も触れなかったため Miss");
            else
                Debug.Log("時間内に右端へ到達できず Miss");

            Debug.Log("判定: Miss");
        }

        Debug.Log("=== 判定終了 ===");
    }*/

    private IEnumerator JudgeMouseDrag()
    {
        float requiredTime = 3f;          // 理想の到達時間
        float judgeEndOffset = 1f;        // 判定終了までの猶予時間（= requiredTime + 1秒）
        float judgeEndTime = requiredTime + judgeEndOffset;

        float elapsed = 0f;               // ドラッグ時間を積算
        bool dragStarted = false;
        bool dragEnded = false;
        bool cubeTouched = false;

        Debug.Log("=== 判定開始 ===");

        // 判定全体の経過時間を測るための外側ループ
        float totalJudgeTime = 0f;
        while (totalJudgeTime < judgeEndTime && !dragEnded)
        {
            // 毎フレーム経過時間を積算
            totalJudgeTime += Time.deltaTime;

            // マウス位置取得（ローカル座標）
            notesPosition.GetMouseXOnCubeMM(ConnectNote);
            float x_m = notesPosition.localPos.x;

            // Cubeに一度でも触れたかどうか
            if (Input.GetMouseButton(0)) cubeTouched = true;

            // ドラッグ開始判定（左端）
            if (!dragStarted && x_m <= -0.25f)
            {
                dragStarted = true;
                elapsed = 0f; // 開始時にリセット
                Debug.Log($"ドラッグ開始: {totalJudgeTime:F2} 秒 (local x = {x_m:F2})");
            }

            // ドラッグ中は経過時間を積算
            if (dragStarted && !dragEnded)
            {
                elapsed += Time.deltaTime;
            }

            // ドラッグ終了判定（右端）
            if (dragStarted && x_m >= 0.25f)
            {
                dragEnded = true;
                Debug.Log($"ドラッグ終了: {totalJudgeTime:F2} 秒 (local x = {x_m:F2})");
                Debug.Log($"ドラッグ時間: {elapsed:F2} 秒");

                // 判定ロジック（requiredTime基準）
                string result;
                if (elapsed < 2.5f) result = "Miss";
                else if (elapsed < 2.9f) result = "Good";
                else if (elapsed < 3.1f) result = "Perfect";
                else if (elapsed < 3.6f) result = "Good";
                else result = "Miss";

                Debug.Log($"判定: {result}（目標 {requiredTime:F2} 秒）");
            }

            yield return null;
        }

        // 時間切れ判定
        if (!dragEnded)
        {
            if (!cubeTouched)
                Debug.Log("Cubeに一度も触れなかったため Miss");
            else
                Debug.Log("時間内に右端へ到達できず Miss");

            Debug.Log("判定: Miss");
        }

        Debug.Log("=== 判定終了 ===");
    }




}
