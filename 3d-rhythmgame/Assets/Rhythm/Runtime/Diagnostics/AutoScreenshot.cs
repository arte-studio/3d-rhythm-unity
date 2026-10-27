using System;
using System.Collections;
using UnityEngine;

namespace Arte.Rhythm.Diagnostics
{
    /// <summary>
    /// 起動引数 -screenshot &lt;path&gt; [-screenshot-delay 秒] があるとき，指定秒後に画面を PNG に保存する．
    /// LED のキャンバスがあれば &lt;path&gt;-canvas.png にも保存する．
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
                // LED のキャンバス（列 × LED 数）も 4 倍に拡大して保存する
                var canvas = Render.LedCanvas.Current;
                if (canvas != null && canvas.Texture != null)
                {
                    var rt = canvas.Texture;
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    RenderTexture.active = prev;
                    var big = new Texture2D(rt.width * 4, rt.height * 4, TextureFormat.RGBA32, false);
                    for (int y = 0; y < big.height; y++)
                        for (int x = 0; x < big.width; x++) big.SetPixel(x, y, tex.GetPixel(x / 4, y / 4));
                    System.IO.File.WriteAllBytes(System.IO.Path.ChangeExtension(path, null) + "-canvas.png", big.EncodeToPNG());
                }
            }
            if (float.TryParse(Arg("-quit-after"), out var q))
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(0, q - (path != null ? delay : 0)));
                Application.Quit();
            }
        }
    }
}
