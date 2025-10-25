using System.Collections;
using UnityEngine;
using UnityEngine.Rendering; // AsyncGPUReadbackを使用するために必要
// using static UnityEngine.GraphicsBuffer; // この行は現在のコードでは使用されていないため、削除しても問題ない可能性があります

/// <summary>
/// カメラの描画結果をテクスチャから読み取り、バイトデータに変換するクラス
/// </summary>
public class newline : MonoBehaviour
{
    // カメラの描画結果を保存するためのレンダーテクスチャ (★ linetermから共有のものを受け取る)
    RenderTexture rd;

    // このオブジェクトのMeshRendererが持つマテリアルの配列
    Material[] mats;

    // rdをテクスチャとして設定する新しいマテリアル
    Material mtr;

    // 初期化が完了したかどうかを示すフラグ
    bool ready = false;

    // Inspectorから設定するシェーダー
    [SerializeField] Shader shader;

    // このインスタンスを識別するためのID
    [SerializeField] int ID;

    // データ集約先となるlinetermスクリプトへの参照
    [SerializeField] lineterm term;

    // ★ lineterm側でConvertIDを使用するため、こちらはコメントアウトまたは削除
    // private int ConvertID(int i)
    // {
    //     ...
    // }

    /// <summary>
    /// 初期化処理
    /// </summary>
    void Start()
    {
        // ★修正: linetermから共有RenderTextureを取得
        // (スクリプト実行順序の設定により、term.combinedRdは初期化済みのはず)
        rd = term.combinedRd;
        if (rd == null)
        {
            Debug.LogError($"lineterm (ID: {ID}) から combinedRd を取得できませんでした。スクリプト実行順序を確認してください。");
            return;
        }

        // 1x120ピクセルのレンダーテクスチャを作成 (★削除)
        // rd = new RenderTexture(1, 120, 0);

        // Inspectorで指定されたシェーダーから新しいマテリアルを作成
        mtr = new Material(shader);

        // 作成したマテリアルのメインテクスチャに、レンダーテクスチャ(rd)を設定
        mtr.SetTexture("_MainTex", rd);

        // このゲームオブジェクトの子にあるカメラを探す
        Camera cam = this.gameObject.GetComponentInChildren<Camera>();
        
        // ★修正: 描画先を共有レンダーテクスチャ(rd)に設定
        cam.targetTexture = rd;

        // ★追加: 共有テクスチャ内の描画位置(ビューポート)を指定
        // (ID 0 は (0,0,1,120), ID 1 は (1,0,1,120)...)
        cam.pixelRect = new Rect(ID, 0, 1, 120);
        
        // ★追加: カメラが背景をクリアしないように設定 (重要)
        // 他のカメラの描画を上書きしないようにする
        cam.clearFlags = CameraClearFlags.Nothing;

        // このゲームオブジェクトのMeshRendererからマテリアル配列を取得
        mats = this.gameObject.GetComponent<MeshRenderer>().materials;

        // マテリアル配列の2番目(インデックス1)を、先ほど作成したマテリアル(mtr)に差し替え
        mats[1] = mtr;

        // 変更したマテリアル配列をMeshRendererに再設定
        this.gameObject.GetComponent<MeshRenderer>().materials = mats;

        // 初期化完了フラグを立てる
        ready = true;
    }

    /// <summary>
    /// フレームごとの更新処理
    /// </summary>
    private void Update()
    {
        // ★修正: lineterm側で一括処理するため、ここでの読み出し処理はすべて削除
        /*
        if (term.ready && ready)
        {
            // (AsyncGPUReadback.Request(...) などの処理をすべて削除)
        }
        */
    }
}
