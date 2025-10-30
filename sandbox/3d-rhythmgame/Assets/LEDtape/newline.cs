using System.Collections;
using UnityEngine;
using UnityEngine.Rendering; // AsyncGPUReadbackを使用するために必要

/// <summary>
/// カメラの描画結果をテクスチャから読み取り、バイトデータに変換するクラス
/// </summary>
public class newline : MonoBehaviour
{
    // カメラの描画結果を保存するためのレンダーテクスチャ
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
    
    // ★追加: LEDカメラが描画するレイヤー名
    [SerializeField]
    private string captureLayerName = "LEDCapture";

    private int ConvertID(int i)
    {
        int n = i % 30 * 3;
        if ((int)i / 30 == 0) n += 0;
        else if ((int)i / 30 == 1) n += 2;
        else if ((int)i / 30 == 2) n += 1;
        return n;
    }

    /// <summary>
    /// 初期化処理
    /// </summary>
    void Start()
    {
        // ★修正: lineterm の準備ができるまで待機する (実行順序設定が確実だが念のため)
        StartCoroutine(InitializeWhenReady());
    }
    
    /// <summary>
    /// ★追加: lineterm の準備ができてから初期化を実行するコルーチン
    /// </summary>
    private IEnumerator InitializeWhenReady()
    {
        // lineterm が ready になるか、combinedRd が割り当てられるまで待機
        while (term == null || !term.IsReady() || term.combinedRd == null)
        {
            yield return null;
        }

        // lineterm から共有 RenderTexture を取得
        rd = term.combinedRd; 

        // 1x120ピクセルのレンダーテクスチャを作成 (★削除)
        // rd = new RenderTexture(1, 120, 0);

        // Inspectorで指定されたシェーダーから新しいマテリアルを作成
        mtr = new Material(shader);

        // 作成したマテリアルのメインテクスチャに、レンダーテクスチャ(rd)を設定
        mtr.SetTexture("_MainTex", rd);

        // このゲームオブジェクトの子にあるカメラを探す
        Camera cam = this.gameObject.GetComponentInChildren<Camera>();
        
        // その描画先を共有レンダーテクスチャ(rd)に設定
        cam.targetTexture = rd;
        
        // --- ★ここから追加 (Culling Mask設定) ---
        
        // 1. このカメラが描画するレイヤーを設定
        int captureLayer = LayerMask.NameToLayer(captureLayerName);
        if (captureLayer == -1)
        {
            Debug.LogError($"レイヤー '{captureLayerName}' が見つかりません。Project Settings > Tags and Layers で作成してください。", this.gameObject);
            yield break;
        }
        cam.cullingMask = 1 << captureLayer; // captureLayerName のレイヤー「だけ」を描画
        // カメラの描画負荷を極限まで下げる ---
        cam.renderingPath = RenderingPath.VertexLit; // 最も軽量なレンダリングパス
        cam.allowMSAA = false; // アンチエイリアスを無効化
        // cam.allowHDR = false; // HDRを無効化
        // cam.useOcclusionCulling = false; // オクルージョンカリングを無効化
        // cam.shadows = LightShadows.None; // LEDキャプチャレイヤーのオブジェクトが Unlit (影なし) シェーダーなら，影の描画も不要

        // 2. カメラの背景をクリアする方法を設定
        //    (Nothingだとゴミが残る可能性、SolidColorで黒にするのが安全)
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black; // 背景色を黒に設定

        // 3. 描画先のビューポート（ピクセル単位）を指定
        //    (combinedRd の (ID, 0) の位置に 1x120 ピクセルで描画)
        cam.pixelRect = new Rect(ID, 0, 1, 120); 
        
        // --- ★追加ここまで ---

        // このゲームオブジェクトのMeshRendererからマテリアル配列を取得
        // (このMeshRendererがプレビュー用のものであれば、以下の処理は残す)
        var meshRenderer = this.gameObject.GetComponent<MeshRenderer>();
        if (meshRenderer != null && meshRenderer.materials.Length > 1)
        {
            mats = meshRenderer.materials;

            // マテリアル配列の2番目(インデックス1)を、先ほど作成したマテリアル(mtr)に差し替え
            mats[1] = mtr;

            // 変更したマテリアル配列をMeshRendererに再設定
            meshRenderer.materials = mats;
        }
        else
        {
            Debug.LogWarning($"MeshRenderer またはマテリアル[1] が見つかりません (ID: {ID})", this.gameObject);
        }

        // 初期化完了フラグを立てる
        ready = true;
    }


    /// <summary>
    /// フレームごとの更新処理
    /// </summary>
    private void Update()
    {
        // ★修正: 読み出し処理はすべて lineterm に移行したため、
        // この Update 内の AsyncGPUReadback.Request はすべて削除する。
        
        // (もし lineterm.ready を使った他の処理がなければ、Update() ごと削除しても良い)
    }
}

