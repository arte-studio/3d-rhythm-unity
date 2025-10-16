using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;

/// <summary>
/// 複数のnewlineスクリプトからバイトデータを集約し、管理するクラス
/// </summary>
public class lineterm : MonoBehaviour
{
    // 各newlineスクリプトからのバイト配列を格納するためのジャグ配列 (byte[]の配列)
    public byte[][] bytes;

    // 初期化が完了し、データの受け入れ準備ができたことを示すフラグ
    public bool ready = false;

    /// <summary>
    /// 初期化処理
    /// </summary>
    void Start()
    {
        // 90個のバイト配列を格納できる領域を確保
        bytes = new byte[90][];

        // 初期化完了フラグを立てる
        ready = true;

        // テスト用の関数呼び出し (現在はコメントアウトされている)
        // Invoke("get1to4", 1f);
    }

    /// <summary>
    /// テスト用の関数
    /// </summary>
    void get1to4()
    {
        // IDが0から3までのバイトデータを結合するテストを実行
        GetBytes(0, 3);
    }

    /// <summary>
    /// 指定された範囲(beginからendまで)のIDのバイトデータを結合して1つのバイト配列として返す
    /// </summary>
    /// <param name="begin">結合を開始するID</param>
    /// <param name="end">結合を終了するID</param>
    /// <returns>結合されたバイト配列</returns>
    public byte[] GetBytes(int begin, int end)
    {
        // 結合後のデータを格納するためのArrayListを、最初のデータ(bytes[begin])で初期化
        ArrayList list = new ArrayList(bytes[begin]);

        // 2番目以降のデータを順番にArrayListに追加していく
        for (int i = begin + 1; i <= end; i++)
        {
            list.AddRange(bytes[i]);
        }

        // ArrayListをbyte配列に変換
        byte[] bt = (byte[])list.ToArray(typeof(byte));

        // デバッグ用: 結合後のバイト配列の長さをログに出力
        Debug.Log("結合後のバイト配列の長さ: " + bt.Length);

        // 結合したバイト配列を返す
        return bt;
    }

    /*
    // デバッグ用のUpdate処理 (現在はコメントアウトされている)
    private void Update()
    {
        Debug.Log(bytes[0][0]);
    }
    */
}