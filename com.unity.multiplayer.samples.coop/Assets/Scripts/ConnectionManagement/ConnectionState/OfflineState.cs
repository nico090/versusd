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
    }
}
