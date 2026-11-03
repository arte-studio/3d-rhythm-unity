using System.Collections.Generic;
using Arte.Rhythm.Net;
using Arte.Rhythm.Render;
using Arte.Rhythm.Song;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
#if ARTE_XR_HANDS
using UnityEngine.XR.Hands;
#endif
using InputDevices = UnityEngine.XR.InputDevices;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>
    /// VR でノーツモジュールに触れて遊ぶ（ViewerVR で VR になっているときだけ）．
    /// コントローラの先（握ったときの手の先），または手の認識で人差し指の先が，むぎゅっとの正面の「触れる箱」に入った瞬間を，
    /// そのレーンのタッチにする（判定は Core の ModuleTouchDetector）．手が認識されていれば指を，されていなければコントローラを使う．
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
            public InputAction position, rotation, tracked;
            public ModuleTouchDetector detector;
            public Transform ball;
            public Material material;
        }

        readonly List<Hand> hands = new List<Hand>();

        /// <summary>手の認識で見えている手の数（VR の中の様子の表示用）．</summary>
        public static int TrackedHands { get; private set; }

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
                tracked = new InputAction(type: InputActionType.Button, binding: $"<XRController>{{{usage}}}/isTracked"),
            };
            h.position.Enable();
            h.rotation.Enable();
            h.tracked.Enable();
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
            VrMenu.PokePoints.Clear();
            int tracked = 0;
            foreach (var h in hands)
            {
                if (h.detector == null) h.detector = ModuleTouchDetector.FromLayout(link.Notes);
                if (h.ball == null) h.ball = NewBall(h);
                var rig = ViewerVR.Rig;
                Vector3 tip;
                if (TryFingerTip(h.node, out var finger))
                {
                    tip = rig.TransformPoint(finger);
                    tracked++;
                }
                else if (h.tracked.IsPressed())
                {
                    var local = h.position.ReadValue<Vector3>();
                    var rot = h.rotation.ReadValue<Quaternion>();
                    if (rot.x == 0 && rot.y == 0 && rot.z == 0 && rot.w == 0) rot = Quaternion.identity;
                    tip = rig.TransformPoint(local + rot * (Vector3.forward * tipOffsetM));
                }
                else
                {
                    // 手もコントローラも見えていない（置いたコントローラの古い位置で触れないように）
                    h.ball.gameObject.SetActive(false);
                    pressed.Clear();
                    h.detector.Update(System.Array.Empty<float>(), pressed);
                    continue;
                }
                h.ball.gameObject.SetActive(true);
                h.ball.position = tip;
                VrMenu.PokePoints.Add(tip);
                var p = root.InverseTransformPoint(tip);
                point[0] = p.x;
                point[1] = p.y;
                point[2] = p.z;
                pressed.Clear();
                h.detector.Update(point, pressed);
                foreach (int lane in pressed)
                {
                    // 音や手の追跡の遅れの分だけ前に押したことにする（VrLatency．PC + Link と Quest 単体で別の値）
                    if (session != null) session.Touch(lane, AudioClock.Realtime - VrLatency.Sec);
                    InputDevices.GetDeviceAtXRNode(h.node).SendHapticImpulse(0, hapticAmplitude, hapticSec);
                }
                if (h.material != null)
                    h.material.color = h.detector.Touching.Count > 0 ? new Color(0.9f, 1f, 0.9f) : new Color(0.35f, 0.35f, 0.45f);
            }
            TrackedHands = tracked;
        }

#if ARTE_XR_HANDS
        static XRHandSubsystem handSubsystem;
        static readonly List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();
#endif

        // 手の認識で人差し指の先（VR の足元の中の座標）．XR Hands のパッケージがあるときだけ（ARTE_XR_HANDS）
        static bool TryFingerTip(XRNode node, out Vector3 tip)
        {
            tip = default;
#if !ARTE_XR_HANDS
            return false;
#else
            if (handSubsystem == null || !handSubsystem.running)
            {
                handSubsystems.Clear();
                SubsystemManager.GetSubsystems(handSubsystems);
                handSubsystem = handSubsystems.Find(s => s.running);
                if (handSubsystem == null) return false;
            }
            var hand = node == XRNode.LeftHand ? handSubsystem.leftHand : handSubsystem.rightHand;
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var pose)) return false;
            tip = pose.position;
            return true;
#endif
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
                h.tracked?.Dispose();
                if (h.material != null) Destroy(h.material);
            }
        }
    }
}
