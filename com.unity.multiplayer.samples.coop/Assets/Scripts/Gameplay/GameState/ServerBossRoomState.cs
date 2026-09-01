using System.Collections;
using System.Collections.Generic;
using Mirror;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.DedicatedServer;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.BossRoom.Gameplay.Messages;
using Unity.BossRoom.Infrastructure;
using Unity.BossRoom.Utils;
using Unity.Multiplayer.Samples.BossRoom;
using Unity.Multiplayer.Samples.Utilities;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Serialization;
using VContainer;
using Random = UnityEngine.Random;

namespace Unity.BossRoom.Gameplay.GameState
{
    /// <summary>
    /// Server specialization of core BossRoom game logic.
    /// </summary>
    [RequireComponent(typeof(NetcodeHooks), typeof(NetworkGameState), typeof(ServerScoreTracker))]
    public class ServerBossRoomState : GameStateBehaviour
    {
        [FormerlySerializedAs("m_NetworkWinState")]
        [SerializeField]
        PersistentGameState persistentGameState;

        public NetworkGameState networkGameState { get; private set; }

        [SerializeField]
        NetcodeHooks m_NetcodeHooks;

        [SerializeField]
        [Tooltip("Make sure this is included in the NetworkManager's list of prefabs!")]
        private GameObject m_PlayerPrefab;

        [SerializeField]
        [Tooltip("A collection of locations for spawning players")]
        private Transform[] m_PlayerSpawnPoints;

        private List<Transform> m_PlayerSpawnPointsList = null;

        public override GameState ActiveState { get { return GameState.BossRoom; } }

        private const float k_WinDelay = DeathmatchRules.PostMatchDelay;
        private const float k_RespawnDelay = DeathmatchRules.RespawnDelay;
        // Brief spawn protection so a freshly respawned player can't be instantly
        // re-killed at the spawn point (spawn-camping). Server-authoritative, and cancelled the
        // moment the player throws a punch — see ServerCharacter.CancelInvulnerability.
        private const float k_RespawnInvulnerability = DeathmatchRules.RespawnInvulnerability;

        /// <summary>
        /// Has the ServerBossRoomState already hit its initial spawn? (i.e. spawned players following load from character select).
        /// </summary>
        public bool InitialSpawnDone { get; private set; }

        bool m_ServerInitialized;
        bool m_MatchEnded;
        ServerBossSpawner m_BossSpawner;
        ServerZoneSpawner m_ZoneSpawner;

        /// <summary>
        /// Keeping the subscriber during this GameState's lifetime to allow disposing of subscription and re-subscribing
        /// when despawning and spawning again.
        /// </summary>
        [Inject] ISubscriber<LifeStateChangedEventMessage> m_LifeStateChangedEventMessageSubscriber;

        [Inject] ConnectionManager m_ConnectionManager;
        [Inject] PersistentGameState m_PersistentGameState;

        protected override void Awake()
        {
            base.Awake();
            networkGameState = GetComponent<NetworkGameState>();
            m_NetcodeHooks.OnNetworkSpawnHook += OnNetworkSpawn;
            m_NetcodeHooks.OnNetworkDespawnHook += OnNetworkDespawn;
        }

        void OnNetworkSpawn()
        {
            if (!m_NetcodeHooks.isServer || m_ServerInitialized)
            {
                if (!m_NetcodeHooks.isServer) enabled = false;
                return;
            }
            m_ServerInitialized = true;
            m_MatchEnded = false;
            m_PersistentGameState.Reset();
            m_LifeStateChangedEventMessageSubscriber.Subscribe(OnLifeStateChangedEventMessage);

            NetworkServer.OnConnectedEvent += OnServerClientConnected;
            NetworkServer.OnDisconnectedEvent += OnServerClientDisconnected;

            SessionManager<SessionPlayerData>.Instance.OnSessionStarted();

            // Mirror equivalent of NGO's OnLoadEventCompleted: the server has finished loading the
            // scene so spawn a character for every already-connected client.
            InitialSpawnDone = true;
            foreach (var conn in NetworkServer.connections.Values)
            {
                SpawnPlayer((ulong)(uint)conn.connectionId, false);
            }

            // Players are in — start the clock. The spawner arms itself now but holds the boss back
            // until the last two minutes of the match.
            ServerCharacter.MatchInputFrozen = false;
            networkGameState.StartMatch();
            m_BossSpawner = ServerBossSpawner.Create(m_PlayerSpawnPoints, networkGameState);
            m_ZoneSpawner = ServerZoneSpawner.Create(m_PlayerSpawnPoints, networkGameState);
        }

