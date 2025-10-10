using UnityEngine;
using System.IO;

public class ObjectSpawner : MonoBehaviour 
{ 
    [ContextMenu("Load Objects From JSON")] //コンテキストメニューにLoad Objects From JSONを追加
    //jsonファイルを読み込んでオブジェクトを配置する
    
    public void LoadObjects() 
    {
        string path = Path.Combine(Application.dataPath, "Resources", "object_positions.json"); //パスを生成
        if (!File.Exists(path)) //もし、指定した path にファイルが存在しないなら
        { 
            Debug.LogWarning($"JSONファイルが見つかりません: {path}"); 
            return; 
        } 
        string json = File.ReadAllText(path);//指定したファイルの中身（テキスト）をすべて読み取って、文字列として取得する

        SavedObjectList sceneObjects_fukugen = JsonUtility.FromJson<SavedObjectList>(json); //JSON文字列をUnityのオブジェクトに変換する,JSONから変換された SavedObjectList 型のオブジェクトをsceneObjects という変数に代入
        foreach (var obj in sceneObjects_fukugen.objects)
        {
            GameObject prefab = Resources.Load<GameObject>(obj.prefabName); //Resourcesフォルダからプレハブ（ゲームオブジェクト）を読み込む
            if (prefab == null)
            {
                Debug.LogWarning($" Prefab '{obj.prefabName}' が Resources に見つかりません。");
                continue;
            }
            //読み込んだプレハブをシーンに生成して配置する
            GameObject instance = Instantiate( prefab, new Vector3(obj.x, obj.y, obj.z), Quaternion.Euler(obj.rx, obj.ry, obj.rz) );
            //instance.name = obj.objectName;
        }
        Debug.Log($" {sceneObjects_fukugen.objects.Count} 件のオブジェクトを生成しました");
    }
}