using Mirror;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// Connection state corresponding to a connected client. When being disconnected, transitions to the
    /// ClientReconnecting state if no reason is given, or to the Offline state.
    /// </summary>
    class ClientConnectedState : OnlineState
    {
        public override void Enter() { }

        public override void Exit() { }

        public override void OnClientDisconnect(ulong _)
        {
            // Mirror does not carry a structured disconnect reason by default.
            // Treat all disconnects as reconnectable unless explicitly shut down.
            m_ConnectStatusPublisher.Publish(ConnectStatus.Reconnecting);
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_ClientReconnecting);
        }
    }
}
