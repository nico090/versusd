using System;
using System.Collections.Generic;
using Unity.BossRoom.MasterServer;
using Unity.BossRoom.Utils;
using Mirror;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.ConnectionManagement
{
    public enum ConnectStatus
    {
        Undefined,
        Success,                  //client successfully connected. This may also be a successful reconnect.
        ServerFull,               //can't join, server is already at capacity.
        LoggedInAgain,            //logged in on a separate client, causing this one to be kicked out.
        UserRequestedDisconnect,  //Intentional Disconnect triggered by the user.
        GenericDisconnect,        //server disconnected, but no specific reason given.
        Reconnecting,             //client lost connection and is attempting to reconnect.
        IncompatibleBuildType,    //client build type is incompatible with server.
        HostEndedSession,         //host intentionally ended the session.
        StartHostFailed,          // server failed to bind
        StartClientFailed         // failed to connect to server and/or invalid network endpoint
    }

    public struct ReconnectMessage
    {
        public int CurrentAttempt;
        public int MaxAttempt;

        public ReconnectMessage(int currentAttempt, int maxAttempt)
        {
            CurrentAttempt = currentAttempt;
            MaxAttempt = maxAttempt;
        }
    }

    public struct ConnectionEventMessage : Mirror.NetworkMessage
    {
        public ConnectStatus ConnectStatus;
        public string PlayerName;
    }

    [Serializable]
    public class ConnectionPayload
    {
        public string playerId;
        public string playerName;
        public bool isDebug;
        public string joinToken; // validated by game server via Master Server; empty for IP-only sessions
    }

    /// <summary>
    /// This state machine handles connection through Mirror's NetworkManager. It is responsible for listening to
    /// Mirror callbacks and other outside calls and redirecting them to the current ConnectionState object.
    /// </summary>
    public class ConnectionManager : MonoBehaviour
    {
        /// <summary>
        /// Fires on the server after a client's connection payload has been validated and
        /// session data set up. Gameplay systems (e.g. ServerCharSelectState) subscribe to
        /// this to seat late-joining remote clients.
        /// </summary>
        public static event Action<ulong> ClientApprovedForSession;

        public static void InvokeClientApproved(ulong clientId)
        {
            ClientApprovedForSession?.Invoke(clientId);
        }

        ConnectionState m_CurrentState;

        [SerializeField]
        int m_NbReconnectAttempts = 2;

        public int NbReconnectAttempts => m_NbReconnectAttempts;

        [Inject]
        IObjectResolver m_Resolver;

        public int MaxConnectedPlayers = 8;

        /// <summary>
        /// The master-server facade, when one is registered (i.e. a MasterServerConfig is
        /// assigned). Null for IP-direct/LAN-only setups. Used by the reconnection path to
        /// fetch a fresh single-use join token before retrying a dedicated server.
        /// </summary>
        public MasterServerFacade MasterServerFacade { get; private set; }

        internal readonly OfflineState m_Offline = new OfflineState();
        internal readonly ClientConnectingState m_ClientConnecting = new ClientConnectingState();
        internal readonly ClientConnectedState m_ClientConnected = new ClientConnectedState();
        internal readonly ClientReconnectingState m_ClientReconnecting = new ClientReconnectingState();
        internal readonly StartingHostState m_StartingHost = new StartingHostState();
        internal readonly HostingState m_Hosting = new HostingState();

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            if (m_Resolver == null)
            {
                Debug.LogError("[ConnectionManager] m_Resolver is null — VContainer did not inject IObjectResolver. Check that ConnectionManager is registered via RegisterComponent in ApplicationController.");
                return;
            }

            List<ConnectionState> states = new() { m_Offline, m_ClientConnecting, m_ClientConnected, m_ClientReconnecting, m_StartingHost, m_Hosting };
            foreach (var connectionState in states)
            {
                m_Resolver.Inject(connectionState);
            }

            m_CurrentState = m_Offline;

            // The MasterServerFacade is only registered when a MasterServerConfig is assigned
            // (online/dedicated setups). Resolve it optionally so IP-direct/LAN-only play, which
            // has no facade, still works. Resolve throws when unregistered — treat that as "none".
            try { MasterServerFacade = m_Resolver.Resolve<MasterServerFacade>(); }
            catch { MasterServerFacade = null; }

            // NOTE: Do NOT subscribe to NetworkServer/NetworkClient events here.
            // Mirror's RegisterServerMessages() and RegisterClientMessages() use `=` (not `+=`),
            // which overwrites whatever we subscribe here. Subscribe in OnMirrorServerStarted /
            // OnMirrorClientStarted instead, which run after those registrations.
        }

        void OnDestroy()
        {
            NetworkServer.OnConnectedEvent -= OnServerClientConnected;
            NetworkServer.OnDisconnectedEvent -= OnServerClientDisconnected;

            NetworkClient.OnConnectedEvent -= OnClientConnected;
            NetworkClient.OnDisconnectedEvent -= OnClientDisconnected;
        }

        // Called on the server when a new client connects
        void OnServerClientConnected(NetworkConnectionToClient conn)
        {
            ulong clientId = (ulong)(uint)conn.connectionId;
            m_CurrentState.OnClientConnected(clientId);
        }

        // Called on the server when a client disconnects
        void OnServerClientDisconnected(NetworkConnectionToClient conn)
        {
            ulong clientId = (ulong)(uint)conn.connectionId;
            m_CurrentState.OnClientDisconnect(clientId);
        }

        // Called on the client when successfully connected to the server
        void OnClientConnected()
        {
            // NetworkClient.connection is a NetworkConnectionToServer, which does not expose the
            // server-assigned connectionId. The client-side states ignore the id anyway, so we pass
            // the local connection id (0).
            ulong clientId = (ulong)NetworkConnection.LocalConnectionId;
            // Only fire for pure clients (not the host, which uses server callbacks)
            if (!NetworkServer.active)
            {
                m_CurrentState.OnClientConnected(clientId);
            }
        }

        // Called on the client when disconnected from the server
        void OnClientDisconnected()
        {
            if (!NetworkServer.active)
            {
                m_CurrentState.OnClientDisconnect(0);
            }
        }

        /// <summary>
        /// Call this from a Mirror NetworkManager bridge component when the server fully starts.
        /// (Mirror's NetworkManager.OnStartServer override should call this.)
        /// </summary>
        public void OnMirrorServerStarted()
        {
            // RegisterServerMessages() already ran (it uses `=`), so += here is safe.
            NetworkServer.OnConnectedEvent += OnServerClientConnected;
            NetworkServer.OnDisconnectedEvent += OnServerClientDisconnected;
            m_CurrentState.OnServerStarted();
        }

        public void OnMirrorServerStopped()
        {
            NetworkServer.OnConnectedEvent -= OnServerClientConnected;
            NetworkServer.OnDisconnectedEvent -= OnServerClientDisconnected;
            m_CurrentState.OnServerStopped();
        }

        public void OnMirrorClientStarted()
        {
            // RegisterClientMessages() already ran (it uses `=`), so += here is safe.
            NetworkClient.OnConnectedEvent += OnClientConnected;
            NetworkClient.OnDisconnectedEvent += OnClientDisconnected;
        }

        public void OnMirrorClientStopped()
        {
            NetworkClient.OnConnectedEvent -= OnClientConnected;
            NetworkClient.OnDisconnectedEvent -= OnClientDisconnected;
        }

        /// <summary>
        /// Processes the connection approval payload for a connecting client.
        /// Called from BossRoomMirrorNetworkManager once the payload JSON is available.
        /// Returns true if the connection was approved.
        /// </summary>
        public bool ProcessConnectionApproval(int connectionId, string payloadJson)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(payloadJson);
            bool approved = m_Hosting.HandleApproval(connectionId, bytes, out var status);
            Debug.Log($"[CharSelect] ProcessConnectionApproval connId={connectionId} approved={approved} status={status}");
            return approved;
        }

        /// <summary>
        /// Call this from a Mirror NetworkManager bridge component on transport failure.
        /// </summary>
        public void OnMirrorTransportFailure()
        {
            m_CurrentState.OnTransportFailure();
        }

        internal void ChangeState(ConnectionState nextState)
        {
            Debug.Log($"{name}: Changed connection state from {m_CurrentState.GetType().Name} to {nextState.GetType().Name}.");

            if (m_CurrentState != null)
            {
                m_CurrentState.Exit();
            }
            m_CurrentState = nextState;
            m_CurrentState.Enter();
        }

        public void StartClientIp(string playerName, string ipaddress, int port, string joinToken = null, string sessionId = null)
        {
            m_CurrentState.StartClientIP(playerName, ipaddress, port, joinToken, sessionId);
        }

        public void StartHostIp(string playerName, string ipaddress, int port)
        {
            m_CurrentState.StartHostIP(playerName, ipaddress, port);
        }

        public void RequestShutdown()
        {
            m_CurrentState.OnUserRequestedShutdown();
        }
    }
}
