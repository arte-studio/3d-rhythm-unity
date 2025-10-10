using System.Collections.Generic;
using System.IO;
using UnityEngine;


public class JsonObjectLoader : MonoBehaviour
{
    [Header("JSONファイル名 (Resourcesフォルダ内)")]
    public string jsonFileName = "object_positions";

    [Header("プレハブマッピング")]
    public List<PrefabMapping> prefabMappings;

    [Header("生成されたオブジェクト名リスト")]
    public List<string> createdObjectNames = new List<string>();

    public Dictionary<string, int> prefabCounters = new Dictionary<string, int>(); //プレハブ名ごとにオブジェクトの個数を管理するための辞書を用意している

    //実行ボタン押したら
    void Start()
    {
        LoadObjectsFromJson();
    }

    //Jsonファイルから読み込んだ座標とインスペクタ上に指定したプレハブを複製して配置する
    void LoadObjectsFromJson()
    {
        string path = Path.Combine(Application.dataPath, "Resources", jsonFileName + ".json"); //パスの生成
        if (!File.Exists(path)) //パスが示す場所にJsonファイルがなければ
        {
            Debug.LogError($"JSONファイルが見つかりません: {path}");
            return;
        }

        string json = File.ReadAllText(path); //パスにあるJsonファイルをすべて読み込んで文字列に変換
        SavedObjectList data = JsonUtility.FromJson<SavedObjectList>(json); //文字列からUnityのオブジェクトに変換

        //プレハブ名があり、マッピングしてあるプレハブを複製、配置する
        foreach (var obj in data.objects)
        {
            var mapping = GetMappedPrefab(obj.prefabName); // JSONの元名に対応するPrefabを探す
            if (mapping == null)
            {
                //Debug.LogWarning($"マッピングされていないプレハブ名: {obj.prefabName}");
                continue;
            }

            //プレハブを複製、配置する
            GameObject instance = Instantiate(mapping.newPrefab, new Vector3(obj.x, obj.y, obj.z), Quaternion.Euler(obj.rx, obj.ry, obj.rz));

            // 名前のカウント（元名ごと）
            if (!prefabCounters.ContainsKey(mapping.originalName))
                prefabCounters[mapping.originalName] = 1;
            else
                prefabCounters[mapping.originalName]++;

            int index = prefabCounters[mapping.originalName];
            string newName = $"{mapping.originalName}_{index}";

            instance.name = newName;
            createdObjectNames.Add(newName);
        }
    }

    PrefabMapping GetMappedPrefab(string originalName)
    {
        foreach (var mapping in prefabMappings)
        {
            if (mapping.originalName == originalName)
                return mapping;
        }
        return null;
    }
}