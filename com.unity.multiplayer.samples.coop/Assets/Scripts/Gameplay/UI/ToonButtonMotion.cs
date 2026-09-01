using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Makes a menu button feel like a physical thing: it swells slightly under the pointer, its
    /// accent contour lights up, it squashes while held, and it springs back when released.
    /// Added by <see cref="ToonMenuSkin.StyleButton"/>; never authored in a prefab.
    /// </summary>
    /// <remarks>
    /// <para>The scale runs through a spring rather than a lerp because the overshoot on release
    /// is the whole point — a linear tween back to 1 reads as a fade, a spring reads as a button
    /// popping back up. Everything ticks on unscaled time: menus are shown at
    /// <c>Time.timeScale == 0</c> often enough (pause, post-game) that scaled time would freeze
    /// the feedback exactly when the player is clicking.</para>
    ///
    /// <para>It drives <c>localScale</c> only, so it can never disturb a layout group or shift a
    /// neighbouring widget — the worst it can do is draw the same button a few percent larger.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public class ToonButtonMotion : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        const float k_HoverGrowth = 0.05f;
        const float k_PressSquash = 0.10f;

        // Stiff enough to arrive in about a tenth of a second, damped just short of critical so
        // there is one visible overshoot and no wobble.
        const float k_Stiffness = 260f;
        const float k_Damping = 15f;

        const float k_AccentFadeSpeed = 9f;

        Selectable m_Selectable;
        Graphic m_AccentRing;

        Vector3 m_BaseScale = Vector3.one;
        bool m_HasBaseScale;

        bool m_Hovered;
        bool m_Pressed;

        float m_Scale = 1f;
        float m_ScaleVelocity;
        float m_AccentAlpha;

        /// <summary>
        /// Points the motion at the widget it belongs to. Called again on every restyle pass, so
        /// it has to stay idempotent.
        /// </summary>
        public void Bind(Selectable selectable, Graphic accentRing)
        {
            m_Selectable = selectable;
            m_AccentRing = accentRing;
            CaptureBaseScale();
        }

        void CaptureBaseScale()
        {
            // Capture once, and only from a scale we have not touched yet — re-reading it later
            // would fold our own animation into the rest pose and let the button creep.
            if (m_HasBaseScale)
            {
                return;
            }

            m_BaseScale = transform.localScale;
            m_HasBaseScale = true;
        }

        void OnEnable()
        {
            CaptureBaseScale();
            ResetPose();
        }

        void OnDisable()
        {
            // A button hidden while hovered never receives its pointer-exit, so it would come back
            // swollen. Clearing here is what makes hiding and re-showing a panel safe.
            m_Hovered = false;
            m_Pressed = false;
            ResetPose();
        }

        void ResetPose()
        {
            m_Scale = 1f;
            m_ScaleVelocity = 0f;
            m_AccentAlpha = 0f;

            transform.localScale = m_BaseScale;
            ApplyAccent();
        }

        bool Interactable => m_Selectable == null || m_Selectable.IsInteractable();

        void Update()
        {
            bool hovered = m_Hovered && Interactable;
            bool pressed = m_Pressed && Interactable;

            // A menu can hold a few dozen of these. Once a button has settled back to rest there
            // is nothing left to integrate, so it costs four comparisons a frame instead of a
            // spring step and a transform write.
            if (!hovered && !pressed
                && Mathf.Abs(m_Scale - 1f) < 0.001f
                && Mathf.Abs(m_ScaleVelocity) < 0.001f
                && m_AccentAlpha <= 0f)
            {
                return;
            }

            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            float target = 1f + (hovered ? k_HoverGrowth : 0f) - (pressed ? k_PressSquash : 0f);

            m_ScaleVelocity += (target - m_Scale) * k_Stiffness * deltaTime;
            m_ScaleVelocity *= Mathf.Exp(-k_Damping * deltaTime);
            m_Scale += m_ScaleVelocity * deltaTime;

            transform.localScale = m_BaseScale * m_Scale;

            float accentTarget = hovered ? 0.9f : 0f;
            m_AccentAlpha = Mathf.MoveTowards(m_AccentAlpha, accentTarget, k_AccentFadeSpeed * deltaTime);
            ApplyAccent();
        }

        void ApplyAccent()
        {
            if (m_AccentRing == null)
            {
                return;
            }

            var color = m_AccentRing.color;
            m_AccentRing.color = new Color(color.r, color.g, color.b, m_AccentAlpha);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            m_Hovered = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            m_Hovered = false;
            m_Pressed = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            m_Pressed = true;
            // Touch never sends an enter, so without this a tap would squash without ever lighting
            // the contour up.
            m_Hovered = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            m_Pressed = false;

            // On touch there is no pointer to keep hovering, so the button has to fall back to
            // rest by itself once the finger is gone.
            if (eventData != null && eventData.pointerId >= 0)
            {
                m_Hovered = false;
            }
        }
    }
}
