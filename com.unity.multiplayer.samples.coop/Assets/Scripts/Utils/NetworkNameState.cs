using System;
using Mirror;
using UnityEngine;

namespace Unity.BossRoom.Utils
{
    /// <summary>
    /// NetworkBehaviour containing only one SyncVar string which represents this object's name.
    /// </summary>
    public class NetworkNameState : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnNameChanged))]
        string m_Name;

        [HideInInspector]
        public string Name
        {
            get => m_Name;
            set => m_Name = value;
        }

        public event Action<string, string> NameChanged;

        void OnNameChanged(string oldVal, string newVal)
        {
            NameChanged?.Invoke(oldVal, newVal);
        }
    }

    /// <summary>
    /// Wrapping string so that if we want to change player name max size in the future, we only do it once here.
    /// Kept for caller compatibility — implicit conversions to/from string are preserved.
    /// </summary>
    public struct FixedPlayerName
    {
        string m_Name;

        public override string ToString()
        {
            return m_Name ?? string.Empty;
        }

        public static implicit operator string(FixedPlayerName s) => s.ToString();
        public static implicit operator FixedPlayerName(string s) => new FixedPlayerName() { m_Name = s };
    }
}
