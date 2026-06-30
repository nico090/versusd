using TMPro;
using Unity.BossRoom.MasterServer;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.UI
{
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
            if (m_SessionCountText) m_SessionCountText.SetText($"{lobby.current_players}/{lobby.max_players}");
            if (m_LockIcon) m_LockIcon.SetActive(lobby.is_private);
        }

        public void OnClick()
        {
            m_JoiningUI?.OnLobbySelected(m_Lobby);
        }
    }
}
