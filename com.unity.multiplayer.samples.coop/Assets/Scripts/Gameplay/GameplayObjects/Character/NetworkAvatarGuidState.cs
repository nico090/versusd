using System;
using Mirror;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Infrastructure;
using UnityEngine;
using UnityEngine.Serialization;
using Avatar = Unity.BossRoom.Gameplay.Configuration.Avatar;

namespace Unity.BossRoom.Gameplay.GameplayObjects.Character
{
    /// <summary>
    /// NetworkBehaviour component to send/receive GUIDs from server to clients.
    /// </summary>
    public class NetworkAvatarGuidState : NetworkBehaviour
    {
        [FormerlySerializedAs("AvatarGuidArray")]
        [SyncVar(hook = nameof(OnAvatarGuidChanged))]
        string m_AvatarGuid = "";

        public string AvatarGuid
        {
            get => m_AvatarGuid;
            set => m_AvatarGuid = value;
        }

        [SerializeField]
        AvatarRegistry m_AvatarRegistry;

        Avatar m_Avatar;

        public Avatar RegisteredAvatar
        {
            get
            {
                if (m_Avatar == null)
                {
                    var guid = string.IsNullOrEmpty(m_AvatarGuid) ? Guid.Empty : Guid.Parse(m_AvatarGuid);
                    RegisterAvatar(guid);
                }

                return m_Avatar;
            }
        }

        public void SetRandomAvatar()
        {
            m_AvatarGuid = m_AvatarRegistry.GetRandomAvatar().Guid.ToString();
        }

        void OnAvatarGuidChanged(string oldVal, string newVal)
        {
            var guid = string.IsNullOrEmpty(newVal) ? Guid.Empty : Guid.Parse(newVal);
            RegisterAvatar(guid);
        }

        void RegisterAvatar(Guid guid)
        {
            if (guid.Equals(Guid.Empty))
            {
                // not a valid Guid
                return;
            }

            // based on the Guid received, Avatar is fetched from AvatarRegistry
            if (!m_AvatarRegistry.TryGetAvatar(guid, out var avatar))
            {
                Debug.LogError("Avatar not found!");
                return;
            }

            if (m_Avatar != null)
            {
                // already set, this is an idempotent call, we don't want to Instantiate twice
                return;
            }

            m_Avatar = avatar;

            if (TryGetComponent<ServerCharacter>(out var serverCharacter))
            {
                serverCharacter.CharacterClass = avatar.CharacterClass;
            }
        }
    }
}
