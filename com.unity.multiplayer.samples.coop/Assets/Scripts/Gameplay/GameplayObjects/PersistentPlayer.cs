using System;
using Mirror;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.BossRoom.Infrastructure;
using Unity.BossRoom.Utils;
using Unity.Multiplayer.Samples.BossRoom;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.GameplayObjects
{
    /// <summary>
    /// NetworkBehaviour that represents a player connection and is the "Default Player Prefab" inside Mirror's
    /// NetworkManager. This NetworkBehaviour will contain several other NetworkBehaviours that
    /// should persist throughout the duration of this connection, meaning it will persist between scenes.
    /// </summary>
    /// <remarks>
    /// It is not necessary to explicitly mark this as a DontDestroyOnLoad object as Mirror will handle migrating this
    /// Player object between scene loads.
    /// </remarks>
    [RequireComponent(typeof(NetworkIdentity))]
    public class PersistentPlayer : NetworkBehaviour
    {
        [SerializeField]
        PersistentPlayerRuntimeCollection m_PersistentPlayerRuntimeCollection;

        [SerializeField]
        NetworkNameState m_NetworkNameState;

        [SerializeField]
        NetworkAvatarGuidState m_NetworkAvatarGuidState;

        public NetworkNameState NetworkNameState => m_NetworkNameState;

        public NetworkAvatarGuidState NetworkAvatarGuidState => m_NetworkAvatarGuidState;

        public ulong OwnerClientId => (ulong)(uint)(connectionToClient?.connectionId ?? 0);

        public override void OnStartServer()
        {
            gameObject.name = "PersistentPlayer" + OwnerClientId;

            // Note that this is done here on OnStartServer in case this NetworkBehaviour's properties are accessed
            // when this element is added to the runtime collection. If this was done in OnEnable() there is a chance
            // that OwnerClientId could be its default value (0).
            m_PersistentPlayerRuntimeCollection.Add(this);

            var sessionPlayerData = SessionManager<SessionPlayerData>.Instance.GetPlayerData(OwnerClientId);
            if (sessionPlayerData.HasValue)
            {
                var playerData = sessionPlayerData.Value;
                m_NetworkNameState.Name = playerData.PlayerName;
                if (playerData.HasCharacterSpawned)
                {
                    m_NetworkAvatarGuidState.AvatarGuid = playerData.AvatarNetworkGuid.ToGuid().ToString();
                }
                else
                {
                    m_NetworkAvatarGuidState.SetRandomAvatar();
                    playerData.AvatarNetworkGuid = Guid.Parse(m_NetworkAvatarGuidState.AvatarGuid).ToNetworkGuid();
                    SessionManager<SessionPlayerData>.Instance.SetPlayerData(OwnerClientId, playerData);
                }
            }
        }

        public override void OnStartClient()
        {
            if (!isServer)
            {
                gameObject.name = "PersistentPlayer" + OwnerClientId;
                m_PersistentPlayerRuntimeCollection.Add(this);
            }
        }

        void OnDestroy()
        {
            RemovePersistentPlayer();
        }

        public override void OnStopServer()
        {
            RemovePersistentPlayer();
        }

        public override void OnStopClient()
        {
            if (!isServer)
            {
                RemovePersistentPlayer();
            }
        }

        void RemovePersistentPlayer()
        {
            m_PersistentPlayerRuntimeCollection.Remove(this);
            if (isServer)
            {
                var sessionPlayerData = SessionManager<SessionPlayerData>.Instance.GetPlayerData(OwnerClientId);
                if (sessionPlayerData.HasValue)
                {
                    var playerData = sessionPlayerData.Value;
                    playerData.PlayerName = m_NetworkNameState.Name;
                    playerData.AvatarNetworkGuid = Guid.Parse(m_NetworkAvatarGuidState.AvatarGuid).ToNetworkGuid();
                    SessionManager<SessionPlayerData>.Instance.SetPlayerData(OwnerClientId, playerData);
                }
            }
        }
    }
}
