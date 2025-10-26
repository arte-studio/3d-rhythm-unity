using System.Collections;
using UnityEngine;
using UnityEngine.Rendering; // AsyncGPUReadbackを使用するために必要
using static UnityEngine.GraphicsBuffer; // この行は現在のコードでは使用されていないため、削除しても問題ない可能性があります

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

    /// <summary>
    /// 初期化処理
    /// </summary>
    void Start()
    {
        // 1x120ピクセルのレンダーテクスチャを作成
        rd = new RenderTexture(1, 120, 0);

        // Inspectorで指定されたシェーダーから新しいマテリアルを作成
        mtr = new Material(shader);

        // 作成したマテリアルのメインテクスチャに、レンダーテクスチャ(rd)を設定
        mtr.SetTexture("_MainTex", rd);

        // このゲームオブジェクトの子にあるカメラを探し、その描画先をレンダーテクスチャ(rd)に設定
        this.gameObject.GetComponentInChildren<Camera>().targetTexture = rd;

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
        // linetermとこのスクリプトの両方の準備ができたら処理を開始
        if (term.ready && ready)
        {
            // GPU上のレンダーテクスチャ(rd)のピクセルデータを非同期でリクエストする
            AsyncGPUReadback.Request(rd, 0, request => {
                // リクエストにエラーがあった場合
                if (request.hasError)
                {
                    Debug.LogError("GPUデータの読み込みに失敗しました。");
                }
                // リクエストが成功した場合
                else
                {
                    // 読み込んだデータをColor32の配列として取得
                    var data = request.GetData<Color32>();
                    Color32[] colors = data.ToArray();

                    // Color32配列をRGBのバイト配列に変換する
                    // (各ピクセルからR, G, Bの3バイトを取り出す)
                    byte[] bytes = new byte[colors.Length * 3];
                    for (int i = 0; i < colors.Length; i++)
                    {
                        bytes[i * 3]     = colors[i].r; // R
                        bytes[i * 3 + 1] = colors[i].g; // G
                        bytes[i * 3 + 2] = colors[i].b; // B
                        // if ((int)(ID / 3) % 2 == 0) // 偶数IDの場合、ピクセルの順番を反転
                        // {
                        //     bytes[i * 3] = colors[i].r; // R
                        //     bytes[i * 3 + 1] = colors[i].g; // G
                        //     bytes[i * 3 + 2] = colors[i].b; // B
                        // }
                        // else // 奇数IDの場合、そのまま
                        // {
                        //     bytes[(colors.Length - 1 - i) * 3] = colors[i].r; // R
                        //     bytes[(colors.Length - 1 - i) * 3 + 1] = colors[i].g; // G
                        //     bytes[(colors.Length - 1 - i) * 3 + 2] = colors[i].b; // B
                        // }
                    }

                    // 変換したバイト配列を、linetermスクリプトのbytes配列に、自身のIDの位置に格納
                    term.bytes[ID] = bytes;

                    // デバッグ用: IDが0の場合のみ、最初のピクセルのR値をログに出力
                    // if (ID == 0) Debug.Log("id " + ID + " の最初のR値は " + colors[0].r);
                }
            });
        }
    }
}
