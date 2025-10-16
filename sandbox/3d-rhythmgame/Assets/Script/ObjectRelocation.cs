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
    //public Dictionary<string, Dictionary<int, GameObject>> objectByTypeAndLane= new Dictionary<string, Dictionary<int, GameObject>>();

    // キー: noteType ("touch", "line")
    // 値: そのタイプのプレハブから生成された全ての GameObject のリスト
    [HideInInspector]
    public Dictionary<string, List<GameObject>> noteObjectPools = new Dictionary<string, List<GameObject>>();

    // GameManagerが利用する、次に使用可能なノーツオブジェクトのインデックス（タイプごと）
    [HideInInspector]
    public Dictionary<string, int> nextAvailableNoteIndex = new Dictionary<string, int>();

    public static ObjectRelocation Instance; // シングルトンインスタンス

    [HideInInspector]
    public List<GameObject> spawnedNotes = new List<GameObject>(); //生成した touch_notes を保持するリスト


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
        // プールとインデックスの初期化
        noteObjectPools.Clear();
        nextAvailableNoteIndex.Clear();

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
            /*if (!prefabCounters.ContainsKey(mapping.originalName))
                prefabCounters[mapping.originalName] = 0;

            int index = prefabCounters[mapping.originalName];
            string newName = $"{mapping.originalName}_{index}";*/

            // 既存の配置コード: オブジェクトを固定位置に生成
            GameObject instance = Instantiate(mapping.newPrefab, new Vector3(obj.x, obj.y, obj.z), Quaternion.Euler(obj.rx, obj.ry, obj.rz));
            instance.name = $"{mapping.originalName}_{prefabCounters.GetValueOrDefault(mapping.originalName)}";

            // instance.SetActive(false); // ノーツが固定位置にあるため、ここでは非表示にせず、色で待機状態を表現します。

            // 修正点: objectByTypeAndLane の代わりに noteObjectPools に登録
            string type = mapping.noteType;
            if (!noteObjectPools.ContainsKey(type))
            {
                noteObjectPools[type] = new List<GameObject>();
                nextAvailableNoteIndex[type] = 0; // 次に使うインデックスを0に初期化
            }
            noteObjectPools[type].Add(instance);

            // 既存のカウンターとリストの更新（互換性のために残す）
            prefabCounters[mapping.originalName] = prefabCounters.GetValueOrDefault(mapping.originalName) + 1;
            spawnedNotes.Add(instance);


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

    //新規メソッド: 次に使用するノーツオブジェクトを取得・再利用
    public GameObject GetNextAvailableNote(string noteType)
    {
        if (!noteObjectPools.ContainsKey(noteType) || noteObjectPools[noteType].Count == 0)
        {
            Debug.LogError($"Note pool for type '{noteType}' is empty or not initialized.");
            return null;
        }

        List<GameObject> pool = noteObjectPools[noteType];
        int currentIndex = nextAvailableNoteIndex[noteType];

        GameObject note = pool[currentIndex];

        // 次に利用するオブジェクトのインデックスを更新（循環させる）
        nextAvailableNoteIndex[noteType] = (currentIndex + 1) % pool.Count;

        return note;
    }
}