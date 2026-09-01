using TMPro;
using Unity.BossRoom.MasterServer;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// One room in the public room list: its name, how full it is, and whether it is private.
    /// </summary>
    public class SessionListItemUI : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI m_SessionNameText;
        [SerializeField] TextMeshProUGUI m_SessionCountText;
        [SerializeField] GameObject m_LockIcon;

        LobbyResponse m_Lobby;
        SessionJoiningUI m_JoiningUI;

        public void SetData(LobbyResponse lobby, SessionJoiningUI joiningUI)
        {
            m_Lobby = lobby;
            m_JoiningUI = joiningUI;

            if (m_SessionNameText) m_SessionNameText.SetText(lobby.name);

            if (m_SessionCountText)
            {
                m_SessionCountText.SetText($"{lobby.current_players}/{lobby.max_players}");
                // A room you cannot enter and a room with one seat left look identical as a pair
                // of numbers, so the number carries the answer as well: red is full, amber is
                // nearly full, green has room.
                m_SessionCountText.color = SeatsColor(lobby.current_players, lobby.max_players);
                m_SessionCountText.fontStyle |= FontStyles.Bold;
            }

            if (m_LockIcon)
            {
                m_LockIcon.SetActive(lobby.is_private);
                StyleLock();
            }
        }

        static Color SeatsColor(int current, int max)
        {
            if (max <= 0 || current >= max)
            {
                return UIKit.Danger;
            }

            return current >= max - 1 ? UIKit.Gold : UIKit.Positive;
        }

        /// <summary>
        /// Gives the prefab's padlock this project's own icon. Done here rather than in the prefab
        /// because prefab edits do not reliably reach a build from this project (see
        /// <see cref="ToonMenuRestyler"/>), and idempotent because a row is re-used every refresh.
        /// </summary>
        void StyleLock()
        {
            var image = m_LockIcon.GetComponent<Image>();
            if (image == null)
            {
                return;
            }

            var sprite = UIIcons.Get(UIIcons.Icon.Lock);
            if (image.sprite == sprite)
            {
                return;
            }

            image.sprite = sprite;
            image.color = UIKit.Gold;
            image.preserveAspect = true;
        }

        public void OnClick()
        {
            m_JoiningUI?.OnLobbySelected(m_Lobby);
        }
    }
}
