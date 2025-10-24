using UnityEngine;

//SphereオブジェクトをクリックしたかどうかのスクリプトなのでUnity上のみ必要なはず
public class TouchNotes_Flag : MonoBehaviour
{
    [HideInInspector]
    public bool TouchFlag = false;  // タッチフラグ（true でクリックされたことを示す）


    void OnMouseDown()
    {
        SetClicked();
        
    }

    // フラグを立てる関数
    public void SetClicked()
    {
        TouchFlag = true;
        Debug.Log("ノーツがクリックされました。");
    }

    // 必要ならリセット関数も
    public void ResetFlag()
    {
        TouchFlag = false;
    }
}
