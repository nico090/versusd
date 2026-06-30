using System;
using Mirror;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.BossRoom.Gameplay.GameState;
using Unity.BossRoom.Gameplay.Messages;
using Unity.BossRoom.Infrastructure;
using Unity.BossRoom.Utils;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.Gameplay.GameplayObjects
{
    /// <summary>
    /// Server-only component which publishes a message once the LifeState changes.
    /// </summary>
    [RequireComponent(typeof(NetworkLifeState), typeof(ServerCharacter))]
    public class PublishMessageOnLifeChange : NetworkBehaviour
    {
        NetworkLifeState m_NetworkLifeState;
        ServerCharacter m_ServerCharacter;

        [SerializeField]
        string m_CharacterName;

        NetworkNameState m_NameState;

        [Inject]
        IPublisher<LifeStateChangedEventMessage> m_Publisher;

        void Awake()
        {
            m_NetworkLifeState = GetComponent<NetworkLifeState>();
            m_ServerCharacter = GetComponent<ServerCharacter>();
        }

        public override void OnStartServer()
        {
            m_NameState = GetComponent<NetworkNameState>();
            m_NetworkLifeState.LifeStateChanged += OnLifeStateChanged;

            var gameState = FindAnyObjectByType<ServerBossRoomState>();
            if (gameState != null)
            {
                gameState.Container.Inject(this);
            }
        }

        void OnLifeStateChanged(LifeState previousState, LifeState newState)
        {
            var msg = new LifeStateChangedEventMessage
            {
                CharacterName = m_NameState != null ? m_NameState.Name : m_CharacterName,
                CharacterType = m_ServerCharacter.CharacterClass.CharacterType,
                NewLifeState = newState,
                VictimClientId = m_ServerCharacter.OwnerClientId,
                VictimIsNpc = m_ServerCharacter.IsNpc,
            };

            if (newState == LifeState.Dead || newState == LifeState.Fainted)
            {
                var killer = m_ServerCharacter.LastLethalInflicter;
                if (killer != null)
                {
                    msg.KillerClientId = killer.OwnerClientId;
                    msg.KillerIsNpc = killer.IsNpc;
                }
                else
                {
                    msg.KillerIsNpc = true; // no known attacker → treat as environment/NPC kill
                }
            }

            m_Publisher.Publish(msg);
        }
    }
}
