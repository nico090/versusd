using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Unity.BossRoom.Infrastructure
{
    /// <summary>
    /// A networked channel whose client-side handler can be put back after Mirror drops it.
    /// </summary>
    public interface IReRegisterableChannel
    {
        void RegisterClientHandler();
    }

    /// <summary>
    /// Keeps track of every live <see cref="NetworkedMessageChannel{T}"/> so their handlers can be
    /// re-registered when a client starts.
    /// </summary>
    /// <remarks>
    /// This exists because of a lifetime mismatch. The channels are created once, in
    /// ApplicationController, and live for the whole process — but
    /// <see cref="NetworkClient.Shutdown"/> calls handlers.Clear(), and Mirror runs it from
    /// NetworkManager.OnClientDisconnectInternal on *every* disconnect. So after the first time a
    /// client stops, nothing was listening for these messages again for the rest of the process:
    /// the next LifeStateChangedEventMessage or ConnectionEventMessage the server sent arrived at
    /// a client with no handler for it, and Mirror answers an unknown message id by disconnecting
    /// ("Unknown message id" → "failed to unpack and invoke message. Disconnecting."). A client
    /// dropping that way takes the room with it when the client is the host.
    /// </remarks>
    public static class NetworkedMessageChannelRegistry
    {
        static readonly List<IReRegisterableChannel> s_Channels = new List<IReRegisterableChannel>();

        internal static void Add(IReRegisterableChannel channel)
        {
            if (!s_Channels.Contains(channel))
            {
                s_Channels.Add(channel);
            }
        }

        internal static void Remove(IReRegisterableChannel channel)
        {
            s_Channels.Remove(channel);
        }

        /// <summary>
        /// Call once per client start, after Mirror has set NetworkClient up.
        /// </summary>
        public static void RegisterClientHandlers()
        {
            foreach (var channel in s_Channels)
            {
                channel.RegisterClientHandler();
            }
        }
    }

    /// <summary>
    /// Networked message channel: server publishes → Mirror broadcasts to all clients → local pub/sub fires on each.
    /// Clients subscribe and receive via NetworkClient handler; server publishes via NetworkServer.SendToAll.
    /// </summary>
    public class NetworkedMessageChannel<T> : MessageChannel<T>, IReRegisterableChannel where T : struct, NetworkMessage
    {
        bool m_HandlerRegistered;

        public NetworkedMessageChannel()
        {
            NetworkedMessageChannelRegistry.Add(this);
            RegisterClientHandler();
        }

        /// <summary>
        /// Registers (or re-registers) the client handler. Idempotent — ReplaceHandler rather than
        /// RegisterHandler, which warns when a handler for the type already exists.
        /// </summary>
        public void RegisterClientHandler()
        {
            if (IsDisposed)
            {
                return;
            }

            NetworkClient.ReplaceHandler<T>(OnReceiveFromServer, requireAuthentication: false);
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
            NetworkedMessageChannelRegistry.Remove(this);
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
