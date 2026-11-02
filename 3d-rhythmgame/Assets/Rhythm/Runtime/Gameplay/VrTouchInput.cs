using System.Collections.Generic;
using Arte.Rhythm.Net;
using Arte.Rhythm.Render;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using InputDevices = UnityEngine.XR.InputDevices;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>
    /// VR でノーツモジュールに触れて遊ぶ（ViewerVR で VR になっているときだけ）．
    /// コントローラの先（握ったときの手の先）がむぎゅっとの正面の「触れる箱」に入った瞬間を，そのレーンのタッチにする（判定は Core の ModuleTouchDetector）．
    /// 触れた瞬間はそのコントローラを短く振動させる．手の位置が分かるよう，コントローラの先に小さな光の球を出す（触れている間は明るく）．
    /// 入口はキーボード（KeyboardTouchInput）や実機のタッチと同じ GameSession.Touch．
    /// </summary>
    public sealed class VrTouchInput : MonoBehaviour
    {
        public GameSession session;
        public DeviceLink link;
        [Tooltip("LED の位置の基準（LedPreview の Transform．ノーツモジュールの位置はこの中の座標）")]
        public Transform layoutRoot;
        [Tooltip("手の先の球のマテリアル（URP の Unlit）")]
        public Material handMaterial;
        [Tooltip("コントローラの握る位置から手の先までの距離（m）")]
        public float tipOffsetM = 0.07f;
        [Tooltip("手の先の球の直径（m）")]
        public float handSizeM = 0.04f;
        [Range(0, 1)] public float hapticAmplitude = 0.6f;
        public float hapticSec = 0.06f;

        sealed class Hand
        {
            public XRNode node;
            public InputAction position, rotation;
            public ModuleTouchDetector detector;
            public Transform ball;
            public Material material;
        }

        readonly List<Hand> hands = new List<Hand>();

        /// <summary>
        /// シーンにゲーム（GameSession）があって VrTouchInput が無ければ足す（古い Play シーンでもそのまま VR で遊べるように．ViewerVR が呼ぶ）．
        /// </summary>
        public static void EnsureInScene()
        {
            if (FindFirstObjectByType<VrTouchInput>() != null) return;
            var session = FindFirstObjectByType<GameSession>();
            if (session == null) return;
            var input = session.gameObject.AddComponent<VrTouchInput>();
            input.session = session;
            input.link = FindFirstObjectByType<DeviceLink>();
            var preview = FindFirstObjectByType<LedPreview>();
            input.layoutRoot = preview != null ? preview.transform : null;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null) input.handMaterial = new Material(shader);
            Debug.Log("[VrTouchInput] VR でノーツモジュールに触れて遊べるようにした");
        }
        readonly List<int> pressed = new List<int>();
        readonly float[] point = new float[3];

        void Start()
        {
            AddHand(XRNode.LeftHand, "LeftHand");
            AddHand(XRNode.RightHand, "RightHand");
        }

        void AddHand(XRNode node, string usage)
        {
            var h = new Hand
            {
                node = node,
                position = new InputAction(binding: $"<XRController>{{{usage}}}/devicePosition", expectedControlType: "Vector3"),
                rotation = new InputAction(binding: $"<XRController>{{{usage}}}/deviceRotation", expectedControlType: "Quaternion"),
            };
            h.position.Enable();
            h.rotation.Enable();
            hands.Add(h);
        }

        void Update()
        {
            if (!ViewerVR.Active || ViewerVR.Rig == null || link == null || link.Notes == null)
            {
                foreach (var h in hands)
                    if (h.ball != null) h.ball.gameObject.SetActive(false);
                return;
            }
            var root = layoutRoot != null ? layoutRoot : transform.root;
            foreach (var h in hands)
            {
                if (h.detector == null) h.detector = ModuleTouchDetector.FromLayout(link.Notes);
                if (h.ball == null) h.ball = NewBall(h);
                var local = h.position.ReadValue<Vector3>();
                var rot = h.rotation.ReadValue<Quaternion>();
                if (rot.x == 0 && rot.y == 0 && rot.z == 0 && rot.w == 0) rot = Quaternion.identity;
                var rig = ViewerVR.Rig;
                var tip = rig.TransformPoint(local + rot * (Vector3.forward * tipOffsetM));
                h.ball.gameObject.SetActive(true);
                h.ball.position = tip;
                var p = root.InverseTransformPoint(tip);
                point[0] = p.x;
                point[1] = p.y;
                point[2] = p.z;
                pressed.Clear();
                h.detector.Update(point, pressed);
                foreach (int lane in pressed)
                {
                    if (session != null) session.Touch(lane);
                    InputDevices.GetDeviceAtXRNode(h.node).SendHapticImpulse(0, hapticAmplitude, hapticSec);
                }
                if (h.material != null)
                    h.material.color = h.detector.Touching.Count > 0 ? new Color(0.9f, 1f, 0.9f) : new Color(0.35f, 0.35f, 0.45f);
            }
        }

        Transform NewBall(Hand h)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"手の先（{h.node}）";
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * handSizeM;
            if (handMaterial != null)
            {
                h.material = new Material(handMaterial);
                go.GetComponent<Renderer>().sharedMaterial = h.material;
            }
            return go.transform;
        }

        void OnDestroy()
        {
            foreach (var h in hands)
            {
                h.position?.Dispose();
                h.rotation?.Dispose();
                if (h.material != null) Destroy(h.material);
            }
        }
    }
}
