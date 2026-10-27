using System.Collections.Generic;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Song;
using UnityEngine;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>
    /// 確認用の画面表示．LED の描画（P5）ができるまで，判定と音のずれをこれで確かめる．
    /// レーンごとに上からノーツが降りてきて，判定線に重なる瞬間がノーツ時刻．
    /// 判定を受け付けている間（旧版でノーツが黄色だった間）は黄色，判定が出るとその色で光る．
    /// 判定の色は旧版と同じ（Perfect = マゼンタ，Good = 白，Miss = シアン）．
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        public SongConductor conductor;
        public GameSession session;

        [Tooltip("判定線より上に何秒先まで表示するか")]
        public float lookAheadSec = 3f;

        [Tooltip("表示するレーン数")]
        public int laneCount = 15;

        readonly Dictionary<int, (JudgeResult result, float until)> flashes = new Dictionary<int, (JudgeResult, float)>();
        string last = "";

        void OnEnable()
        {
            if (session != null) session.Judged += OnJudged;
        }

        void OnDisable()
        {
            if (session != null) session.Judged -= OnJudged;
        }

        void OnJudged(JudgeEvent e)
        {
            flashes[e.Lane] = (e.Result, Time.unscaledTime + 0.5f);
            last = e.ToString();
        }

        static Color ColorOf(JudgeResult r) => r switch
        {
            JudgeResult.Perfect => Color.magenta,
            JudgeResult.Good => Color.white,
            _ => Color.cyan,
        };

        void Update()
        {
            if (conductor == null) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.enterKey.wasPressedThisFrame) conductor.Play();
            if (kb.spaceKey.wasPressedThisFrame)
            {
                if (conductor.IsPaused) conductor.Resume();
                else conductor.Pause();
            }
            // 演出の確認用に 5 秒ずつ飛ぶ（判定は飛んだ先から数え直す）
            if (conductor.IsStarted && (kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame))
            {
                double t = conductor.SongTime + (kb.rightArrowKey.wasPressedThisFrame ? 5 : -5);
                conductor.Play(System.Math.Max(0, t));
            }
        }

        void OnGUI()
        {
            if (conductor == null || session == null) return;
            GUI.Label(new Rect(10, 10, 800, 22), "Enter = 最初から再生，Space = 一時停止 / 再開，← → = 5 秒戻る / 進む，1〜0・Q〜P = レーン 1〜20 をタッチ，F9 = 演出を切り替え" +
                (SongVisualSync.CurrentEffects != null ? $"（今: {SongVisualSync.CurrentEffects}）" : ""));
            if (!conductor.IsStarted || session.Judge == null) return;

            double now = conductor.SongTime;
            var judge = session.Judge;
            GUI.Label(new Rect(10, 30, 800, 22),
                $"{now:F2} 秒  {conductor.Data?.SectionAt(now)}  スコア {judge.Score}  Perfect {judge.PerfectCount}  Good {judge.GoodCount}  Miss {judge.MissCount}   {last}");

            float top = 60, bottom = Screen.height - 60, width = Mathf.Min(48, (Screen.width - 20f) / laneCount);
            float left = (Screen.width - width * laneCount) / 2;
            float pxPerSec = (bottom - top) / lookAheadSec;
            var white = Texture2D.whiteTexture;
            for (int lane = 0; lane < laneCount; lane++)
            {
                float x = left + lane * width;
                GUI.color = new Color(1, 1, 1, lane % 2 == 0 ? 0.06f : 0.1f);
                GUI.DrawTexture(new Rect(x, top, width - 2, bottom - top), white);
                if (flashes.TryGetValue(lane, out var f) && f.until > Time.unscaledTime)
                {
                    GUI.color = ColorOf(f.result);
                    GUI.DrawTexture(new Rect(x, bottom - 6, width - 2, 12), white);
                }
                GUI.color = Color.white;
                GUI.Label(new Rect(x, bottom + 8, width, 20), $"{Chart.LaneLabel(lane)}");
            }
            GUI.color = new Color(1, 1, 1, 0.6f);
            GUI.DrawTexture(new Rect(left, bottom, width * laneCount, 2), white);

            var notes = session.Chart.Notes;
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                double dt = n.Time - now;
                if (dt > lookAheadSec || n.Lane >= laneCount) continue;
                var result = judge.ResultOf(i);
                if (result != null && dt < -0.3) continue;
                GUI.color = result != null ? ColorOf(result.Value) : judge.IsOpen(i, now) ? Color.yellow : new Color(0.6f, 0.6f, 0.6f);
                float y = bottom - (float)dt * pxPerSec;
                GUI.DrawTexture(new Rect(left + n.Lane * width + 4, y - 4, width - 10, 8), white);
            }
            GUI.color = Color.white;
        }
    }
}
