using System.Collections.Generic;
using Unity.BossRoom.ConnectionManagement;
using Unity.Multiplayer.Samples.BossRoom;
using Mirror;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.GameplayObjects.Character
{
    [RequireComponent(typeof(ServerCharacter))]
    public class PlayerServerCharacter : NetworkBehaviour
    {
        static List<ServerCharacter> s_ActivePlayers = new List<ServerCharacter>();

        [SerializeField]
        ServerCharacter m_CachedServerCharacter;

        public override void OnStartServer()
        {
            s_ActivePlayers.Add(m_CachedServerCharacter);
        }

        void OnDisable()
        {
            s_ActivePlayers.Remove(m_CachedServerCharacter);
        }

        public override void OnStopServer()
        {
            var movementTransform = m_CachedServerCharacter.Movement.transform;
            SessionPlayerData? sessionPlayerData = SessionManager<SessionPlayerData>.Instance.GetPlayerData(m_CachedServerCharacter.OwnerClientId);
            if (sessionPlayerData.HasValue)
            {
                var playerData = sessionPlayerData.Value;
                playerData.PlayerPosition = movementTransform.position;
                playerData.PlayerRotation = movementTransform.rotation;
                playerData.CurrentHitPoints = m_CachedServerCharacter.HitPoints;
                playerData.HasCharacterSpawned = true;
                SessionManager<SessionPlayerData>.Instance.SetPlayerData(m_CachedServerCharacter.OwnerClientId, playerData);
            }
        }

        public static List<ServerCharacter> GetPlayerServerCharacters() => s_ActivePlayers;

        public static ServerCharacter GetPlayerServerCharacter(ulong ownerClientId)
        {
            foreach (var sc in s_ActivePlayers)
            {
                if (sc.OwnerClientId == ownerClientId)
                    return sc;
            }
            return null;
        }
    }
}
