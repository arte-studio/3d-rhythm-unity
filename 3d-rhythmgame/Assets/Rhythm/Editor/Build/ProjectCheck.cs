using System.Collections.Generic;
using System.Linq;
using Arte.Rhythm.Song;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Timeline;

namespace Arte.Rhythm.Editor
{
    /// <summary>
    /// ファイルを動かしたあとなどに，参照が切れていないかを確かめる（2026-11-05 のリファクタで作った）．
    /// - シーン（Build Settings のもの）: スクリプトが見つからない部品，参照先が消えた欄
    /// - 曲（Songs/*/*.asset）: 曲データ・音声・Timeline
    /// - 演出の Timeline（Effects/**/*.playable）: スクリプトが見つからないトラック・クリップ，マテリアルの無いクリップ
    /// - prefab とマテリアル（Assets/Rhythm，Assets/Legacy）: スクリプト・シェーダーが見つからないもの
    /// Tools / Arte Rhythm / Check Project．
    /// コマンドライン: Unity -batchmode -quit -projectPath 3d-rhythmgame -executeMethod Arte.Rhythm.Editor.ProjectCheck.RunFromCommandLine
    /// </summary>
    public static class ProjectCheck
    {
        [MenuItem("Tools/Arte Rhythm/Check Project")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var current = EditorSceneManager.GetActiveScene().path;
            Run();
            if (!string.IsNullOrEmpty(current)) EditorSceneManager.OpenScene(current);
        }

        public static void RunFromCommandLine()
        {
            int problems;
            try { problems = Run(); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                problems = -1;
            }
            EditorApplication.Exit(problems == 0 ? 0 : 1);
        }

        /// <summary>見つかった問題の数を返す（0 なら大丈夫）．</summary>
        public static int Run()
        {
            var problems = new List<string>();
            CheckScenes(problems);
            CheckSongs(problems);
            CheckTimelines(problems);
            CheckPrefabsAndMaterials(problems);
            foreach (var p in problems) Debug.LogError("[ProjectCheck] " + p);
            Debug.Log($"[ProjectCheck] {(problems.Count == 0 ? "OK" : "NG")}: 問題 {problems.Count} 個");
            return problems.Count;
        }

        static void CheckScenes(List<string> problems)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            if (scenes.Count == 0) problems.Add("Build Settings にシーンが無い");
            foreach (var path in scenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    problems.Add($"{path}: シーンが無い");
                    continue;
                }
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int objects = 0;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        objects++;
                        CheckGameObject(t.gameObject, path, problems);
                    }
                Debug.Log($"[ProjectCheck] {path}: {objects} 個の GameObject を見た");
            }
        }

        static void CheckGameObject(GameObject go, string where, List<string> problems)
        {
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (missing > 0) problems.Add($"{where}: {Path(go)} にスクリプトが見つからない部品が {missing} 個");
            foreach (var c in go.GetComponents<Component>())
                if (c != null) CheckReferences(c, $"{where}: {Path(go)} の {c.GetType().Name}", problems);
        }

        // 参照先が消えた欄（インスペクタで Missing と出るもの）
        static void CheckReferences(Object o, string where, List<string> problems)
        {
            var so = new SerializedObject(o);
            var it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0)
                    problems.Add($"{where}.{it.propertyPath} の参照先が無い");
            }
        }

        static void CheckSongs(List<string> problems)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:SongAsset", new[] { "Assets/Rhythm/Songs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var song = AssetDatabase.LoadAssetAtPath<SongAsset>(path);
                if (song == null) { problems.Add($"{path}: 曲を読めない"); continue; }
                if (song.songJson == null) problems.Add($"{path}: 曲データ（song.json）が無い");
                else if (song.Data == null) problems.Add($"{path}: 曲データを読めない");
                if (song.audio == null) problems.Add($"{path}: 音声が無い");
                if (song.timeline == null) problems.Add($"{path}: 演出の Timeline が無い");
                CheckReferences(song, path, problems);
                Debug.Log($"[ProjectCheck] {path}: 曲 {song.audio?.name}，映像 {(song.video != null ? song.video.name : "なし")}，演出 {song.AllEffects().Count} 個");
            }
        }

        static void CheckTimelines(List<string> problems)
        {
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:TimelineAsset", new[] { "Assets/Rhythm" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tl = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
                if (tl == null) { problems.Add($"{path}: Timeline を読めない"); continue; }
                count++;
                foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (sub == null) problems.Add($"{path}: スクリプトが見つからない中身がある（トラック・クリップ・目印）");
                    else CheckReferences(sub, $"{path}: {sub.name}", problems);
                foreach (var track in tl.GetOutputTracks())
                    foreach (var clip in track.GetClips())
                        if (clip.asset == null) problems.Add($"{path}: {track.name} の {clip.displayName} の中身が無い");
            }
            Debug.Log($"[ProjectCheck] Timeline {count} 個を見た");
        }

        static void CheckPrefabsAndMaterials(List<string> problems)
        {
            var dirs = new[] { "Assets/Rhythm", "Assets/Legacy" };
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", dirs))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) { problems.Add($"{path}: prefab を読めない"); continue; }
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) CheckGameObject(t.gameObject, path, problems);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Material", dirs))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) problems.Add($"{path}: マテリアルを読めない");
                else if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader") problems.Add($"{path}: シェーダーが無い");
            }
        }

        static string Path(GameObject go)
        {
            var names = new List<string>();
            for (var t = go.transform; t != null; t = t.parent) names.Insert(0, t.name);
            return string.Join("/", names);
        }
    }
}
