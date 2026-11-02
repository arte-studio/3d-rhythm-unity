using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using Arte.Rhythm.Viewer;

namespace Arte.Rhythm.Render
{
    /// <summary>
    /// Viewer を VR（Meta Quest 3 を Quest Link でつないだ PC VR．OpenXR）で見る．Play シーンにも Viewer シーンにも入っている．
    ///
    /// VR は次のときだけ始める（ヘッドセットが無ければふつうの画面のまま）:
    /// - エディタ: メニュー Tools / Arte Rhythm / VR / Play in VR にチェックを入れて再生
    /// - ビルド: 起動引数 -vr
    ///
    /// 始まったら，カメラを「VR の足元（リグ）」の子にしてヘッドセットの位置と向きで動かす．足元はリングの中心の床（高さ 0）に置く．
    /// 操作（コントローラ / キーボード）:
    /// - A / F5: 子どもの目の高さ（1.1m）で見る（頭の高さに関係なく目を 1.1m に合わせる）
    /// - B / F6: 自分の目の高さで見る
    /// - X / F7: リングの中心に戻る（今いる場所と向きをリングの中心・正面に合わせる）
    /// - Y / F3: 床の種類を切り替える
    /// - 左スティック: 歩く，右スティック: 30° ずつ向きを変える
    /// 将来 VR でゲームを遊ぶときは，コントローラや手の位置をここから取り，ノーツモジュールに触れたらタッチにする．
    /// </summary>
    [DefaultExecutionOrder(105)]
    public sealed class ViewerVR : MonoBehaviour
    {
        public const string EditorPrefsKey = "Arte.ViewerVR.PlayInVR";

        [Tooltip("子どもの目の高さ（m）")]
        public float childEyeHeight = 1.1f;
        [Tooltip("歩く速さ（m/秒）")]
        public float moveSpeed = 1.0f;
        [Tooltip("右スティックで向きを変える角度")]
        public float snapTurnDeg = 30f;
        public ViewerRoom room;

        /// <summary>VR で表示しているか．</summary>
        public static bool Active { get; private set; }

        Transform rig;
        Camera cam;
        bool childView;
        bool turnReady = true;
        readonly List<InputAction> actions = new List<InputAction>();
        InputAction childAction, ownAction, recenterAction, floorAction, moveAction, turnAction;

