using UnityEngine;
using UnityEngine.InputSystem;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>
    /// 実機が無いときの入力．キーボードの 1〜0 がレーン 1〜10（内部 0〜9），Q〜P がレーン 11〜20（内部 10〜19）．
    /// 画面に出すレーン番号（1始まり）とキーの数字がそろうようにしている．
    /// </summary>
    public sealed class KeyboardTouchInput : MonoBehaviour
    {
        public GameSession session;

        static readonly Key[] Keys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0,
            Key.Q, Key.W, Key.E, Key.R, Key.T, Key.Y, Key.U, Key.I, Key.O, Key.P,
        };

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || session == null) return;
            for (int lane = 0; lane < Keys.Length; lane++)
                if (kb[Keys[lane]].wasPressedThisFrame) session.Touch(lane);
        }
    }
}
