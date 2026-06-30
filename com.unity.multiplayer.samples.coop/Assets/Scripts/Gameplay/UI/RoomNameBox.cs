using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    // Session code display removed — direct IP / Master Server migration.
    public class RoomNameBox : MonoBehaviour
    {
        [SerializeField]
        TextMeshProUGUI m_RoomNameText;
        [SerializeField]
        Button m_CopyToClipboardButton;

        void Awake() { gameObject.SetActive(false); }

        public void CopyToClipboard() { }
    }
}
