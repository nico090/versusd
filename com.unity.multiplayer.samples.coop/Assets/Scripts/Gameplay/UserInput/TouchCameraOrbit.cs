using System.Collections.Generic;
using Mirror;
using Unity.BossRoom.CameraUtils;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UserInput
{
    /// <summary>
    /// Touch camera rotation: drag anywhere on the right half of the screen that isn't a character
    /// and the camera swings around the player. The touch counterpart of
    /// <see cref="MouseCameraOrbit"/> (middle-mouse drag on desktop).
    ///
    /// <para><b>The camera never moves on its own.</b> There used to be an auto-rotation that swung
    /// it to follow the walk, and it is gone. It had to keep the movement basis frozen for the
    /// length of a swing — otherwise the walk curved, because movement is camera-relative and a held
    /// sideways input would keep redefining "sideways" — and a frozen basis is exactly what made
    /// walking right come out as walking forward once the camera had turned. Manual rotation has no
    /// such problem: the basis is read live from the orbit yaw (<c>CameraOrbitYaw</c>), so the stick
    /// stays glued to the screen no matter where the camera is pointing.</para>
    ///
    /// <para><b>What it will not claim.</b> A press is only taken as a camera drag if it lands on
    /// nothing that matters:</para>
    /// <list type="bullet">
    /// <item>Left half of the screen — that belongs to the movement joystick, and the split is read
    /// from <see cref="MobileMovementJoystick.MovementZoneWidthFraction"/> rather than duplicated.</item>
    /// <item>The zoom bar's grab area, asked of <see cref="MobileZoomBar.OwnsScreenPoint"/>.</item>
    /// <item>Interactive UI — buttons, sliders, anything <see cref="Selectable"/> — so the action bar
    /// still works. Decorative graphics deliberately do not count; see
    /// <see cref="IsOverInteractiveUI"/>, which is where the first version of this went wrong.</item>
    /// <item>A player or an enemy: if the ray hits a <see cref="ServerCharacter"/>, the press is a
    /// tap-to-select and is left alone. This is the rule the feature was asked for.</item>
    /// </list>
    ///
    /// <para>Note that a press on empty ground still reaches <c>ClientInputSender</c>'s tap-to-select
    /// on the frame it happens (selection fires on press, before any drag exists), so beginning a
    /// drag also picks the nearest enemy. Harmless — selecting is not attacking — and the
    /// alternative, deferring selection to release, would put a lag on every tap.</para>
    ///
    /// Only the horizontal axis is touched: the vertical one is zoom, and it has the bar.
    /// Self-bootstrapping like the other touch widgets, so it needs no scene or prefab wiring.
    /// </summary>
    // Before ClientInputSender (0) so IsActive is current when it runs, after
    // nothing else polls raw touches before it.
    [DefaultExecutionOrder(-90)]
    public class TouchCameraOrbit : MonoBehaviour
    {
        const string k_CMCameraTag = "CMCamera";

        /// <summary>True while the camera is being dragged, for anything that wants to keep out of the way.</summary>
        public static bool IsActive { get; private set; }

        // A drag right across the screen is a full turn. Expressed against the screen width rather
        // than in degrees-per-pixel (what MouseCameraOrbit uses) because touch travel is bounded by
        // the display: a fixed per-pixel rate that feels right on a phone would make the same
        // physical thumb sweep on a tablet turn the camera much further.
        const float k_DegreesPerScreenWidth = 360f;

        // Slack before a press counts as a drag, so the wobble in a tap never nudges the camera.
        const float k_DragThresholdFraction = 0.01f;

        const float k_RaycastDistance = 100f;
        static readonly RaycastHit[] k_Hits = new RaycastHit[8];
        static readonly List<RaycastResult> k_UIResults = new List<RaycastResult>();

        // TEMPORARY: reports which gate turned a right-half press away, for working out why a drag
        // isn't taking on device. Reads the same as UIClickDiagnostics and should go the same way —
        // delete once the touch camera is confirmed working. Deliberately not Debug.isDebugBuild:
        // the iOS build is a release one, so keying off that logged nothing exactly where the
        // answer was needed. One line per press on the right half is not worth metering.
        const bool k_Diagnostics = true;

        CinemachineOrbitalFollow m_OrbitalFollow;
        Camera m_Camera;

        int m_ActiveTouchId = -1;
        Vector2 m_LastPosition;
        bool m_PastThreshold;
        float m_TravelSincePress;
        float m_NextStateLog;   // TEMPORARY, with k_Diagnostics.

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Touchscreen.current == null && !Application.isMobilePlatform)
            {
                return;
            }

            var go = new GameObject(nameof(TouchCameraOrbit));
            DontDestroyOnLoad(go);
            go.AddComponent<TouchCameraOrbit>();
        }

        void Update()
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null || NetworkClient.localPlayer == null)
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

            LogState(touchscreen);

            if (m_ActiveTouchId == -1)
            {
                TryBeginTouch(touchscreen);
            }
            else
            {
                ContinueTouch(touchscreen);
            }
        }

        // TEMPORARY, with k_Diagnostics. Once a second, so a single run on device answers both
        // "is anything still driving the yaw on its own" and "did my finger ever get claimed".
        void LogState(Touchscreen touchscreen)
        {
            if (!k_Diagnostics || Time.unscaledTime < m_NextStateLog)
            {
                return;
            }
            m_NextStateLog = Time.unscaledTime + 1f;

            int pressed = 0;
            foreach (var touch in touchscreen.touches)
            {
                if (touch.press.isPressed)
                {
                    pressed++;
                }
            }

            Debug.Log($"[OrbitDiag] state: yaw={m_OrbitalFollow.HorizontalAxis.Value:F1} " +
                      $"dragging={IsActive} activeTouch={m_ActiveTouchId} fingersDown={pressed} " +
                      $"screen={Screen.width}x{Screen.height}");
        }

        void TryBeginTouch(Touchscreen touchscreen)
        {
            foreach (var touch in touchscreen.touches)
            {
                if (!touch.press.wasPressedThisFrame)
                {
                    continue;
                }

                Vector2 pos = touch.position.ReadValue();
                int touchId = (int)touch.touchId.ReadValue();

                if (pos.x <= Screen.width * MobileMovementJoystick.MovementZoneWidthFraction)
                {
                    continue;
                }
                if (MobileZoomBar.OwnsScreenPoint(pos))
                {
                    Reject(touchId, pos, "zoom bar owns the point");
                    continue;
                }
                if (IsOverInteractiveUI(pos))
                {
                    Reject(touchId, pos, "point is over interactive UI");
                    continue;
                }
                if (HitsCharacter(pos))
                {
                    Reject(touchId, pos, "ray hit a ServerCharacter (tap-to-select)");
                    continue;
                }

                if (k_Diagnostics)
                {
                    Debug.Log($"[OrbitDiag] touch {touchId} at {pos} CLAIMED (screen {Screen.width}x{Screen.height})");
                }

                m_ActiveTouchId = touchId;
                m_LastPosition = pos;
                m_PastThreshold = false;
                m_TravelSincePress = 0f;
                return;
            }
        }

        void ContinueTouch(Touchscreen touchscreen)
        {
            foreach (var touch in touchscreen.touches)
            {
                if ((int)touch.touchId.ReadValue() != m_ActiveTouchId)
                {
                    continue;
                }

                if (!touch.press.isPressed)
                {
                    Release();
                    return;
                }

                Vector2 pos = touch.position.ReadValue();
                float deltaX = pos.x - m_LastPosition.x;
                m_TravelSincePress += (pos - m_LastPosition).magnitude;
                m_LastPosition = pos;

                if (!m_PastThreshold)
                {
                    if (m_TravelSincePress < Screen.width * k_DragThresholdFraction)
                    {
                        return;
                    }
                    m_PastThreshold = true;
                }

                IsActive = true;

                Rotate(deltaX);
                return;
            }

            // The touch vanished without a clean release (happens on some devices).
            Release();
        }

        /// <summary>Drag right swings the camera right, matching the desktop mouse-look convention.</summary>
        void Rotate(float deltaX)
        {
            if (Mathf.Abs(deltaX) < 0.001f || Screen.width <= 0)
            {
                return;
            }

            var axis = m_OrbitalFollow.HorizontalAxis;
            float value = axis.Value + deltaX / Screen.width * k_DegreesPerScreenWidth;
            axis.Value = axis.Wrap
                ? WrapIntoRange(value, axis.Range)
                : Mathf.Clamp(value, axis.Range.x, axis.Range.y);

            // Copy-modify-assign: HorizontalAxis is a struct property (same as MouseCameraOrbit).
            m_OrbitalFollow.HorizontalAxis = axis;
        }

        /// <summary>
        /// Whether the press landed on a player or an enemy, in which case it belongs to
        /// tap-to-select. Raycast against everything and look for a <see cref="ServerCharacter"/> in
        /// the parents, rather than against a layer mask: this component is bootstrapped rather than
        /// placed in a scene, so it has no serialized mask to be configured with, and the component
        /// is the thing being asked about anyway.
        /// </summary>
        bool HitsCharacter(Vector2 screenPos)
        {
            if (m_Camera == null)
            {
                return false;
            }

            var ray = m_Camera.ScreenPointToRay(screenPos);
            int numHits = Physics.RaycastNonAlloc(ray, k_Hits, k_RaycastDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < numHits; i++)
            {
                if (k_Hits[i].transform.GetComponentInParent<ServerCharacter>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        // TEMPORARY, with k_Diagnostics.
        static void Reject(int touchId, Vector2 pos, string reason)
        {
            if (k_Diagnostics)
            {
                Debug.Log($"[OrbitDiag] touch {touchId} at {pos} REJECTED: {reason}");
            }
        }

        void Release()
        {
            m_ActiveTouchId = -1;
            m_PastThreshold = false;
            m_TravelSincePress = 0f;
            IsActive = false;
        }

        /// <summary>
        /// Whether the point is over UI the player could actually be aiming at — a button, a slider,
        /// anything <see cref="Selectable"/>.
        ///
        /// <para>Deliberately not <c>EventSystem.IsPointerOverGameObject(touchId)</c>, which is what
        /// the other touch widgets use. Two reasons, and both of them cost a working camera drag on
        /// device before this was changed. First, that overload answers for a pointer the UI module
        /// has already processed and keyed by <i>its</i> id, which is not reliably the raw touch id
        /// for a second finger — the first finger works, the second reads as an unknown pointer.
        /// Asking positionally sidesteps the bookkeeping entirely. Second, it answers "is any
        /// raycastable graphic here", and a single full-screen Image with <c>raycastTarget</c> left
        /// on — the classic HUD slip, and this project has a whole diagnostic
        /// (<c>UIClickDiagnostics</c>) written for that class of problem — turns the whole screen
        /// into UI and blocks every drag. A decorative panel should not eat the camera; a button
        /// should.</para>
        /// </summary>
        static bool IsOverInteractiveUI(Vector2 screenPos)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            var pointerData = new PointerEventData(eventSystem) { position = screenPos };
            k_UIResults.Clear();
            eventSystem.RaycastAll(pointerData, k_UIResults);

            for (int i = 0; i < k_UIResults.Count; i++)
            {
                var hit = k_UIResults[i].gameObject;
                if (hit != null && hit.GetComponentInParent<Selectable>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        static float WrapIntoRange(float value, Vector2 range)
        {
            float span = range.y - range.x;
            return span > 0f ? range.x + Mathf.Repeat(value - range.x, span) : range.x;
        }

        void ResolveCamera()
        {
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
            }

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
