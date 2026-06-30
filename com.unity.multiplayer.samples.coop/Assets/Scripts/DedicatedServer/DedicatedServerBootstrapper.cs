using System;
using System.Threading.Tasks;
using Mirror;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.MasterServer;
using Unity.Multiplayer.Samples.Utilities;
using UnityEngine;

namespace Unity.BossRoom.DedicatedServer
{
    /// <summary>
    /// Attach to a persistent GameObject in the dedicated-server scene.
    /// Lifecycle: Register → Poll for allocation → Load game → Heartbeat → Unregister → Quit.
    ///
    /// Required env vars:
    ///   MASTER_SERVER_URL        e.g. http://your-vps:8000
    ///   SERVER_IP                public IP of this VPS
    ///   SERVER_PORT              UDP port this instance listens on (default 9999)
    ///   SERVER_SHARED_SECRET     shared secret for privileged master-server endpoints
    /// </summary>
    public class DedicatedServerBootstrapper : MonoBehaviour
    {
        [SerializeField] MasterServerConfig m_MasterServerConfig;
        [SerializeField] int m_DefaultPort = 9999;
        [SerializeField] float m_PollIntervalSeconds = 5f;

        enum ServerState { Registering, Polling, Allocated, InGame }

        MasterServerFacade m_Facade;
        string m_ServerId;
        string m_CurrentSessionId;
        int m_AllocMaxPlayers = 8;
        ServerState m_State = ServerState.Registering;
        bool m_Destroyed;

        /// <summary>The running DedicatedServerBootstrapper, or null in P2P / editor builds.</summary>
        public static DedicatedServerBootstrapper Current { get; private set; }

        public MasterServerFacade Facade => m_Facade;

        async void Start()
        {
            Current = this;
            DontDestroyOnLoad(gameObject);

            string masterUrl = Env("MASTER_SERVER_URL") ?? m_MasterServerConfig?.baseUrl ?? "http://localhost:8000";
            string serverIp  = Env("SERVER_IP") ?? "127.0.0.1";
            int port = int.TryParse(Env("SERVER_PORT"), out var p) ? p : m_DefaultPort;

            var config = ScriptableObject.CreateInstance<MasterServerConfig>();
            config.baseUrl = masterUrl;
            m_Facade = new MasterServerFacade(config);
            m_Facade.SetServerSecret(Env("SERVER_SHARED_SECRET") ?? "");

            Debug.Log($"[DS] Registering at {masterUrl} — {serverIp}:{port}");

            var reg = await m_Facade.RegisterDedicatedServerAsync(serverIp, port);
            if (reg == null)
            {
                Debug.LogError("[DS] Registration failed. Quitting.");
                Application.Quit(1);
                return;
            }

            m_ServerId = reg.server_id;
            Debug.Log($"[DS] Registered → server_id={m_ServerId}");

            if (NetworkManager.singleton != null)
            {
                var transport = Transport.active;
                if (transport != null)
                {
                    // Mirror transports (KcpTransport, Telepathy, …) expose Port as a
                    // *property* backed by a lowercase `port` field. GetField("Port")
                    // returns null (it only finds fields), so the assignment used to
                    // silently no-op and the server kept listening on the default 7777.
                    // Set the property; fall back to the lowercase field.
                    var t = transport.GetType();
                    var portProp = t.GetProperty("Port");
                    if (portProp != null && portProp.CanWrite)
                        portProp.SetValue(transport, (ushort)port);
                    else
                        t.GetField("port")?.SetValue(transport, (ushort)port);
                    Debug.Log($"[DS] Transport listening on port {port}");
                }
                NetworkManager.singleton.StartServer();
            }

            _ = ServerHeartbeatLoopAsync();
            _ = PollForAllocationLoopAsync();
        }

        // ── Polling ───────────────────────────────────────────────────────────

        async Task PollForAllocationLoopAsync()
        {
            m_State = ServerState.Polling;
            Debug.Log("[DS] Polling for allocation...");

            while (!m_Destroyed && m_State == ServerState.Polling)
            {
                await Task.Delay(TimeSpan.FromSeconds(m_PollIntervalSeconds));
                if (m_Destroyed) return;

                var alloc = await m_Facade.GetServerAllocationAsync(m_ServerId);
                if (alloc == null || !alloc.allocated) continue;

                m_State = ServerState.Allocated;
                m_CurrentSessionId = alloc.session_id;
                m_AllocMaxPlayers = alloc.max_players > 0 ? alloc.max_players : 8;
                Debug.Log($"[DS] Allocated! session={m_CurrentSessionId} lobby={alloc.lobby_name} maxPlayers={m_AllocMaxPlayers}");

                ApplyMaxPlayers();
                _ = LobbyHeartbeatLoopAsync(m_CurrentSessionId);
                OnAllocated();
            }
        }

        void ApplyMaxPlayers()
        {
            var connMgr = FindAnyObjectByType<ConnectionManager>();
            if (connMgr != null)
                connMgr.MaxConnectedPlayers = m_AllocMaxPlayers;
        }

        void OnAllocated()
        {
            m_State = ServerState.InGame;
            Debug.Log("[DS] Loading CharSelect...");
            if (SceneLoaderWrapper.Instance != null)
                SceneLoaderWrapper.Instance.LoadScene("CharSelect", useNetworkSceneManager: true);
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene("CharSelect");
        }

        // ── Called by game code when the match ends ───────────────────────────

        public async void OnMatchEnded()
        {
            Debug.Log("[DS] Match ended — shutting down container.");
            m_Destroyed = true;
            m_CurrentSessionId = null;

            await m_Facade.ServerUnregisterAsync(m_ServerId);
            await Task.Delay(2000);
            Application.Quit(0);
        }

        // ── Heartbeat loops ───────────────────────────────────────────────────

        async Task ServerHeartbeatLoopAsync()
        {
            while (!m_Destroyed)
            {
                await Task.Delay(TimeSpan.FromSeconds(15));
                if (!m_Destroyed)
                    await m_Facade.ServerHeartbeatAsync(m_ServerId);
            }
        }

        async Task LobbyHeartbeatLoopAsync(string sessionId)
        {
            while (!m_Destroyed && m_CurrentSessionId == sessionId)
            {
                await Task.Delay(TimeSpan.FromSeconds(15));
                if (!m_Destroyed && m_CurrentSessionId == sessionId)
                {
                    try { await m_Facade.LobbyHeartbeatAsync(sessionId); }
                    catch { /* non-fatal */ }
                }
            }
        }

        void OnDestroy()
        {
            m_Destroyed = true;
            if (Current == this) Current = null;
        }

        static string Env(string key) =>
            Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : null;
    }
}
