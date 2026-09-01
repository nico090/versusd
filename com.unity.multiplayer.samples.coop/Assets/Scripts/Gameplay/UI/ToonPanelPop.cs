using UnityEngine;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Pops a menu card in when it appears: a short scale-up that overshoots and settles.
    /// Added by <see cref="ToonMenuRestyler"/> to top-level panels only.
    /// </summary>
    /// <remarks>
    /// This is the cheapest way to make a menu feel alive. A panel that simply blinks into
    /// existence reads as a state change; one that arrives reads as a thing. It only ever writes
    /// <c>localScale</c>, so layout, hit-testing and the panel's own show/hide logic are untouched
    /// — and because it keys off <c>OnEnable</c>, it works for panels shown by
    /// <c>SetActive</c> without anyone having to call it.
    /// </remarks>
    [DisallowMultipleComponent]
    public class ToonPanelPop : MonoBehaviour
    {
        const float k_Duration = 0.22f;
        const float k_StartScale = 0.94f;

        // Overshoot strength of the classic "back" ease. 1.7 is the textbook value; it lands about
        // 4% past the target, which at this duration reads as a bounce and not as a wobble.
        const float k_Overshoot = 1.7f;

        Vector3 m_BaseScale = Vector3.one;
        bool m_HasBaseScale;
        float m_Elapsed;

        void Awake()
        {
            CaptureBaseScale();
        }

        void CaptureBaseScale()
        {
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
            m_Elapsed = 0f;
            Apply(0f);
        }

        void OnDisable()
        {
            // Left mid-animation, the panel would be re-shown at whatever scale it froze at, and
            // anything measuring it in the meantime would measure the wrong size.
            transform.localScale = m_BaseScale;
        }

        void Update()
        {
            if (m_Elapsed >= k_Duration)
            {
                return;
            }

            // Menus run at timeScale 0 often enough (pause, post-game) that scaled time would
            // leave every panel stuck at its start scale.
            m_Elapsed += Time.unscaledDeltaTime;
            Apply(Mathf.Clamp01(m_Elapsed / k_Duration));
        }

        void Apply(float t)
        {
            float eased = EaseOutBack(t);
            transform.localScale = m_BaseScale * Mathf.LerpUnclamped(k_StartScale, 1f, eased);
        }

        static float EaseOutBack(float t)
        {
            float c = t - 1f;
            return 1f + (k_Overshoot + 1f) * c * c * c + k_Overshoot * c * c;
        }
    }
}
