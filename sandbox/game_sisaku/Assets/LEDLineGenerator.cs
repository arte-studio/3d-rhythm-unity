using UnityEngine;

public class LEDLineGenerator : MonoBehaviour
{
    public GameObject ledPrefab;   // 小さな球体 (LEDSphere)
    public GameObject startSphere;
    public GameObject endSphere;
    public int count = 30;         // 並べる個数

    private GameObject[] leds;     // 複数の GameObject をまとめて扱える配列ledsを宣言

    void Start()
    {
        if (startSphere == null || endSphere == null || ledPrefab == null) return; //スタートのSphere、ゴールのSphere、間のledPrefabがあるか確認

        leds = new GameObject[count]; // ledsという配列を生成
        Vector3 start = startSphere.transform.position; //startSphereの三次元座標(位置・回転・スケールの位置)を取得して3次元の座標（x, y, z）を表す構造体の変数に保存
        Vector3 end = endSphere.transform.position;

        //startSphere から endSphere まで均等に LED を配置する処理
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / (count - 1); //t = start から end までの 割合（0~1）を求める。Lerp関数を使うため
            Vector3 pos = Vector3.Lerp(start, end, t); //2つの値の間を割合で補間（中間値を計算）した座標を保存
            leds[i] = Instantiate(ledPrefab, pos, Quaternion.identity, transform); //LED プレハブを複製してその位置に置くのと配列に格納、このスクリプトの GameObject の Transform
            leds[i].name = $"LED_{i}";// 複製したGameObjectの名前を指定する、{}内に変数や数式を書ける
        }
    }
}
