using Arte.Rhythm.Gameplay;
using Arte.Rhythm.Net;
using Arte.Rhythm.Song;
using UnityEngine;

namespace Arte.Rhythm.Render
{
    /// <summary>
    /// ノーツ用 LED を DeviceLink.Frame に描く（中身は Core の NoteLedRenderer．旧版の GameManager / NoteLeds と同じ動き）．
    /// 判定は GameSession から受け取る．LedTestPattern のノーツ表示を選んでいる間はそちらが上書きする．
    /// </summary>
    [DefaultExecutionOrder(-15)]
    public sealed class NoteLedPainter : MonoBehaviour
    {
        public DeviceLink link;
        public GameSession session;
        public SongConductor conductor;
        public NoteLedStyle style = new NoteLedStyle();

        NoteLedRenderer painter;

        void Start()
        {
            if (link == null || link.Notes == null || session == null || conductor == null)
            {
                Debug.LogError("[NoteLedPainter] DeviceLink・GameSession・SongConductor を設定する");
                enabled = false;
                return;
            }
            painter = new NoteLedRenderer(link.Notes, style);
            session.Judged += OnJudged;
            conductor.Started += OnStarted;
        }

        void OnDestroy()
        {
            if (session != null) session.Judged -= OnJudged;
            if (conductor != null) conductor.Started -= OnStarted;
        }

        void OnStarted(double fromSec) => painter.Reset();

        void OnJudged(JudgeEvent e) => painter.OnJudged(e, Time.timeAsDouble);

        void LateUpdate()
        {
            if (painter == null || link.Frame == null) return;
            bool started = conductor.IsStarted && session.Judge != null;
            painter.Render(started ? session.Chart : null, started ? session.Judge : null, started ? conductor.SongTime : 0,
                Time.timeAsDouble, Time.deltaTime, conductor.IsPlaying, link.Frame);
        }
    }
}
