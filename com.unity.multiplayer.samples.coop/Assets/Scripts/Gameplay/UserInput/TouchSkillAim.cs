using Unity.BossRoom.CameraUtils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.BossRoom.Gameplay.UserInput
{
    /// <summary>
    /// Touch aiming: press a skill button, drag out of it to swing the aim around, release to fire.
    /// The button doubles as a small joystick — the direction is the vector from the button's centre
    /// to the finger, in the camera's basis.
    ///
    /// <para><b>Why this exists.</b> On a phone there was no way to aim at all. Touch counted as
    /// "movement input", so the aim direction was the character's facing, which is the direction it
    /// was walking. A player standing still could not re-aim: their only hope was that the old 80°
    /// soft-lock happened to grab someone, and if it didn't, the shot went into empty ground. This
    /// gives touch the same explicit aim the mouse has.</para>
    ///
    /// <para><b>Instant vs charge-up skills.</b> An instant skill fires on release, so the drag can
    /// aim it first — <see cref="HeroActionBar"/> defers the request for those. A charge-up skill
    /// still fires on press, because pressing is what starts the charge; the drag aims it anyway and
    /// the shot resolves against the live aim when the charge is released
    /// (<c>ServerCharacter.AimDirection</c>).</para>
    ///
    /// <para>Self-bootstrapping like the other touch widgets, so it needs no scene or prefab wiring
    /// and can't be lost to an Editor re-import.</para>
    /// </summary>
    // Before ClientInputSender (0) so the aim is current on the frame it is read.
    [DefaultExecutionOrder(-95)]
    public class TouchSkillAim : MonoBehaviour
    {
        /// <summary>True on a device where this scheme applies at all.</summary>
        public static bool IsAvailable => Touchscreen.current != null || Application.isMobilePlatform;

        /// <summary>True while a skill button is held and the drag has left the dead zone.</summary>
        public static bool IsAiming { get; private set; }

        /// <summary>
        /// The aimed direction, flattened and normalized, or zero when not aiming. Read by
        /// <see cref="ClientInputSender"/> as the highest-priority aim source.
        /// </summary>
        public static Vector3 WorldDirection { get; private set; }

        // Drag distance, as a fraction of the screen's shorter side, before the gesture counts as
        // an aim rather than a tap. Below it a plain tap still fires straight ahead, which is what
        // a player who just wants to attack expects.
        const float k_DeadZoneFraction = 0.03f;

        // Where the finger has to reach for the aim to be at full confidence. Not used to scale the
        // direction — aim is a bearing, not a magnitude — only to size the on-screen indicator.
        const float k_FullThrowFraction = 0.18f;

        // How long the aim stays readable after the finger lifts. A skill request is *queued* on
        // release and only resolved in the next FixedUpdate, so clearing the aim the instant the
        // finger came up would have the shot resolve against nothing and fly straight ahead. The
        // ordering happens to work out without this, but only by accident of Unity's FixedUpdate/
        // Update interleaving, which is not something a control scheme should rest on.
        const float k_ReleaseGraceSeconds = 0.2f;

        static TouchSkillAim s_Instance;

        RectTransform m_ButtonRect;
        Vector2 m_ButtonCenter;
        bool m_Holding;
        float m_Throw;
        float m_GraceUntil;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (!IsAvailable)
            {
                return;
            }

            var go = new GameObject(nameof(TouchSkillAim));
            DontDestroyOnLoad(go);
            go.AddComponent<TouchSkillAim>();
        }

        void Awake()
        {
            s_Instance = this;
        }

        void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
                Clear();
            }
        }

        /// <summary>
        /// Called when a skill button goes down. Anchors the aim joystick on that button.
        /// Harmless to call when this component doesn't exist (desktop): it simply does nothing,
        /// so the action bar doesn't have to branch on the platform beyond checking
        /// <see cref="IsAvailable"/>.
        /// </summary>
        public static void BeginAiming(RectTransform buttonRect)
        {
            if (s_Instance == null)
            {
                return;
            }

            s_Instance.m_ButtonRect = buttonRect;
            s_Instance.m_ButtonCenter = buttonRect != null
                ? RectTransformUtility.WorldToScreenPoint(null, buttonRect.position)
                : Vector2.zero;
            s_Instance.m_Holding = true;
            s_Instance.m_Throw = 0f;

            // Not aiming until the finger has actually travelled: a plain tap must stay a tap.
            IsAiming = false;
            WorldDirection = Vector3.zero;
        }

        /// <summary>
        /// Called when the skill button is released. The aim stays readable for
        /// <see cref="k_ReleaseGraceSeconds"/> so the shot this release is firing can still see it.
        /// </summary>
        public static void EndAiming()
        {
            s_Instance?.Release();
        }

        /// <summary>Stops tracking the finger, but leaves the last aim readable for a moment.</summary>
        void Release()
        {
            m_Holding = false;
            m_ButtonRect = null;
            m_Throw = 0f;
            m_GraceUntil = IsAiming ? Time.unscaledTime + k_ReleaseGraceSeconds : 0f;
        }

        /// <summary>Drops the aim outright, with no grace window.</summary>
        void Clear()
        {
            m_Holding = false;
            m_ButtonRect = null;
            m_Throw = 0f;
            m_GraceUntil = 0f;
            IsAiming = false;
            WorldDirection = Vector3.zero;
        }

        void Update()
        {
            if (!m_Holding)
            {
                // Let a released aim expire, so a later shot can't inherit this one's bearing.
                if (IsAiming && Time.unscaledTime >= m_GraceUntil)
                {
                    IsAiming = false;
                    WorldDirection = Vector3.zero;
                }

                return;
            }

            if (!TryGetActivePointer(out Vector2 pointer))
            {
                // The finger vanished without the button reporting a release — happens on some
                // devices. Go through the same grace window rather than dropping the aim, in case
                // a request for this gesture is still queued.
                Release();
                return;
            }

            // The button can move between frames (the bar re-lays out, the screen rotates), so
            // re-read its centre rather than trusting the one captured on press.
            if (m_ButtonRect != null)
            {
                m_ButtonCenter = RectTransformUtility.WorldToScreenPoint(null, m_ButtonRect.position);
            }

            Vector2 offset = pointer - m_ButtonCenter;
            float shorterSide = Mathf.Min(Screen.width, Screen.height);
            float deadZone = shorterSide * k_DeadZoneFraction;

            if (offset.magnitude < deadZone)
            {
                IsAiming = false;
                WorldDirection = Vector3.zero;
                m_Throw = 0f;
                return;
            }

            Vector3 world = CameraOrbitYaw.ToWorldDirection(offset.normalized);
            if (world.sqrMagnitude < 0.001f)
            {
                return;
            }

            WorldDirection = world;
            IsAiming = true;
            m_Throw = Mathf.Clamp01((offset.magnitude - deadZone) / (shorterSide * k_FullThrowFraction));
        }

        /// <summary>
        /// The screen position of whichever pointer is currently down. Prefers a real touch and
        /// falls back to the mouse, so this still behaves in the Editor with Device Simulator off.
        /// </summary>
        static bool TryGetActivePointer(out Vector2 position)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                foreach (var touch in touchscreen.touches)
                {
                    if (touch.press.isPressed)
                    {
                        position = touch.position.ReadValue();
                        return true;
                    }
                }
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                position = mouse.position.ReadValue();
                return true;
            }

            position = default;
            return false;
        }

        /// <summary>
        /// How far the drag has been pulled, 0..1. For the on-screen indicator only; the aim itself
        /// is a bearing and does not weaken with a shorter drag.
        /// </summary>
        public static float Throw => s_Instance != null ? s_Instance.m_Throw : 0f;
    }
}
