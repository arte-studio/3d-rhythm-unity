using System;
using System.Collections;
using UnityEngine;

namespace Arte.Rhythm.Diagnostics
{
    /// <summary>
    /// 起動引数 -screenshot &lt;path&gt; [-screenshot-delay 秒] があるとき，指定秒後に画面を PNG に保存する．
    /// -probe と違って終了はしない（-quit-after 秒 を付けると終了する）．-play 秒 で曲をその位置から再生する．見た目の確認を自動でするため．
    /// </summary>
    public sealed class AutoScreenshot : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (Arg("-screenshot") == null && Arg("-quit-after") == null && Arg("-play") == null) return;
            var go = new GameObject("[AutoScreenshot]");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoScreenshot>();
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        IEnumerator Start()
        {
            if (float.TryParse(Arg("-play"), out var from))
            {
                yield return null;
                var conductor = FindFirstObjectByType<Song.SongConductor>();
                if (conductor != null) conductor.Play(from);
            }
            string path = Arg("-screenshot");
            float delay = float.TryParse(Arg("-screenshot-delay"), out var d) ? d : 5f;
            if (path != null)
            {
                yield return new WaitForSecondsRealtime(delay);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log($"[AutoScreenshot] {path}");
            }
            if (float.TryParse(Arg("-quit-after"), out var q))
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(0, q - (path != null ? delay : 0)));
                Application.Quit();
            }
        }
    }
}
