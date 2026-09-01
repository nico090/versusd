using System;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    [RequireComponent(typeof(Image))]
    public class UITinter : MonoBehaviour
    {
        [SerializeField]
        Color[] m_TintColors;
        Image m_Image;

        /// <summary>
        /// The Image, resolved on first use rather than only in Awake.
        /// </summary>
        /// <remarks>
        /// Awake does not run on a GameObject that starts inactive, and these tinters sit on tab
        /// highlights that are hidden until their tab is selected. So a caller that paints the tab
        /// strip during its own Start — which is exactly what <c>SessionUIMediator</c> does to show
        /// the default tab as selected — reached a component whose Awake had never run and threw
        /// on a null Image. A null-conditional at the call site cannot catch that: the component
        /// reference is perfectly valid, it is only its cached field that is empty.
        /// </remarks>
        Image Image => m_Image != null ? m_Image : m_Image = GetComponent<Image>();

        public void SetToColor(int colorIndex)
        {
            if (m_TintColors == null || colorIndex < 0 || colorIndex >= m_TintColors.Length)
                return;

            var image = Image;
            if (image == null)
                return;

            image.color = m_TintColors[colorIndex];
        }
    }
}