        void OnNetworkDespawn()
        {
            if (!m_ServerInitialized) return;
            m_ServerInitialized = false;

            if (m_BossSpawner != null)
            {
                Destroy(m_BossSpawner.gameObject);
                m_BossSpawner = null;
            }

            if (m_ZoneSpawner != null)
            {
                // Its OnDestroy clears both the replicated list and the static boon table, so no
                // zone and no boon can survive into the next match.
                Destroy(m_ZoneSpawner.gameObject);
                m_ZoneSpawner = null;
            }
            ServerCharacter.MatchInputFrozen = false;

            if (m_LifeStateChangedEventMessageSubscriber != null)
            {
                m_LifeStateChangedEventMessageSubscriber.Unsubscribe(OnLifeStateChangedEventMessage);
            }

            NetworkServer.OnConnectedEvent -= OnServerClientConnected;
            NetworkServer.OnDisconnectedEvent -= OnServerClientDisconnected;

            // The symmetric half of the OnSessionStarted() above. It used to live only in
            // ServerPostGameState, which means the session was only ever ended when a match ran
            // all the way to the results screen — leave from the pause menu, lose the host, or
            // have the server change scene for any other reason and it never closed.
            //
            // That is not cosmetic. SessionManager.DisconnectClient keeps a player's data while
            // m_HasSessionStarted is true, so they can reconnect mid-match. With the flag stuck on,
            // the NEXT match treats everyone as a reconnection: PersistentPlayer sees
            // HasCharacterSpawned and reuses the old avatar guid instead of taking the one just
            // chosen, and ServerBossRoomState restores the previous match's position. The player
            // ends up with no character they can see. Ending the session here reinitialises that
            // data whatever route the match ended by.
            //
            // Safe to run twice: PostGame still calls it, and both halves are idempotent.
            SessionManager<SessionPlayerData>.Instance.OnSessionEnded();
        }

        void OnServerClientConnected(NetworkConnectionToClient conn)
        {
            // Late-join: spawn only if the initial wave is done and this player has no character yet.
            if (InitialSpawnDone && !PlayerServerCharacter.GetPlayerServerCharacter((ulong)(uint)conn.connectionId))
            {
                SpawnPlayer((ulong)(uint)conn.connectionId, true);
            }
        }

        void OnServerClientDisconnected(NetworkConnectionToClient conn)
        {
            // Remove the leaver from the live scoreboard so they can't keep a slot — or
            // "win" the match — while no longer connected.
            //
            // DESIGN NOTE (PLAN_1.0 BUG-1): this also means a player who drops for even a
            // moment loses their score. That's an accepted trade-off *because the scoreboard
            // is keyed by Mirror connectionId, which changes on reconnect* — so the score
            // couldn't survive a reconnect anyway. To make scores durable across brief
            // drops, re-key ScoreEntry/ApplyScoreDelta by the stable master-server PlayerId
            // and mark entries connected/disconnected instead of deleting them. Left as
            // follow-up: it's a networked-struct + reconnection change that needs in-game
            // validation.
            if (networkGameState != null)
            {
                networkGameState.RemovePlayer((ulong)(uint)conn.connectionId);
            }
        }

        void Update()
        {
            if (!m_ServerInitialized || m_MatchEnded || networkGameState == null) return;

            // Free-for-all: the clock is the only end condition. Whoever has the most points when
            // it hits zero wins (ties broken by player kills — see ScoreEntry.CompareForRanking).
            if (networkGameState.Phase == MatchPhase.Ended)
            {
                m_MatchEnded = true;
                FreezeAllPlayers();
                StartCoroutine(CoroGameOver(k_WinDelay));
            }
        }

