using System.IO;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Song;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

namespace Arte.Rhythm.Editor
{
    /// <summary>
    /// 旧版の譜面（Assets/Resources/*_NoteData_*.json）から，新しい曲データ（song.json + SongAsset）を作る．
    /// 旧譜面のレーン番号は1始まりなので，変換で0始まりに直る．
    /// </summary>
    public static class LegacySongImporter
    {
        public const string SongsRoot = "Assets/Rhythm/Songs";

        [MenuItem("Tools/Arte Rhythm/Import Legacy Chart...")]
        static void ImportFromMenu()
        {
            string path = EditorUtility.OpenFilePanel("旧版の譜面 JSON", "Assets/Resources", "json");
            if (string.IsNullOrEmpty(path)) return;
            string name = Path.GetFileNameWithoutExtension(path);
            var asset = Import(File.ReadAllText(path), name, name, null, null, 0);
            Selection.activeObject = asset;
        }

        /// <summary>
        /// 古代祭（2025-11-01）の曲を作る．音声は GameMusic.wav，演出は旧版の動画 arte.mp4．
        /// 動画の音声は GameMusic.wav より約 0.017 秒早い（相互相関で測定）ので videoOffsetSec で合わせる．
        /// テンポは未測定（自動推定では 76 / 152 BPM 付近の候補が出たが確定できない）．譜面エディタのタップで合わせる．
        /// コマンドライン: Unity.exe -batchmode -quit -projectPath 3d-rhythmgame -executeMethod Arte.Rhythm.Editor.LegacySongImporter.ImportKodaisai
        /// </summary>
        [MenuItem("Tools/Arte Rhythm/Import Kodaisai 2025 Song")]
        public static void ImportKodaisai()
        {
            string json = File.ReadAllText("Assets/Resources/Kodaisai_NoteData_1101.json");
            var audio = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/GameMusic.wav");
            var video = AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/movie/arte.mp4");
            Import(json, "kodaisai-2025", "古代祭 2025", audio, video, -0.017);
        }

        public static SongAsset Import(string legacyJson, string id, string title, AudioClip audio, VideoClip video, double videoOffsetSec)
        {
            string dir = $"{SongsRoot}/{id}";
            Directory.CreateDirectory(dir);

            var data = SongJson.FromLegacy(legacyJson, title);
            data.Audio = audio != null ? Path.GetFileName(AssetDatabase.GetAssetPath(audio)) : "";
            data.Video = video != null ? Path.GetFileName(AssetDatabase.GetAssetPath(video)) : "";
            data.VideoOffsetSec = videoOffsetSec;
            string jsonPath = $"{dir}/song.json";
            File.WriteAllText(jsonPath, SongJson.Save(data));
            AssetDatabase.ImportAsset(jsonPath);

            string assetPath = $"{dir}/{id}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<SongAsset>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<SongAsset>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            asset.songJson = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
            if (audio != null) asset.audio = audio;
            if (video != null) asset.video = video;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LegacySongImporter] {assetPath}: {data.Notes.Count} ノーツ");
            return asset;
        }
    }
}
