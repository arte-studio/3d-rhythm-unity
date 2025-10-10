using UnityEngine;

//円状に並んだ6つの球（Sphere）を生成して配置する
public class SpherePlacer : MonoBehaviour
{
    public GameObject spherePrefab;
    public float radius = 0.1f;

    [HideInInspector] public GameObject[] spheres; //public関数はインスペクターで見えるけど、[HideInInspector]と設定すると見えなくなる

    void Start()
    {
        spheres = new GameObject[6]; //Start関数内で生成されたSphere配列にもアクセス可能

        for (int i = 0; i < 6; i++)
        {
            float angle = Mathf.Deg2Rad * (60 * i); //Mathf.Deg2Rad は「度をラジアンに変換する定数」
            Vector3 pos = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius; //Mathf.Cos(angle) がX座標、Mathf.Sin(angle) がY座標で三次元座標を決める
            spheres[i] = Instantiate(spherePrefab, pos, Quaternion.identity, transform); //インスペクタで指定されたspherePrefabに指定したSphereオブジェクトを複製
            spheres[i].name = "Sphere_" + i; //spheres配列に入ってるゲームオブジェクトの名前を指定
        }
    }
}
