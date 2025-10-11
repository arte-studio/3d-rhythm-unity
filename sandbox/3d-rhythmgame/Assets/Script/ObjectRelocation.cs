using System.Collections.Generic;
using System.IO;
using UnityEngine;


public class ObjectRelocation : MonoBehaviour
{
    [Header("JSONファイル名 (Resourcesフォルダ内)")]
    public string jsonFileName = "object_positions";

    [Header("プレハブマッピング")]
    public List<PrefabMapping> prefabMappings;

    [HideInInspector]
    public Dictionary<string, int> prefabCounters = new Dictionary<string, int>(); //プレハブ名ごとにオブジェクトの個数を管理するための辞書を用意している
    //プレハブの種類ごとに、レーン番号ごとのオブジェクトリスト,キーがプレハブの種類名(文字列)、値がレーン番号とその種類・レーンに属するオブジェクト
    public Dictionary<string, Dictionary<int, GameObject>> objectByTypeAndLane= new Dictionary<string, Dictionary<int, GameObject>>();

    public static ObjectRelocation Instance; // シングルトンインスタンス

    //シングルトン
    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

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

            // prefabCounters の値を index として使う
            if (!prefabCounters.ContainsKey(mapping.originalName))
                prefabCounters[mapping.originalName] = 0;

            int index = prefabCounters[mapping.originalName];
            string newName = $"{mapping.originalName}_{index}";

            //プレハブを複製、配置する
            GameObject instance = Instantiate(mapping.newPrefab, new Vector3(obj.x, obj.y, obj.z), Quaternion.Euler(obj.rx, obj.ry, obj.rz));
            instance.name = newName;
            

            // type + lane(=index)で登録
            if (!objectByTypeAndLane.ContainsKey(mapping.noteType))
                objectByTypeAndLane[mapping.noteType] = new Dictionary<int, GameObject>();

            objectByTypeAndLane[mapping.noteType][index] = instance;

            prefabCounters[mapping.originalName]++;

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