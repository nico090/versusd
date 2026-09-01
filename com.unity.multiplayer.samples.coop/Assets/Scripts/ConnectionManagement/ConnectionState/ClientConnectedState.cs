using LightReflectiveMirror;
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
            // The relay tells a client outright when the room it was in is gone, which is what
            // happens the moment the host quits. Retrying that is pointless — there is nothing
            // left to reconnect to — and it used to cost the player the whole reconnect loop
            // before landing on a generic "connection lost". Say what happened and go back to the
            // menu.
            if (LightReflectiveMirrorTransport.RoomClosedRecently())
            {
                m_ConnectStatusPublisher.Publish(ConnectStatus.HostEndedSession);
                m_ConnectionManager.ChangeState(m_ConnectionManager.m_Offline);
                return;
            }

            // Mirror does not carry a structured disconnect reason by default.
            // Treat all other disconnects as reconnectable unless explicitly shut down.
            m_ConnectStatusPublisher.Publish(ConnectStatus.Reconnecting);
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_ClientReconnecting);
        }
    }
}