        static bool Wanted()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorPrefs.GetBool(EditorPrefsKey, false)) return true;
#endif
            return System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-vr") >= 0;
        }

        IEnumerator Start()
        {
            if (!Wanted()) yield break;
            var settings = XRGeneralSettings.Instance;
            if (settings == null || settings.Manager == null)
            {
                Debug.LogWarning("[ViewerVR] XR の設定が無い（Tools / Arte Rhythm / VR / Set Up OpenXR を実行する）");
                yield break;
            }
            var manager = settings.Manager;
            if (manager.activeLoader == null) yield return manager.InitializeLoader();
            if (manager.activeLoader == null)
            {
                Debug.LogWarning("[ViewerVR] VR を始められない（Quest Link がつながっているか，Meta Quest Link が OpenXR ランタイムになっているかを確かめる）．ふつうの画面で表示する");
                yield break;
            }
            manager.StartSubsystems();
            Active = true;
            SetUpRig();
            SetUpInput();
            // 足元の高さを床に合わせる（Floor）．合わせられないランタイムでは頭の高さが 0 になるので，あとで A / B で直す
            var subsystems = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var s in subsystems) s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            yield return null;
            yield return null;
            Recenter();
            Debug.Log($"[ViewerVR] VR で表示する（{manager.activeLoader.name}）");
        }

        void SetUpRig()
        {
            cam = Camera.main;
            if (cam == null) return;
            var desktop = cam.GetComponent<ViewerCamera>();
            if (desktop != null) desktop.enabled = false;
            rig = new GameObject("VR Rig（足元）").transform;
            rig.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            cam.transform.SetParent(rig, false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
            cam.fieldOfView = 90;   // VR では使われない（ヘッドセットの視野になる）
            var pose = cam.gameObject.AddComponent<TrackedPoseDriver>();
            pose.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            pose.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            pose.positionInput = new InputActionProperty(NewAction("<XRHMD>/centerEyePosition", InputActionType.Value, "Vector3"));
            pose.rotationInput = new InputActionProperty(NewAction("<XRHMD>/centerEyeRotation", InputActionType.Value, "Quaternion"));
        }

        void SetUpInput()
        {
            childAction = NewAction("<XRController>{RightHand}/primaryButton", InputActionType.Button);
            ownAction = NewAction("<XRController>{RightHand}/secondaryButton", InputActionType.Button);
            recenterAction = NewAction("<XRController>{LeftHand}/primaryButton", InputActionType.Button);
            floorAction = NewAction("<XRController>{LeftHand}/secondaryButton", InputActionType.Button);
            moveAction = NewAction("<XRController>{LeftHand}/thumbstick", InputActionType.Value, "Vector2");
            turnAction = NewAction("<XRController>{RightHand}/thumbstick", InputActionType.Value, "Vector2");
        }

        InputAction NewAction(string binding, InputActionType type, string control = null)
        {
            var a = new InputAction(type: type, binding: binding, expectedControlType: control);
            a.Enable();
            actions.Add(a);
            return a;
        }

        void Update()
        {
            if (!Active || rig == null) return;
            var kb = Keyboard.current;
            if (Pressed(childAction) || (kb != null && kb.f5Key.wasPressedThisFrame)) SetChildView(true);
            if (Pressed(ownAction) || (kb != null && kb.f6Key.wasPressedThisFrame)) SetChildView(false);
            if (Pressed(recenterAction) || (kb != null && kb.f7Key.wasPressedThisFrame)) Recenter();
            if (Pressed(floorAction) && room != null) room.SetFloor((room.FloorIndex + 1) % RoomSurfaces.Floors.Length);

            var move = moveAction.ReadValue<Vector2>();
            if (move.sqrMagnitude > 0.04f)
            {
                var fwd = cam.transform.forward;
                fwd.y = 0;
                fwd.Normalize();
                var right = new Vector3(fwd.z, 0, -fwd.x);
                rig.position += (fwd * move.y + right * move.x) * (moveSpeed * Time.unscaledDeltaTime);
            }
            float turn = turnAction.ReadValue<Vector2>().x;
            if (turnReady && Mathf.Abs(turn) > 0.7f)
            {
                rig.RotateAround(cam.transform.position, Vector3.up, Mathf.Sign(turn) * snapTurnDeg);
                turnReady = false;
            }
            else if (Mathf.Abs(turn) < 0.3f)
            {
                turnReady = true;
            }
        }

        static bool Pressed(InputAction a) => a != null && a.WasPressedThisFrame();

        /// <summary>子どもの目の高さで見る（true）か，自分の目の高さで見る（false）．</summary>
        public void SetChildView(bool child)
        {
            childView = child;
            var p = rig.position;
            p.y = child ? childEyeHeight - cam.transform.localPosition.y : 0;
            rig.position = p;
        }

        /// <summary>今の頭の位置と向きを，リングの中心・正面（+z）に合わせる．</summary>
        public void Recenter()
        {
            if (rig == null || cam == null) return;
            var head = cam.transform;
            float yaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized + new Vector3(0, 0, 1e-4f)).eulerAngles.y;
            rig.RotateAround(head.position, Vector3.up, -yaw);
            var offset = head.position;
            rig.position -= new Vector3(offset.x, 0, offset.z);
            SetChildView(childView);
        }

        void OnDestroy()
        {
            foreach (var a in actions) a.Dispose();
            actions.Clear();
            if (!Active) return;
            Active = false;
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager != null && manager.activeLoader != null)
            {
                manager.StopSubsystems();
                manager.DeinitializeLoader();
            }
        }
    }
}
