using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using UnityEngine.SceneManagement; // SceneManagerを使用するために必要

public static class ExportObjectTransforms
{
    [MenuItem("Tools/Export Object Transforms (Hierarchy Order)")]
    // 座標を保存 (Hierarchyの順序で)
    public static void SaveSceneObjects()
    {
        SavedObjectList sceneObjects = new SavedObjectList();

        // 現在アクティブなシーンを取得
        Scene activeScene = SceneManager.GetActiveScene();

        // シーン内のすべてのルートゲームオブジェクトを取得
        GameObject[] rootObjects = activeScene.GetRootGameObjects();

        // ルートオブジェクトのTransformをHierarchyの順序（GetSiblingIndexの順）にソート
        // GetRootGameObjects() の戻り値は通常Hierarchyの順序ですが、念のためソートします
        System.Array.Sort(rootObjects, (a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));

        // ルートオブジェクトから再帰的にすべての子オブジェクトをたどり、保存リストに追加する
        foreach (GameObject rootObj in rootObjects)
        {
            TraverseAndSave(rootObj.transform, sceneObjects.objects);
        }

        // 保存処理
        string json = JsonUtility.ToJson(sceneObjects, true);
        string pathSave = Path.Combine(Application.dataPath, "Resources", "object_positions_ochasai.json"); //ここの名前を変更してノーツの配置を保存
        File.WriteAllText(pathSave, json);

        // データベースをリフレッシュして、Unityエディタが新しいファイル（または更新）を認識するようにする
        AssetDatabase.Refresh();

        Debug.Log($"{sceneObjects.objects.Count} 件のPrefabを {pathSave} に保存しました (Hierarchy順)");
    }

    // 再帰的にTransformとその子をたどり、プレハブ情報を保存するヘルパー関数
    private static void TraverseAndSave(Transform parentTransform, List<SavedObjectData> savedObjectsList)
    {
        // 親オブジェクト（現在処理中のTransform）自体の情報を保存
        GameObject obj = parentTransform.gameObject;
        if (obj != null)
        {
            GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(obj);
            string prefabName = prefab != null ? prefab.name : "";

            // プレハブ由来のオブジェクト、またはルートオブジェクト（プレハブ名が無い場合、JSONの読み込み側で無視される前提）のみを保存
            if (prefab != null || prefabName != "")
            {
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
                savedObjectsList.Add(data);
            }
        }

        // 子オブジェクトを Hierarchyの順序 (GetSiblingIndex順) で処理
        for (int i = 0; i < parentTransform.childCount; i++)
        {
            Transform child = parentTransform.GetChild(i);
            TraverseAndSave(child, savedObjectsList); // 子に対して再帰呼び出し
        }
    }
}