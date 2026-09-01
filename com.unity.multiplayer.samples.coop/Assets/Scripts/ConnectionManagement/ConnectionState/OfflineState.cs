using System;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.Utils;
using Unity.Multiplayer.Samples.Utilities;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// Connection state corresponding to when the NetworkManager is shut down. From this state we can transition to the
    /// ClientConnecting state, if starting as a client, or the StartingHost state, if starting as a host.
    /// </summary>
    class OfflineState : ConnectionState
    {
        [Inject]
        ProfileManager m_ProfileManager;

        const string k_MainMenuSceneName = "MainMenu";

        public override void Enter()
        {
            ReleaseMasterServerLobby();

            if (NetworkServer.active)
            {
                Mirror.NetworkManager.singleton.StopHost();
            }
            else if (NetworkClient.isConnected)
            {
                Mirror.NetworkManager.singleton.StopClient();
            }

            if (SceneManager.GetActiveScene().name != k_MainMenuSceneName)
            {
                SceneLoaderWrapper.Instance.LoadScene(k_MainMenuSceneName, useNetworkSceneManager: false);
            }
        }

        public override void Exit() { }

        /// <summary>
        /// Tells the master server we are done with the lobby, and stops its heartbeat.
        /// </summary>
        /// <remarks>
        /// <para><c>MasterServerFacade.LeaveLobbyAsync</c> existed and was called from nowhere at
        /// all, so a lobby was never released — it lingered until its TTL expired. Leaving a match
        /// and immediately hosting another therefore hit
        /// <c>HTTP 409: A room with that name already exists</c>, the host start failed, and the
        /// player never reached CharSelect. The heartbeat made it worse by renewing the very lobby
        /// that was in the way.</para>
        ///
        /// <para>Here rather than at the button that leaves, because this is where every route out
        /// converges: a deliberate quit, losing the host, a failed connection. Fire and forget —
        /// this is cleanup on the way out, and nothing left to wait for should be able to hold up
        /// the return to the main menu, so a master server that is slow or gone must not either.
        /// The 409 it prevents is on the server side; if the call is lost the TTL is still the
        /// backstop it always was.</para>
        /// </remarks>
        void ReleaseMasterServerLobby()
        {
            var facade = m_ConnectionManager != null ? m_ConnectionManager.MasterServerFacade : null;
            if (facade == null)
            {
                return;
            }

            facade.StopHeartbeat();
            _ = facade.LeaveLobbyAsync();
        }

        public override void StartClientIP(string playerName, string ipaddress, int port, string joinToken = null, string sessionId = null)
        {
            var connectionMethod = new ConnectionMethodIP(ipaddress, (ushort)port, m_ConnectionManager, m_ProfileManager, playerName, joinToken, sessionId, m_ConnectionManager.MasterServerFacade);
            m_ConnectionManager.m_ClientReconnecting.Configure(connectionMethod);
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_ClientConnecting.Configure(connectionMethod));
        }

        public override void StartHostIP(string playerName, string ipaddress, int port)
        {
            var connectionMethod = new ConnectionMethodIP(ipaddress, (ushort)port, m_ConnectionManager, m_ProfileManager, playerName);
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_StartingHost.Configure(connectionMethod));
        }

        public override void StartHostRelay(string playerName)
        {
            var connectionMethod = new ConnectionMethodRelay(string.Empty, m_ConnectionManager, m_ProfileManager, playerName);
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_StartingHost.Configure(connectionMethod));
        }

        public override void StartClientRelay(string playerName, string serverId, string joinToken = null, string sessionId = null)
        {
            var connectionMethod = new ConnectionMethodRelay(serverId, m_ConnectionManager, m_ProfileManager, playerName, joinToken, sessionId, m_ConnectionManager.MasterServerFacade);
            m_ConnectionManager.m_ClientReconnecting.Configure(connectionMethod);
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_ClientConnecting.Configure(connectionMethod));
        }
    }
}
