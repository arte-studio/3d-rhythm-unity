using UnityEngine;

// クリックして判定するノーツ（Sphere）」の動作を管理するクラス
public class Note : MonoBehaviour
{
    public float targetTime; //ノーツが押されるべき時間
    private bool isHit = false; //ノーツがヒット済みかどうかを保持する変数(このクラスからしかアクセスできない)、初期値はfalse
    public bool IsHit => isHit; //読み取り専用の変数、他スクリプトから「ノーツがヒットしているか」の判定を調べるための変数

    public int laneIndex;// このSphereが属するレーン番号

    void OnMouseDown() //マウスをクリックすると
    {
        if (!isHit && targetTime > 0) //ノーツが押されているかつノーツが押される時間になっていなければ
        {
            double now = GameManager.Instance.GetSongTime(); // GameManagerクラス内で計算している、現在の音楽の再生時間を取得
            float diff = (float)(now - targetTime); //現在の曲の再生時間とノーツの目標時刻の差を計算する　Unity では多くの関数が float を使う

            // 判定範囲チェック
            if (Mathf.Abs(diff) < GameManager.Instance.perfectRangeSphere) //時間差がPerfectの範囲内なら
                Debug.Log($"PERFECT! lane {laneIndex}");
            else if (Mathf.Abs(diff) < GameManager.Instance.goodRangeSphere) //時間差がGoodの範囲内なら
                Debug.Log($"GOOD! lane {laneIndex}");
            else
                Debug.Log($"MISS! lane {laneIndex}");

            isHit = true; //ヒットしたというフラグを立てる
            GetComponent<Renderer>().material.color = Color.green; //このオブジェクトに付いている Renderer コンポーネントを取得してマテリアルの色を緑色にする
        }
    }

    public void ResetHit() //判定リセットする
    {
        isHit = false;
        targetTime = 0;
    }
}
