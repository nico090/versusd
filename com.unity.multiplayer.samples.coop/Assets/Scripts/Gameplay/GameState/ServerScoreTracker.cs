using Mirror;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.BossRoom.Gameplay.Messages;
using Unity.BossRoom.Infrastructure;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.Gameplay.GameState
{
    /// <summary>
    /// Server-only component that listens to death events and applies the PvPvE deathmatch
    /// scoring rules:
    ///   imp / minor NPC killed by a player → killer +1
    ///   player killed by a player          → killer +5 (+10 during the DoubleKills phase)
    ///   final blow on the boss             → killer +20
    ///   death with no player attacker (boss, imp, environment, self) → nobody scores
    /// Writes results into NetworkGameState so clients see live scores, and pushes one kill-feed
    /// line per scoring kill.
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

            // Nothing scores during the warm-up. Players can't be hurt then (see
            // ServerCharacter.MatchWarmup), but the imps on the map can be, and a player who
            // spends the tutorial farming them would start the match already ahead.
            if (m_NetworkGameState.Phase == MatchPhase.Warmup)
                return;

            // Only a player can score. Everything else — killed by the boss, by an imp, by the
            // environment, or by your own AoE — is worth nothing to anybody. That's the design
            // doc's rule: deaths without an attributable player attacker give no points, and a
            // self-kill must never pay out (it was a free-points exploit when it did).
            //
            // "No attacker" is carried by KillerIsNpc, which PublishMessageOnLifeChange forces to
            // true when the lethal inflicter was null. Do NOT test KillerClientId != 0 instead: in
            // a P2P host game the host player legitimately owns connectionId 0.
            if (msg.KillerIsNpc)
                return;

            if (!msg.VictimIsNpc && msg.KillerClientId == msg.VictimClientId)
                return; // suicide

            string killerName = string.IsNullOrEmpty(msg.KillerName)
                ? m_NetworkGameState.GetPlayerName(msg.KillerClientId)
                : msg.KillerName;
            string victimName = msg.CharacterName;

            if (!msg.VictimIsNpc)
            {
                // Player kill — worth double in the final phase.
                int points = m_NetworkGameState.CurrentPlayerKillValue;
                m_NetworkGameState.AwardKill(msg.KillerClientId, points, 1, 0, false);
                m_NetworkGameState.BroadcastKill(killerName, victimName, points);
            }
            else if (msg.CharacterType == CharacterTypeEnum.ImpBoss)
            {
                // Final blow on the boss. Only the player who lands it scores — that's the whole
                // point of the mechanic, so the kill feed has to say so loudly.
                m_NetworkGameState.AwardKill(msg.KillerClientId, DeathmatchRules.PointsPerBossKill, 0, 0, true);
                m_NetworkGameState.BroadcastKill(killerName, victimName, DeathmatchRules.PointsPerBossKill);
            }
            else
            {
                // Imp / minor NPC. No kill-feed line — at 1 point each they'd flood it.
                m_NetworkGameState.AwardKill(msg.KillerClientId, DeathmatchRules.PointsPerNpcKill, 0, 1, false);
            }
        }
    }
}
