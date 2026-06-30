using Mirror;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.BossRoom.Gameplay.Messages;
using Unity.BossRoom.Infrastructure;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.Gameplay.GameState
{
    /// <summary>
    /// Server-only component that listens to death events and applies the deathmatch
    /// scoring rules:
    ///   PC killed by PC  → killer +3
    ///   NPC killed by PC → killer +1
    ///   PC killed by NPC (or no attacker) → victim -3
    /// Writes results into NetworkGameState so clients see live scores.
    /// Must live on the same GameObject as ServerBossRoomState and NetworkGameState.
    /// </summary>
    [RequireComponent(typeof(NetworkGameState))]
    public class ServerScoreTracker : NetworkBehaviour
    {
        NetworkGameState m_NetworkGameState;

        [Inject]
        ISubscriber<LifeStateChangedEventMessage> m_LifeStateSubscriber;

        void Awake()
        {
            m_NetworkGameState = GetComponent<NetworkGameState>();
        }

        public override void OnStartServer()
        {
            var gameState = GetComponent<ServerBossRoomState>();
            if (gameState != null)
                gameState.Container.Inject(this);

            m_LifeStateSubscriber?.Subscribe(OnDeath);
        }

        public override void OnStopServer()
        {
            m_LifeStateSubscriber?.Unsubscribe(OnDeath);
        }

        void OnDeath(LifeStateChangedEventMessage msg)
        {
            if (msg.NewLifeState != LifeState.Dead && msg.NewLifeState != LifeState.Fainted)
                return;

            bool victimIsNpc = msg.VictimIsNpc;
            bool killerIsNpc = msg.KillerIsNpc;
            ulong killerClientId = msg.KillerClientId;
            ulong victimClientId = msg.VictimClientId;

            if (!victimIsNpc && !killerIsNpc)
            {
                // PC killed by PC → killer +3
                m_NetworkGameState.ApplyScoreDelta(killerClientId, 3);
            }
            else if (victimIsNpc && !killerIsNpc)
            {
                // NPC killed by PC → killer +1
                m_NetworkGameState.ApplyScoreDelta(killerClientId, 1);
            }
            else if (!victimIsNpc && (killerIsNpc || killerClientId == 0))
            {
                // PC killed by NPC or environment → victim -3
                m_NetworkGameState.ApplyScoreDelta(victimClientId, -3);
            }
            // NPC killed by NPC: no score change
        }
    }
}
