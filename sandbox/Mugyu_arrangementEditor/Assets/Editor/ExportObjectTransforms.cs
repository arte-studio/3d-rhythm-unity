using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

    public static class ExportObjectTransforms
    {
        [MenuItem("Tools/Export Object Transforms")] //メニューの親項目のToolsのサブ項目としてExport Object Transformsを追加

        //座標を保存
        public static void SaveSceneObjects()
        {
            SavedObjectList sceneObjects = new SavedObjectList(); //SavedObjectList型の配列を生成して名前をsceneObjectsとする

            GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None); //シーン内のすべてのゲームオブジェクトを探して配列に格納

            foreach (GameObject obj in allObjects)
            {
                GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(obj); //obj がプレハブから作られたなら、元のプレハブを返す。返り値が null ならプレハブ由来ではない
                string prefabName = prefab != null ? prefab.name : ""; //prefab が存在する場合、その名前を返す

            //C# のオブジェクト初期化構文を使って、SavedObjectDataクラスのインスタンスを作成し、各フィールドに値を代入している
            SavedObjectData data = new SavedObjectData
                {
                    prefabName = prefabName,
                    x = obj.transform.position.x,
                    y = obj.transform.position.y,
                    z = obj.transform.position.z,
                    rx = obj.transform.rotation.eulerAngles.x,
                    ry = obj.transform.rotation.eulerAngles.y,
                    rz = obj.transform.rotation.eulerAngles.z
                };

                sceneObjects.objects.Add(data); //上で作ったdataというインスタンスをSavedObjectListクラスのインスタンス sceneObjectsに追加
            }

            string json = JsonUtility.ToJson(sceneObjects, true); //SavedObjectListクラスのインスタンス sceneObjectsをjson形式の文字列に変換
            string pathSave = Path.Combine(Application.dataPath, "Resources", "object_positions.json"); //ファイルの保存パスを作る
            File.WriteAllText(pathSave, json); //指定したパス（pathSave）に文字列（json）を書き込む

            Debug.Log($"{sceneObjects.objects.Count} 件のPrefabを {pathSave} に保存しました");
        }
    }