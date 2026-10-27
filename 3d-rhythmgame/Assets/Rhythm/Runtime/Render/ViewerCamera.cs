using UnityEngine;
using UnityEngine.InputSystem;

namespace Arte.Rhythm.Render
{
    /// <summary>
    /// Viewer のカメラ操作．
    /// - 右ボタンを押している間: マウスで見回す，WASD で歩く，Q / E で下 / 上，Shift で速く（Unity のシーンビューと同じ）
    ///   （右ボタンを押している間はキーボードのレーン入力を止める）
    /// - F5: リングの中心・子どもの目の高さ，F6: 中心・大人の目の高さ，F7: リングの外から，F8: 真上から
    /// 起動引数 -view child|adult|outside|top で最初の視点を選べる．
    /// VR にするときはこの部品の代わりに XR のカメラを使う（docs/architecture-v2.md の VR の計画）．
    /// </summary>
    public sealed class ViewerCamera : MonoBehaviour
    {
        public float childEyeHeight = 1.1f;
        public float adultEyeHeight = 1.6f;
        public float moveSpeed = 1.5f;
        public float lookSpeed = 0.15f;

        float yaw, pitch;

        void Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-view");
            string view = i >= 0 && i + 1 < args.Length ? args[i + 1] : "adult";
            switch (view)
            {
                case "child": Preset(childEyeHeight, 0, 0, -5); break;
                case "outside": PresetAt(new Vector3(0, 1.6f, -2.8f), 0, -5); break;
                case "top": PresetAt(new Vector3(0, 6.5f, 0), 0, 89); break;
                default: Preset(adultEyeHeight, 0, 0, -10); break;
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb != null)
            {
                if (kb.f5Key.wasPressedThisFrame) Preset(childEyeHeight, 0, 0, -5);
                if (kb.f6Key.wasPressedThisFrame) Preset(adultEyeHeight, 0, 0, -10);
                if (kb.f7Key.wasPressedThisFrame) PresetAt(new Vector3(0, 1.6f, -2.8f), 0, -5);
                if (kb.f8Key.wasPressedThisFrame) PresetAt(new Vector3(0, 6.5f, 0), 0, 89);
            }
            if (mouse == null || !mouse.rightButton.isPressed) return;

            var delta = mouse.delta.ReadValue();
            yaw += delta.x * lookSpeed;
            pitch = Mathf.Clamp(pitch - delta.y * lookSpeed, -89, 89);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            if (kb == null) return;
            var move = Vector3.zero;
            if (kb.wKey.isPressed) move += Vector3.forward;
            if (kb.sKey.isPressed) move += Vector3.back;
            if (kb.aKey.isPressed) move += Vector3.left;
            if (kb.dKey.isPressed) move += Vector3.right;
            if (kb.eKey.isPressed) move += Vector3.up;
            if (kb.qKey.isPressed) move += Vector3.down;
            float speed = moveSpeed * (kb.leftShiftKey.isPressed ? 3 : 1);
            transform.position += transform.rotation * move * (speed * Time.unscaledDeltaTime);
        }

        void Preset(float eye, float x, float z, float pitchDeg) => PresetAt(new Vector3(x, eye, z), 0, pitchDeg);

        void PresetAt(Vector3 position, float yawDeg, float pitchDeg)
        {
            yaw = yawDeg;
            pitch = pitchDeg;
            transform.SetPositionAndRotation(position, Quaternion.Euler(pitch, yaw, 0));
        }
    }
}
