using Mirror;
using Unity.BossRoom.CameraUtils;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.BossRoom.Gameplay.UserInput
{
    /// <summary>
    /// Desktop camera rotation: hold the <b>middle mouse button</b> (press the wheel) and move the
    /// mouse to swing the camera around the character. Rolling the wheel keeps doing zoom — that one
    /// is wired on the camera prefab itself (the ScrollWheel action drives "Look Orbit Y", which is
    /// what reads as zoom in this rig; see <see cref="MobileZoomBar"/>).
    ///
    /// This is the desktop half of the camera-control split: on keyboard+mouse the camera holds
    /// still and the player turns it themselves with this, while on touch and gamepad — where there
    /// is no third input to spare for it — <see cref="CameraAutoRotate"/> swings it automatically.
    /// The switch between the two is latched per input device in
    /// <c>ClientInputSender.UpdateCameraControlScheme</c>.
    ///
    /// Only the horizontal axis is touched. The vertical one is zoom here, and it already has the
    /// wheel: letting a drag move it too would mean every slightly-diagonal swing quietly changed
    /// the zoom, with nothing to snap it back.
    ///
    /// Self-bootstrapping like the other input widgets, so it needs no scene or prefab wiring and
    /// can't be lost to an Editor re-import.
    /// </summary>
    public class MouseCameraOrbit : MonoBehaviour
    {
        const string k_CMCameraTag = "CMCamera";

        /// <summary>True while the camera is being dragged, for anything that wants to keep out of the way.</summary>
        public static bool IsActive { get; private set; }

        // Degrees of yaw per pixel of mouse travel. A full 180° turn takes about a third of a
        // 1080p screen's width, which is roughly where mouse-look sits in other games.
        const float k_DegreesPerPixel = 0.25f;

        CinemachineOrbitalFollow m_OrbitalFollow;
        bool m_Dragging;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Application.isMobilePlatform)
            {
                return;
            }

            var go = new GameObject(nameof(MouseCameraOrbit));
            DontDestroyOnLoad(go);
            go.AddComponent<MouseCameraOrbit>();
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || NetworkClient.localPlayer == null)
            {
                Release();
                return;
            }

            ResolveCamera();
            if (m_OrbitalFollow == null)
            {
                Release();
                return;
            }

            if (mouse.middleButton.wasPressedThisFrame)
            {
                m_Dragging = true;
            }
            if (!mouse.middleButton.isPressed)
            {
                Release();
                return;
            }

            IsActive = m_Dragging;
            if (!m_Dragging)
            {
                return;
            }

            // Belt and braces: on keyboard+mouse the auto-rotation is already gated off by the
            // control scheme, but if both schemes are somehow live at once the player's own drag wins.
            CameraAutoRotate.Suspend();

            float deltaX = mouse.delta.ReadValue().x;
            if (Mathf.Abs(deltaX) < 0.001f)
            {
                return;
            }

            // Mouse right = look right, the usual mouse-look convention.
            var axis = m_OrbitalFollow.HorizontalAxis;
            float value = axis.Value + deltaX * k_DegreesPerPixel;
            axis.Value = axis.Wrap
                ? WrapIntoRange(value, axis.Range)
                : Mathf.Clamp(value, axis.Range.x, axis.Range.y);

            // Copy-modify-assign: HorizontalAxis is a struct property (same as CameraAutoRotate).
            m_OrbitalFollow.HorizontalAxis = axis;
        }

        void Release()
        {
            m_Dragging = false;
            IsActive = false;
        }

        static float WrapIntoRange(float value, Vector2 range)
        {
            float span = range.y - range.x;
            return span > 0f ? range.x + Mathf.Repeat(value - range.x, span) : range.x;
        }

        void ResolveCamera()
        {
            if (m_OrbitalFollow != null)
            {
                return;
            }

            var cameraGO = GameObject.FindGameObjectWithTag(k_CMCameraTag);
            if (cameraGO != null)
            {
                m_OrbitalFollow = cameraGO.GetComponent<CinemachineOrbitalFollow>();
            }
        }
    }
}
