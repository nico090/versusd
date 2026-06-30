using System;
using Unity.BossRoom.Infrastructure;
using Unity.Multiplayer.Samples.BossRoom;
using Unity.Multiplayer.Samples.Utilities;
using Mirror;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// Connection state corresponding to a listening host. Handles incoming client connections. When shutting down or
    /// being timed out, transitions to the Offline state.
    /// </summary>
    class HostingState : OnlineState
    {
        [Inject]
        IPublisher<ConnectionEventMessage> m_ConnectionEventPublisher;

        // used in connection approval. This is intended as a bit of light protection against DOS attacks that rely on sending silly big buffers of garbage.
        const int k_MaxConnectPayload = 1024;

        public override void Enter()
        {
            //The "BossRoom" server always advances to CharSelect immediately on start. Different games
            //may do this differently.
            SceneLoaderWrapper.Instance.LoadScene("CharSelect", useNetworkSceneManager: true);
        }

        public override void Exit()
        {
            SessionManager<SessionPlayerData>.Instance.OnServerEnded();
        }

        public override void OnClientConnected(ulong clientId)
        {
            var playerData = SessionManager<SessionPlayerData>.Instance.GetPlayerData(clientId);
            if (playerData != null)
            {
                m_ConnectionEventPublisher.Publish(new ConnectionEventMessage() { ConnectStatus = ConnectStatus.Success, PlayerName = playerData.Value.PlayerName });
            }
        }

        public override void OnClientDisconnect(ulong clientId)
        {
            ulong localClientId = NetworkServer.localConnection != null ? (ulong)(uint)NetworkServer.localConnection.connectionId : 0;
            if (clientId != localClientId)
            {
                var playerId = SessionManager<SessionPlayerData>.Instance.GetPlayerId(clientId);
                if (playerId != null)
                {
                    var sessionData = SessionManager<SessionPlayerData>.Instance.GetPlayerData(playerId);
                    if (sessionData.HasValue)
                    {
                        m_ConnectionEventPublisher.Publish(new ConnectionEventMessage() { ConnectStatus = ConnectStatus.GenericDisconnect, PlayerName = sessionData.Value.PlayerName });
                    }
                    SessionManager<SessionPlayerData>.Instance.DisconnectClient(clientId);
                }
            }
        }

        public override void OnUserRequestedShutdown()
        {
            var reason = JsonUtility.ToJson(ConnectStatus.HostEndedSession);
            ulong localClientId = NetworkServer.localConnection != null ? (ulong)(uint)NetworkServer.localConnection.connectionId : 0;

            // Disconnect all clients except ourselves
            foreach (var kvp in NetworkServer.connections)
            {
                ulong id = (ulong)(uint)kvp.Key;
                if (id != localClientId)
                {
                    kvp.Value.Disconnect();
                }
            }
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_Offline);
        }

        public override void OnServerStopped()
        {
            m_ConnectStatusPublisher.Publish(ConnectStatus.GenericDisconnect);
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_Offline);
        }

        /// <summary>
        /// Called when a client connects to validate their connection payload and set up session data.
        /// In Mirror, this logic is invoked from a custom NetworkAuthenticator or OnServerConnect override.
        /// </summary>
        public bool HandleApproval(int connectionId, byte[] connectionData, out ConnectStatus status)
        {
            status = ConnectStatus.Success;

            if (connectionData.Length > k_MaxConnectPayload)
            {
                status = ConnectStatus.GenericDisconnect;
                return false;
            }

            var payload = System.Text.Encoding.UTF8.GetString(connectionData);
            var connectionPayload = JsonUtility.FromJson<ConnectionPayload>(payload);
            status = GetConnectStatus(connectionPayload);

            if (status == ConnectStatus.Success)
            {
                ulong clientId = (ulong)(uint)connectionId;
                SessionManager<SessionPlayerData>.Instance.SetupConnectingPlayerSessionData(clientId, connectionPayload.playerId,
                    new SessionPlayerData(clientId, connectionPayload.playerName, new NetworkGuid(), 0, true));
                return true;
            }

            return false;
        }

        ConnectStatus GetConnectStatus(ConnectionPayload connectionPayload)
        {
            if (NetworkServer.connections.Count >= m_ConnectionManager.MaxConnectedPlayers)
            {
                return ConnectStatus.ServerFull;
            }

            // Build-type compatibility only matters for player-hosted (P2P) sessions,
            // where a debug host and a release client can desync. A dedicated server
            // (headless batch mode) is always release and legitimately serves clients
            // of any flavor, so don't gate it on the server's own build type.
            if (!Application.isBatchMode && connectionPayload.isDebug != Debug.isDebugBuild)
            {
                return ConnectStatus.IncompatibleBuildType;
            }

            return SessionManager<SessionPlayerData>.Instance.IsDuplicateConnection(connectionPayload.playerId) ?
                ConnectStatus.LoggedInAgain : ConnectStatus.Success;
        }
    }
}
