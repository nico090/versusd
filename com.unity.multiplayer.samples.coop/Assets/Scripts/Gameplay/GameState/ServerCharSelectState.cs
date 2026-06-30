using System;
using System.Collections;
using Mirror;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Infrastructure;
using Unity.Multiplayer.Samples.BossRoom;
using Unity.Multiplayer.Samples.Utilities;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.Gameplay.GameState
{
    /// <summary>
    /// Server specialization of Character Select game state.
    /// </summary>
    [RequireComponent(typeof(NetcodeHooks), typeof(NetworkCharSelection))]
    public class ServerCharSelectState : GameStateBehaviour
    {
        public static ServerCharSelectState Instance { get; private set; }
        [SerializeField]
        NetcodeHooks m_NetcodeHooks;

        public override GameState ActiveState => GameState.CharSelect;
        public NetworkCharSelection networkCharSelection { get; private set; }

        Coroutine m_WaitToEndSessionCoroutine;

        bool m_ServerInitialized;

        [Inject]
        ConnectionManager m_ConnectionManager;

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
            networkCharSelection = GetComponent<NetworkCharSelection>();

            m_NetcodeHooks.OnNetworkSpawnHook += OnNetworkSpawn;
            m_NetcodeHooks.OnNetworkDespawnHook += OnNetworkDespawn;
        }

        protected override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            base.OnDestroy();

            if (m_NetcodeHooks)
            {
                m_NetcodeHooks.OnNetworkSpawnHook -= OnNetworkSpawn;
                m_NetcodeHooks.OnNetworkDespawnHook -= OnNetworkDespawn;
            }
        }

        void OnClientChangedSeat(ulong clientId, int newSeatIdx, bool lockedIn)
        {
            Debug.Log($"[CharSelect] OnClientChangedSeat clientId={clientId} seatIdx={newSeatIdx} sessionPlayers.Count={networkCharSelection.sessionPlayers.Count}");
            int idx = FindSessionPlayerIdx(clientId);
            if (idx == -1)
            {
                throw new Exception($"OnClientChangedSeat: client ID {clientId} is not a Session player and cannot change seats! Shouldn't be here!");
            }

            if (networkCharSelection.IsSessionClosed)
            {
                // The user tried to change their class after everything was locked in... too late! Discard this choice
                return;
            }

            if (newSeatIdx == -1)
            {
                // we can't lock in with no seat
                lockedIn = false;
            }
            else
            {
                // see if someone has already locked-in that seat! If so, too late... discard this choice
                foreach (NetworkCharSelection.SessionPlayerState playerInfo in networkCharSelection.sessionPlayers)
                {
                    if (playerInfo.ClientId != clientId && playerInfo.SeatIdx == newSeatIdx && playerInfo.SeatState == NetworkCharSelection.SeatState.LockedIn)
                    {
                        // somebody already locked this choice in. Stop!
                        // Instead of granting lock request, change this player to Inactive state.
                        networkCharSelection.sessionPlayers[idx] = new NetworkCharSelection.SessionPlayerState(clientId,
                            networkCharSelection.sessionPlayers[idx].PlayerId,
                            networkCharSelection.sessionPlayers[idx].PlayerName,
                            networkCharSelection.sessionPlayers[idx].PlayerNumber,
                            NetworkCharSelection.SeatState.Inactive);

                        // then early out
                        return;
                    }
                }
            }

            networkCharSelection.sessionPlayers[idx] = new NetworkCharSelection.SessionPlayerState(clientId,
                networkCharSelection.sessionPlayers[idx].PlayerId,
                networkCharSelection.sessionPlayers[idx].PlayerName,
                networkCharSelection.sessionPlayers[idx].PlayerNumber,
                lockedIn ? NetworkCharSelection.SeatState.LockedIn : NetworkCharSelection.SeatState.Active,
                newSeatIdx,
                Time.time);

            if (lockedIn)
            {
                // to help the clients visually keep track of who's in what seat, we'll "kick out" any other players
                // who were also in that seat. (Those players didn't click "Ready!" fast enough, somebody else took their seat!)
                for (int i = 0; i < networkCharSelection.sessionPlayers.Count; ++i)
                {
                    if (networkCharSelection.sessionPlayers[i].SeatIdx == newSeatIdx && i != idx)
                    {
                        // change this player to Inactive state.
                        networkCharSelection.sessionPlayers[i] = new NetworkCharSelection.SessionPlayerState(
                            networkCharSelection.sessionPlayers[i].ClientId,
                            networkCharSelection.sessionPlayers[i].PlayerId,
                            networkCharSelection.sessionPlayers[i].PlayerName,
                            networkCharSelection.sessionPlayers[i].PlayerNumber,
                            NetworkCharSelection.SeatState.Inactive);
                    }
                }
            }

            CloseSessionIfReady();
        }

        /// <summary>
        /// Returns the index of a client in the master SessionPlayer list, or -1 if not found
        /// </summary>
        int FindSessionPlayerIdx(ulong clientId)
        {
            for (int i = 0; i < networkCharSelection.sessionPlayers.Count; ++i)
            {
                if (networkCharSelection.sessionPlayers[i].ClientId == clientId)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Looks through all our connections and sees if everyone has locked in their choice;
        /// if so, we lock in the whole Session, save state, and begin the transition to gameplay
        /// </summary>
        void CloseSessionIfReady()
        {
            foreach (NetworkCharSelection.SessionPlayerState playerInfo in networkCharSelection.sessionPlayers)
            {
                if (playerInfo.SeatState != NetworkCharSelection.SeatState.LockedIn)
                    return; // nope, at least one player isn't locked in yet!
            }

            // everybody's ready at the same time! Lock it down!
            networkCharSelection.IsSessionClosed = true;

            // remember our choices so the next scene can use the info
            SaveSessionResults();

            // Delay a few seconds to give the UI time to react, then switch scenes
            m_WaitToEndSessionCoroutine = StartCoroutine(WaitToEndSession());
        }

        /// <summary>
        /// Cancels the process of closing the Session, so that if a new player joins, they are able to choose a character.
        /// </summary>
        void CancelCloseSession()
        {
            if (m_WaitToEndSessionCoroutine != null)
            {
                StopCoroutine(m_WaitToEndSessionCoroutine);
            }
            networkCharSelection.IsSessionClosed = false;
        }

        void SaveSessionResults()
        {
            foreach (NetworkCharSelection.SessionPlayerState playerInfo in networkCharSelection.sessionPlayers)
            {
                var playerNetworkObject = NetworkServer.connections[(int)(uint)playerInfo.ClientId]?.identity?.gameObject;

                if (playerNetworkObject && playerNetworkObject.TryGetComponent(out PersistentPlayer persistentPlayer))
                {
                    // pass avatar GUID to PersistentPlayer
                    persistentPlayer.NetworkAvatarGuidState.AvatarGuid =
                        networkCharSelection.AvatarConfiguration[playerInfo.SeatIdx].Guid.ToString();
                }
            }
        }

        IEnumerator WaitToEndSession()
        {
            yield return new WaitForSeconds(3);
            SceneLoaderWrapper.Instance.LoadScene("BossRoom", useNetworkSceneManager: true);
        }

        void OnNetworkDespawn()
        {
            if (networkCharSelection)
                networkCharSelection.OnClientChangedSeat -= OnClientChangedSeat;

            NetworkServer.OnConnectedEvent -= OnServerClientConnected;
            NetworkServer.OnDisconnectedEvent -= OnServerClientDisconnected;
            ConnectionManager.ClientApprovedForSession -= SeatNewPlayer;
            m_ServerInitialized = false;
        }

        void OnNetworkSpawn()
        {
            // NetcodeHooks fires OnNetworkSpawnHook from both OnStartServer and OnStartClient.
            // Only run server init once.
            if (!m_NetcodeHooks.isServer || m_ServerInitialized)
            {
                enabled = !m_NetcodeHooks.isServer;
                return;
            }
            m_ServerInitialized = true;

            networkCharSelection.OnClientChangedSeat += OnClientChangedSeat;
            NetworkServer.OnConnectedEvent += OnServerClientConnected;
            NetworkServer.OnDisconnectedEvent += OnServerClientDisconnected;

            // Late-joining remote clients: seat them when their payload is approved.
            ConnectionManager.ClientApprovedForSession += SeatNewPlayer;

            // Seat players already connected and approved before this scene loaded.
            // CharSelect loads async, so the host's session data is ready by now.
            foreach (var conn in NetworkServer.connections.Values)
                SeatNewPlayer((ulong)(uint)conn.connectionId);
        }

        void OnServerClientConnected(NetworkConnectionToClient conn)
        {
            SeatNewPlayer((ulong)(uint)conn.connectionId);
        }

        void OnServerClientDisconnected(NetworkConnectionToClient conn)
        {
            ulong clientId = (ulong)(uint)conn.connectionId;
            for (int i = 0; i < networkCharSelection.sessionPlayers.Count; i++)
            {
                if (networkCharSelection.sessionPlayers[i].ClientId == clientId)
                {
                    networkCharSelection.sessionPlayers.RemoveAt(i);
                    CancelCloseSession();
                    break;
                }
            }
        }

        void SeatNewPlayer(ulong clientId)
        {
            // Guard: a host's connection fires both OnConnectedEvent and ClientApprovedForSession.
            // Both paths call SeatNewPlayer; the second must be a no-op.
            if (FindSessionPlayerIdx(clientId) != -1)
                return;

            if (networkCharSelection.IsSessionClosed)
            {
                CancelCloseSession();
            }

            Debug.Log($"[CharSelect] SeatNewPlayer({clientId})");
            SessionPlayerData? sessionPlayerData = SessionManager<SessionPlayerData>.Instance.GetPlayerData(clientId);
            Debug.Log($"[CharSelect] SeatNewPlayer({clientId}) — hasData={sessionPlayerData.HasValue}");
            if (sessionPlayerData.HasValue)
            {
                var playerData = sessionPlayerData.Value;
                if (playerData.PlayerNumber == -1 || !IsPlayerNumberAvailable(playerData.PlayerNumber))
                {
                    // If no player num already assigned or if player num is no longer available, get an available one.
                    playerData.PlayerNumber = GetAvailablePlayerNumber();
                }
                if (playerData.PlayerNumber == -1)
                {
                    // Sanity check. We ran out of seats... there was no room!
                    throw new Exception($"we shouldn't be here, connection approval should have refused this connection already for client ID {clientId} and player num {playerData.PlayerNumber}");
                }

                var playerId = SessionManager<SessionPlayerData>.Instance.GetPlayerId(clientId);
                networkCharSelection.sessionPlayers.Add(new NetworkCharSelection.SessionPlayerState(clientId, playerId, playerData.PlayerName, playerData.PlayerNumber, NetworkCharSelection.SeatState.Inactive));
                SessionManager<SessionPlayerData>.Instance.SetPlayerData(clientId, playerData);
            }
        }

        int GetAvailablePlayerNumber()
        {
            for (int possiblePlayerNumber = 0; possiblePlayerNumber < m_ConnectionManager.MaxConnectedPlayers; ++possiblePlayerNumber)
            {
                if (IsPlayerNumberAvailable(possiblePlayerNumber))
                {
                    return possiblePlayerNumber;
                }
            }
            // we couldn't get a Player# for this person... which means the Session is full!
            return -1;
        }

        bool IsPlayerNumberAvailable(int playerNumber)
        {
            bool found = false;
            foreach (NetworkCharSelection.SessionPlayerState playerState in networkCharSelection.sessionPlayers)
            {
                if (playerState.PlayerNumber == playerNumber)
                {
                    found = true;
                    break;
                }
            }

            return !found;
        }
    }
}