        /// <summary>
        /// Server-only: stop everyone dead when the match ends, so nobody can squeeze in a kill
        /// while the final table is on screen. Movement and running actions are cancelled here and
        /// new input is rejected at the ServerCharacter command handlers.
        /// </summary>
        void FreezeAllPlayers()
        {
            ServerCharacter.MatchInputFrozen = true;

            foreach (var serverCharacter in PlayerServerCharacter.GetPlayerServerCharacters())
            {
                if (serverCharacter == null) continue;
                serverCharacter.ActionPlayer.ClearActions(true);
                serverCharacter.Movement.CancelMove();
            }
        }

        protected override void OnDestroy()
        {
            if (m_LifeStateChangedEventMessageSubscriber != null)
            {
                m_LifeStateChangedEventMessageSubscriber.Unsubscribe(OnLifeStateChangedEventMessage);
            }

            if (m_NetcodeHooks)
            {
                m_NetcodeHooks.OnNetworkSpawnHook -= OnNetworkSpawn;
                m_NetcodeHooks.OnNetworkDespawnHook -= OnNetworkDespawn;
            }

            base.OnDestroy();
        }

        void SpawnPlayer(ulong clientId, bool lateJoin)
        {
            Transform spawnPoint = null;

            if (m_PlayerSpawnPointsList == null || m_PlayerSpawnPointsList.Count == 0)
            {
                m_PlayerSpawnPointsList = new List<Transform>(m_PlayerSpawnPoints);
            }

            Debug.Assert(m_PlayerSpawnPointsList.Count > 0,
                $"PlayerSpawnPoints array should have at least 1 spawn points.");

            int index = Random.Range(0, m_PlayerSpawnPointsList.Count);
            spawnPoint = m_PlayerSpawnPointsList[index];
            m_PlayerSpawnPointsList.RemoveAt(index);

            var conn = NetworkServer.connections[(int)(uint)clientId];
            var playerNetworkObject = conn?.identity?.gameObject;

            var newPlayer = Instantiate(m_PlayerPrefab, Vector3.zero, Quaternion.identity);

            var newPlayerCharacter = newPlayer.GetComponent<ServerCharacter>();

            var physicsTransform = newPlayerCharacter.physicsWrapper.Transform;

            if (spawnPoint != null)
            {
                physicsTransform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
            }

            PersistentPlayer persistentPlayer = null;
            var persistentPlayerExists = playerNetworkObject != null && playerNetworkObject.TryGetComponent(out persistentPlayer);
            Assert.IsTrue(persistentPlayerExists,
                $"Matching persistent PersistentPlayer for client {clientId} not found!");

            // pass character type from persistent player to avatar
            var networkAvatarGuidStateExists =
                newPlayer.TryGetComponent(out NetworkAvatarGuidState networkAvatarGuidState);

            Assert.IsTrue(networkAvatarGuidStateExists,
                $"NetworkCharacterGuidState not found on player avatar!");

            // if reconnecting, set the player's position and rotation to its previous state
            if (lateJoin)
            {
                SessionPlayerData? sessionPlayerData = SessionManager<SessionPlayerData>.Instance.GetPlayerData(clientId);
                if (sessionPlayerData is { HasCharacterSpawned: true })
                {
                    physicsTransform.SetPositionAndRotation(sessionPlayerData.Value.PlayerPosition, sessionPlayerData.Value.PlayerRotation);
                }
            }

            // instantiate new SyncVars with a default value to ensure they're ready for use on OnStartServer/OnStartClient
            networkAvatarGuidState.AvatarGuid = persistentPlayer.NetworkAvatarGuidState.AvatarGuid;

            // pass name from persistent player to avatar
            var playerName = persistentPlayer.NetworkNameState.Name;
            if (newPlayer.TryGetComponent(out NetworkNameState networkNameState))
            {
                networkNameState.Name = playerName;
            }

            // register player in the live scoreboard
            SessionPlayerData? scoreData = SessionManager<SessionPlayerData>.Instance.GetPlayerData(clientId);
            string scorePlayerId = SessionManager<SessionPlayerData>.Instance.GetPlayerId(clientId);
            networkGameState.RegisterPlayer(clientId, scorePlayerId, playerName,
                scoreData?.PlayerNumber ?? 0, Bots.ServerBotManager.IsBot(clientId));

            // spawn player character
            NetworkServer.Spawn(newPlayer, conn);

            // If this "player" is a bot, give its avatar a brain. A no-op for humans — their
            // avatar is driven by their own ClientInputSender. Done after the spawn so the brain
            // starts on an object that already has a netId to attack with.
            Bots.ServerBotManager.TryAttachBrain(clientId, newPlayerCharacter);
        }

