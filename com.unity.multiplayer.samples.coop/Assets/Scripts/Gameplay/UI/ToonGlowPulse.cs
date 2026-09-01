using UnityEngine;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Breathes the alpha of a glow image up and down. Used behind the game logo so the title
    /// looks lit rather than printed.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class ToonGlowPulse : MonoBehaviour
    {
        const float k_PeriodSeconds = 3.4f;

        [SerializeField] float m_MinAlpha = 0.18f;
        [SerializeField] float m_MaxAlpha = 0.42f;

        Image m_Image;

        /// <summary>Sets the range the pulse swings between, for callers that build this at runtime.</summary>
        public void SetRange(float minAlpha, float maxAlpha)
        {
            m_MinAlpha = minAlpha;
            m_MaxAlpha = maxAlpha;
        }

        void Awake()
        {
            m_Image = GetComponent<Image>();
        }

        void Update()
        {
            // Unscaled: the menus this runs in are as likely as not to be sitting at timeScale 0.
            float wave = (Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / k_PeriodSeconds)) + 1f) * 0.5f;
            var color = m_Image.color;
            m_Image.color = new Color(color.r, color.g, color.b, Mathf.Lerp(m_MinAlpha, m_MaxAlpha, wave));
        }
    }
}
