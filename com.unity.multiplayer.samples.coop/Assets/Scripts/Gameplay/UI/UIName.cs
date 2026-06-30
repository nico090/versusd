using System;
using TMPro;
using Unity.BossRoom.Utils;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// UI object that visually represents an object's name. Visuals are updated when NetworkNameState reports a change.
    /// </summary>
    public class UIName : MonoBehaviour
    {
        [SerializeField]
        TextMeshProUGUI m_UINameText;

        NetworkNameState m_NetworkedNameState;

        public void Initialize(NetworkNameState networkedName)
        {
            m_NetworkedNameState = networkedName;

            m_UINameText.text = networkedName.Name;
            networkedName.NameChanged += NameUpdated;
        }

        void NameUpdated(string previousValue, string newValue)
        {
            m_UINameText.text = newValue;
        }

        void OnDestroy()
        {
            if (m_NetworkedNameState != null)
            {
                m_NetworkedNameState.NameChanged -= NameUpdated;
            }
        }
    }
}