        void OnLifeStateChangedEventMessage(LifeStateChangedEventMessage message)
        {
            switch (message.CharacterType)
            {
                case CharacterTypeEnum.Tank:
                case CharacterTypeEnum.Archer:
                case CharacterTypeEnum.Mage:
                case CharacterTypeEnum.Rogue:
                    if (message.NewLifeState == LifeState.Fainted)
                        StartCoroutine(CoroRespawnPlayer(message.VictimClientId));
                    break;
                case CharacterTypeEnum.ImpBoss:
                case CharacterTypeEnum.Imp:
                case CharacterTypeEnum.VandalImp:
                    // NPC deaths don't affect match flow in deathmatch
                    break;
            }
        }

        IEnumerator CoroRespawnPlayer(ulong clientId)
        {
            yield return new WaitForSeconds(k_RespawnDelay);

            var sc = PlayerServerCharacter.GetPlayerServerCharacter(clientId);
            if (sc == null || sc.LifeState != LifeState.Fainted) yield break;

            // Don't respawn into the endgame — the match is over, the table is up.
            if (m_MatchEnded) yield break;

            var spawnPoint = PickSafestSpawnPoint(sc);
            if (spawnPoint != null)
            {
                sc.physicsWrapper.Transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
            }
            sc.Revive(null, sc.MaxHitPoints);
            sc.SetInvulnerable(k_RespawnInvulnerability);
        }

        /// <summary>
        /// Picks the spawn point that is furthest from the nearest living opponent — i.e. maximises
        /// the minimum distance to anyone who could shoot you the moment you appear. A plain loop
        /// over the spawn points is plenty: there are a handful of each.
        /// </summary>
        Transform PickSafestSpawnPoint(ServerCharacter respawning)
        {
            Transform best = null;
            float bestDistance = -1f;

            foreach (var candidate in m_PlayerSpawnPoints)
            {
                if (candidate == null) continue;

                float nearestOpponent = float.MaxValue;
                foreach (var other in PlayerServerCharacter.GetPlayerServerCharacters())
                {
                    if (other == null || other == respawning) continue;
                    if (other.LifeState != LifeState.Alive) continue;
                    if (other.physicsWrapper == null) continue;

                    float distance = Vector3.Distance(candidate.position, other.physicsWrapper.Transform.position);
                    if (distance < nearestOpponent)
                    {
                        nearestOpponent = distance;
                    }
                }

                if (nearestOpponent > bestDistance)
                {
                    bestDistance = nearestOpponent;
                    best = candidate;
                }
            }

            // Nobody alive to hide from (or no usable points): any point will do.
            return best != null
                ? best
                : (m_PlayerSpawnPoints.Length > 0 ? m_PlayerSpawnPoints[Random.Range(0, m_PlayerSpawnPoints.Length)] : null);
        }

        IEnumerator CoroGameOver(float wait)
        {
            m_PersistentGameState.SetWinState(WinState.Win);

            // build sorted scoreboard and persist it for the PostGame scene
            var sorted = new List<ScoreEntry>(networkGameState.Scores.Count);
            for (int i = 0; i < networkGameState.Scores.Count; i++)
                sorted.Add(networkGameState.Scores[i]);
            sorted.Sort(ScoreEntry.CompareForRanking);
            m_PersistentGameState.SetFinalScoreboard(sorted);

            yield return new WaitForSeconds(wait);

            FindAnyObjectByType<DedicatedServerBootstrapper>()?.OnMatchEnded();
            SceneLoaderWrapper.Instance.LoadScene("PostGame", useNetworkSceneManager: true);
        }
    }
}
