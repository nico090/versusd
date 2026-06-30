using System;
using System.Collections;
using Unity.BossRoom.Infrastructure;
using Mirror;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// Connection state corresponding to a client attempting to reconnect to a server. It will try to reconnect a
    /// number of times defined by the ConnectionManager's NbReconnectAttempts property. If it succeeds, it will
    /// transition to the ClientConnected state. If not, it will transition to the Offline state.
    /// </summary>
    class ClientReconnectingState : ClientConnectingState
    {
        [Inject]
        IPublisher<ReconnectMessage> m_ReconnectMessagePublisher;

        Coroutine m_ReconnectCoroutine;
        int m_NbAttempts;

        const float k_TimeBeforeFirstAttempt = 1;
        const float k_TimeBetweenAttempts = 5;

        public override void Enter()
        {
            m_NbAttempts = 0;
            m_ReconnectCoroutine = m_ConnectionManager.StartCoroutine(ReconnectCoroutine());
        }

        public override void Exit()
        {
            if (m_ReconnectCoroutine != null)
            {
                m_ConnectionManager.StopCoroutine(m_ReconnectCoroutine);
                m_ReconnectCoroutine = null;
            }
            m_ReconnectMessagePublisher.Publish(new ReconnectMessage(m_ConnectionManager.NbReconnectAttempts, m_ConnectionManager.NbReconnectAttempts));
        }

        public override void OnClientConnected(ulong _)
        {
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_ClientConnected);
        }

        public override void OnClientDisconnect(ulong _)
        {
            if (m_NbAttempts < m_ConnectionManager.NbReconnectAttempts)
            {
                m_ReconnectCoroutine = m_ConnectionManager.StartCoroutine(ReconnectCoroutine());
            }
            else
            {
                m_ConnectStatusPublisher.Publish(ConnectStatus.GenericDisconnect);
                m_ConnectionManager.ChangeState(m_ConnectionManager.m_Offline);
            }
        }

        IEnumerator ReconnectCoroutine()
        {
            // If not on first attempt, wait some time before trying again
            if (m_NbAttempts > 0)
            {
                yield return new WaitForSeconds(k_TimeBetweenAttempts);
            }

            Debug.Log("Lost connection to host, trying to reconnect...");

            // Stop any active Mirror client/host
            if (NetworkServer.active)
            {
                Mirror.NetworkManager.singleton.StopHost();
            }
            else if (NetworkClient.isConnected || NetworkClient.isConnecting)
            {
                Mirror.NetworkManager.singleton.StopClient();
            }

            // Wait a frame for the stop to propagate
            yield return null;

            Debug.Log($"Reconnecting attempt {m_NbAttempts + 1}/{m_ConnectionManager.NbReconnectAttempts}...");
            m_ReconnectMessagePublisher.Publish(new ReconnectMessage(m_NbAttempts, m_ConnectionManager.NbReconnectAttempts));

            if (m_NbAttempts == 0)
            {
                yield return new WaitForSeconds(k_TimeBeforeFirstAttempt);
            }

            m_NbAttempts++;

            var reconnectingSetupTask = m_ConnectionMethod.SetupClientReconnectionAsync();
            yield return new WaitUntil(() => reconnectingSetupTask.IsCompleted);

            if (!reconnectingSetupTask.IsFaulted && reconnectingSetupTask.Result.success)
            {
                // Attempt to reconnect; OnClientConnected or OnClientDisconnect will fire
                ConnectClientAsync();
            }
            else
            {
                if (!reconnectingSetupTask.Result.shouldTryAgain)
                {
                    m_NbAttempts = m_ConnectionManager.NbReconnectAttempts;
                }
                OnClientDisconnect(0);
            }
        }
    }
}
