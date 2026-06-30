using System;
using Unity.BossRoom.Infrastructure;
using Mirror;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// Base class representing a connection state.
    /// </summary>
    abstract class ConnectionState
    {
        [Inject]
        protected ConnectionManager m_ConnectionManager;

        [Inject]
        protected IPublisher<ConnectStatus> m_ConnectStatusPublisher;

        public abstract void Enter();

        public abstract void Exit();

        public virtual void OnClientConnected(ulong clientId) { }
        public virtual void OnClientDisconnect(ulong clientId) { }

        public virtual void OnServerStarted() { }

        public virtual void StartClientIP(string playerName, string ipaddress, int port, string joinToken = null, string sessionId = null) { }

        public virtual void StartClientSession(string playerName) { }

        public virtual void StartHostIP(string playerName, string ipaddress, int port) { }

        public virtual void StartHostSession(string playerName) { }

        public virtual void OnUserRequestedShutdown() { }

        // Mirror uses NetworkAuthenticator instead of ConnectionApprovalCallback; no-op base method kept for state compatibility
        public virtual void OnTransportFailure() { }

        public virtual void OnServerStopped() { }
    }
}
