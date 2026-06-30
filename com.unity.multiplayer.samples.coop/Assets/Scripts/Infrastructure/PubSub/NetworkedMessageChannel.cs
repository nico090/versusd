using Mirror;
using UnityEngine;

namespace Unity.BossRoom.Infrastructure
{
    /// <summary>
    /// Networked message channel: server publishes → Mirror broadcasts to all clients → local pub/sub fires on each.
    /// Clients subscribe and receive via NetworkClient handler; server publishes via NetworkServer.SendToAll.
    /// </summary>
    public class NetworkedMessageChannel<T> : MessageChannel<T> where T : struct, NetworkMessage
    {
        bool m_HandlerRegistered;

        public NetworkedMessageChannel()
        {
            NetworkClient.RegisterHandler<T>(OnReceiveFromServer, requireAuthentication: false);
            m_HandlerRegistered = true;
        }

        public override void Publish(T message)
        {
            if (NetworkServer.active)
            {
                NetworkServer.SendToAll(message);
                // Also fire locally on the server/host
                base.Publish(message);
            }
            else
            {
                Debug.LogError($"[NetworkedMessageChannel] Only the server can publish {typeof(T).Name}");
            }
        }

        public override void Dispose()
        {
            if (!IsDisposed && m_HandlerRegistered)
            {
                NetworkClient.UnregisterHandler<T>();
                m_HandlerRegistered = false;
            }
            base.Dispose();
        }

        void OnReceiveFromServer(T message)
        {
            // Host receives via NetworkServer.SendToAll path (handled in Publish) → skip double-fire
            if (!NetworkServer.active)
            {
                base.Publish(message);
            }
        }
    }
}
