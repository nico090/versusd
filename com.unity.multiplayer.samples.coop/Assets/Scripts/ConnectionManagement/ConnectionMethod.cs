using System.Threading.Tasks;
using Unity.BossRoom.MasterServer;
using Unity.BossRoom.Utils;
using Mirror;
using kcp2k;
using UnityEngine;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// ConnectionMethod contains all setup needed to prepare Mirror to be ready to start a connection,
    /// either host or client side. Override this abstract class to add a new transport or way of connecting.
    /// </summary>
    public abstract class ConnectionMethodBase
    {
        protected ConnectionManager m_ConnectionManager;
        readonly ProfileManager m_ProfileManager;
        protected readonly string m_PlayerName;

        /// <summary>
        /// Setup the host connection prior to starting the NetworkManager
        /// </summary>
        public abstract void SetupHostConnection();

        /// <summary>
        /// Setup the client connection prior to starting the NetworkManager
        /// </summary>
        public abstract void SetupClientConnection();

        /// <summary>
        /// Setup the client for reconnection prior to reconnecting
        /// </summary>
        /// <returns>
        /// success = true if succeeded in setting up reconnection, false if failed.
        /// shouldTryAgain = true if we should try again after failing, false if not.
        /// </returns>
        public abstract Task<(bool success, bool shouldTryAgain)> SetupClientReconnectionAsync();

        public ConnectionMethodBase(ConnectionManager connectionManager, ProfileManager profileManager, string playerName)
        {
            m_ConnectionManager = connectionManager;
            m_ProfileManager = profileManager;
            m_PlayerName = playerName;
        }

        protected void SetConnectionPayload(string playerId, string playerName, string joinToken = "", string sessionId = "")
        {
            // Hand credentials to Mirror's NetworkAuthenticator (read in
            // MirrorNetworkAuthenticator.OnClientAuthenticate). The joinToken and
            // sessionId are empty for host/LAN-direct connections and set for
            // master-server joins (the server cross-checks them against the lobby).
            ClientAuthPayload.Set(playerId, playerName, Debug.isDebugBuild, joinToken, sessionId);
        }

        /// <summary>
        /// Returns a stable player ID derived from local preferences.
        /// </summary>
        protected string GetPlayerId()
        {
            return ClientPrefs.GetGuid() + m_ProfileManager.Profile;
        }
    }

    /// <summary>
    /// Simple IP connection setup with Mirror's KcpTransport
    /// </summary>
    class ConnectionMethodIP : ConnectionMethodBase
    {
        string m_Ipaddress;
        ushort m_Port;
        // Not readonly: refreshed on reconnect (the join token is single-use, and the
        // dedicated endpoint can change if the server respawned).
        string m_JoinToken;
        readonly string m_SessionId;
        // Null for IP-direct/LAN sessions; set when joining via the master server.
        readonly MasterServerFacade m_MasterServerFacade;

        public ConnectionMethodIP(string ip, ushort port, ConnectionManager connectionManager, ProfileManager profileManager, string playerName, string joinToken = "", string sessionId = "", MasterServerFacade masterServerFacade = null)
            : base(connectionManager, profileManager, playerName)
        {
            m_Ipaddress = ip;
            m_Port = port;
            m_ConnectionManager = connectionManager;
            m_JoinToken = joinToken ?? string.Empty;
            m_SessionId = sessionId ?? string.Empty;
            m_MasterServerFacade = masterServerFacade;
        }

        public override void SetupClientConnection()
        {
            SetConnectionPayload(GetPlayerId(), m_PlayerName, m_JoinToken, m_SessionId);

            // Configure the KcpTransport (or whichever transport is active) with IP + port
            var transport = Mirror.NetworkManager.singleton.GetComponent<KcpTransport>();
            if (transport != null)
            {
                transport.Port = m_Port;
            }

            Mirror.NetworkManager.singleton.networkAddress = m_Ipaddress;
        }

        public override async Task<(bool success, bool shouldTryAgain)> SetupClientReconnectionAsync()
        {
            // P2P / LAN-direct: no master-server session, so there's no token to refresh —
            // just retry the same IP endpoint.
            if (string.IsNullOrEmpty(m_SessionId) || m_MasterServerFacade == null)
            {
                return (true, true);
            }

            // Master-server (dedicated) session: the join token is single-use and was burned
            // when we were first seated, and a dedicated server requires a valid token. Ask the
            // master server to re-join the lobby for a fresh token (and current endpoint) before
            // the next StartClient. SetupClientConnection then sends the refreshed token.
            // NOTE: private lobbies that require a password are not handled here (we don't keep
            // the password); those would need it threaded through to re-join.
            var join = await m_MasterServerFacade.JoinLobbyAsync(m_SessionId);
            if (join == null || string.IsNullOrEmpty(join.join_token))
            {
                // Lobby gone or re-join refused. Allow the remaining attempts (the master may be
                // briefly unreachable); the attempt cap in ClientReconnectingState bounds this.
                return (false, true);
            }

            m_JoinToken = join.join_token;
            if (!string.IsNullOrEmpty(join.host_ip)) m_Ipaddress = join.host_ip;
            if (join.host_port != 0) m_Port = (ushort)join.host_port;
            return (true, true);
        }

        public override void SetupHostConnection()
        {
            SetConnectionPayload(GetPlayerId(), m_PlayerName);

            var transport = Mirror.NetworkManager.singleton.GetComponent<KcpTransport>();
            if (transport != null)
            {
                transport.Port = m_Port;
            }

            Mirror.NetworkManager.singleton.networkAddress = m_Ipaddress;
        }
    }
}
