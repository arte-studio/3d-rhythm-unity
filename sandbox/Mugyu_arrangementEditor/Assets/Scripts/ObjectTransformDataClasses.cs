using UnityEngine;
using System.Collections.Generic;
using System.IO;

//ゲームオブジェクト（特にプレハブ）を保存・再現するためのデータ構造
[System.Serializable]
public class SavedObjectData
{
    public string prefabName; //プレハブ名、無ければ空欄
    public float x, y, z;
    public float rx, ry, rz;
}

//SavedObjectData を 複数まとめて管理するためのラッパークラス
[System.Serializable]
public class SavedObjectList
{
    public List<SavedObjectData> objects = new List<SavedObjectData>(); //SavedObjectData 型を格納する 空のリスト を作る,他のスクリプトからアクセス可能にする
}

[System.Serializable]
public class PrefabMapping
{
    public string originalName;   // JSONに書かれているプレハブ名
    public GameObject newPrefab;  // 実際に配置したいプレハブ
}