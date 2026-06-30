using UnityEngine;

namespace Unity.BossRoom.Utils
{
    // NetworkSimulator was tied to Unity Multiplayer Tools / NGO transport.
    // Disabled after Mirror migration.
    public class NetworkSimulatorUIMediator : MonoBehaviour
    {
        [SerializeField]
        CanvasGroup m_CanvasGroup;

        void Awake()
        {
            if (m_CanvasGroup != null)
            {
                m_CanvasGroup.alpha = 0f;
                m_CanvasGroup.interactable = false;
                m_CanvasGroup.blocksRaycasts = false;
            }
        }

        public void Hide() { }
    }
}
