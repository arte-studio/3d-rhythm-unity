using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Arte.Rhythm.Editor
{
    /// <summary>
    /// プレイモードに入らずに曲を鳴らす（譜面エディタ用）．
    ///
    /// Unity の公開 API には編集中に音を鳴らす手段が無いので，Inspector の試聴と同じ内部クラス
    /// UnityEditor.AudioUtil をリフレクションで呼ぶ．Unity のバージョンで見つからなくなった場合は
    /// Available が false になり，譜面エディタは再生以外の機能だけで動く．
    ///
    /// 再生位置は AudioUtil のサンプル位置を正とし，その間を実時間で補間して滑らかにする（AudioClock と同じ考え方）．
    /// </summary>
    static class EditorAudio
    {
        static readonly Type AudioUtil = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("UnityEditor.AudioUtil")).FirstOrDefault(t => t != null);
        static readonly MethodInfo PlayMethod = Find("PlayPreviewClip", 3);
        static readonly MethodInfo StopMethod = Find("StopAllPreviewClips", 0);
        static readonly MethodInfo IsPlayingMethod = Find("IsPreviewClipPlaying", 0);
        static readonly MethodInfo SamplePosMethod = Find("GetPreviewClipSamplePosition", 0);

        static AudioClip clip;
        static double anchorSec, anchorRealtime;
        static int lastSample = -1;
        static bool playing;

        public static bool Available => PlayMethod != null && StopMethod != null && SamplePosMethod != null;
        public static bool IsPlaying => playing;

        /// <summary>この Unity で再生できるかをログに出す（Unity を更新したときの確認用）．</summary>
        [MenuItem("Tools/Arte Rhythm/Check Editor Audio")]
        public static void LogStatus() =>
            Debug.Log($"[EditorAudio] AudioUtil={(AudioUtil != null)} Play={(PlayMethod != null)} Stop={(StopMethod != null)} " +
                      $"IsPlaying={(IsPlayingMethod != null)} SamplePos={(SamplePosMethod != null)} → Available={Available}");

        static MethodInfo Find(string name, int args) =>
            AudioUtil?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args);

        /// <summary>clip のファイル上 fromSec 秒から鳴らす．</summary>
        public static void Play(AudioClip c, double fromSec)
        {
            if (!Available || c == null) return;
            Stop();
            clip = c;
            int start = Mathf.Clamp((int)(fromSec * c.frequency), 0, c.samples - 1);
            PlayMethod.Invoke(null, new object[] { c, start, false });
            anchorSec = (double)start / c.frequency;
            anchorRealtime = EditorApplication.timeSinceStartup;
            lastSample = start;
            playing = true;
        }

        public static void Stop()
        {
            if (StopMethod != null) StopMethod.Invoke(null, null);
            playing = false;
        }

        /// <summary>再生中の位置（ファイル上の秒）．止まっていれば NaN．</summary>
        public static double Position
        {
            get
            {
                if (!playing || clip == null) return double.NaN;
                if (IsPlayingMethod != null && !(bool)IsPlayingMethod.Invoke(null, null))
                {
                    playing = false;
                    return double.NaN;
                }
                int sample = (int)SamplePosMethod.Invoke(null, null);
                double now = EditorApplication.timeSinceStartup;
                if (sample != lastSample)
                {
                    // サンプル位置が更新されたら，そこを新しい基準にする（補間との差が小さければ滑らかさを優先）
                    double reported = (double)sample / clip.frequency;
                    double predicted = anchorSec + (now - anchorRealtime);
                    if (Math.Abs(predicted - reported) > 0.03)
                    {
                        anchorSec = reported;
                        anchorRealtime = now;
                    }
                    lastSample = sample;
                }
                return anchorSec + (now - anchorRealtime);
            }
        }
    }
}
