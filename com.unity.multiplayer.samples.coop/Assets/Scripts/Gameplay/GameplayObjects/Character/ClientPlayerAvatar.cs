using System;
using Mirror;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.GameplayObjects.Character
{
    public class ClientPlayerAvatar : NetworkBehaviour
    {
        [SerializeField]
        ClientPlayerAvatarRuntimeCollection m_PlayerAvatars;

        public static event Action<ClientPlayerAvatar> LocalClientSpawned;

        public static event Action LocalClientDespawned;

        public override void OnStartClient()
        {
            name = "PlayerAvatar" + (ulong)(uint)(connectionToClient?.connectionId ?? 0);

            if (isOwned)
            {
                LocalClientSpawned?.Invoke(this);
            }

            if (m_PlayerAvatars)
            {
                m_PlayerAvatars.Add(this);
            }
        }

        public override void OnStopClient()
        {
            if (isOwned)
            {
                LocalClientDespawned?.Invoke();
            }

            RemoveNetworkCharacter();
        }

        void OnDestroy()
        {
            RemoveNetworkCharacter();
        }

        void RemoveNetworkCharacter()
        {
            if (m_PlayerAvatars)
            {
                m_PlayerAvatars.Remove(this);
            }
        }
    }
}
