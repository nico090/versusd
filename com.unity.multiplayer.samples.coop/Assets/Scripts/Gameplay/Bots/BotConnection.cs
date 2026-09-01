using System;
using Mirror;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.Bots
{
    /// <summary>
    /// A Mirror server connection with no transport behind it, used to represent a bot.
    /// </summary>
    /// <remarks>
    /// <para>This is the whole trick that lets bots "be players" instead of being a parallel system.
    /// Practically every piece of this game keys off a Mirror connection id: CharSelect seats
    /// (<c>NetworkCharSelection.SessionPlayerState.ClientId</c>), the session store
    /// (<c>SessionManager</c>), avatar ownership (<c>ServerCharacter.OwnerClientId</c>), the live
    /// scoreboard (<c>ScoreEntry.ClientId</c>) and kill attribution
    /// (<c>PublishMessageOnLifeChange</c>). Give a bot a real connection object and every one of
    /// those systems works on it unmodified — a bot picks a seat, gets a PersistentPlayer, spawns
    /// an avatar, scores kills and shows up on the final table through the ordinary code paths.
    /// The alternative — a bot-shaped special case threaded through each of those systems — is the
    /// thing that rots.</para>
    ///
    /// <para>The only thing this connection cannot do is receive: <see cref="SendToTransport"/> is
    /// a no-op, because there is nobody on the other end. Mirror still serialises and batches
    /// messages for it, which is a small, bounded cost, and after the first scene change the
    /// connection stops being "ready" (bots never send a ReadyMessage) so Mirror stops broadcasting
    /// to it altogether.</para>
    /// </remarks>
    public class BotConnection : NetworkConnectionToClient
    {
        /// <summary>
        /// Bot connection ids start well above anything a transport hands out. KCP derives ids from
        /// a random int and LRM counts up from zero, so a band this high is effectively collision
        /// free — and <see cref="AllocateConnectionId"/> checks for a collision anyway.
        /// </summary>
        const int k_ConnectionIdBase = 1_000_000;

        static int s_NextConnectionId = k_ConnectionIdBase;

        /// <summary>The personality driving this bot, including its display name.</summary>
        public BotProfile Profile { get; }

        /// <summary>
        /// The bot's stand-in for a master-server player id. Clients match their own row in
        /// CharSelect and on the scoreboard by PlayerId, so this has to be present and unique —
        /// but it must never collide with a real player's id, hence the prefix.
        /// </summary>
        public string PlayerId { get; }

        public string PlayerName => Profile?.AssignedName ?? "Bot";

        /// <summary>The bot's connection id as the rest of the game sees it.</summary>
        public ulong ClientId => (ulong)(uint)connectionId;

        public BotConnection(int connectionId, BotProfile profile, string playerId)
            : base(connectionId, "bot")
        {
            Profile = profile;
            PlayerId = playerId;
        }

        /// <summary>
        /// Where a real connection hands bytes to the transport, a bot drops them. Everything
        /// upstream — batching, serialisation, observer bookkeeping — still runs normally, so
        /// Mirror never has to know this connection is special.
        /// </summary>
        protected override void SendToTransport(ArraySegment<byte> segment, int channelId = Channels.Reliable)
        {
        }

        /// <summary>
        /// Never asks the transport to disconnect: <c>connectionId</c> is not a transport id, and
        /// handing it to <c>Transport.ServerDisconnect</c> would at best log an error. Bots are
        /// torn down by <see cref="ServerBotManager.RemoveBot"/>, which runs the same cleanup
        /// Mirror runs for a real dropped client.
        /// </summary>
        public override void Disconnect()
        {
            isReady = false;
        }

        /// <summary>
        /// Keeps the connection from being reaped when <c>NetworkServer.disconnectInactiveConnections</c>
        /// is on. A bot never receives anything, so its <c>lastMessageTime</c> would otherwise sit at
        /// its creation time and cross the inactivity timeout a minute into every match.
        /// </summary>
        public void KeepAlive()
        {
            lastMessageTime = Time.time;
        }

        /// <summary>An id no live connection is using. Never returns a colliding id.</summary>
        public static int AllocateConnectionId()
        {
            // Guard against wrap-around into the transport's range on a very long-lived server.
            if (s_NextConnectionId < k_ConnectionIdBase)
            {
                s_NextConnectionId = k_ConnectionIdBase;
            }

            while (NetworkServer.connections.ContainsKey(s_NextConnectionId))
            {
                s_NextConnectionId++;
            }

            return s_NextConnectionId++;
        }

        /// <summary>True if the given connection id belongs to a bot rather than a human.</summary>
        public static bool IsBot(ulong clientId) =>
            NetworkServer.connections.TryGetValue((int)(uint)clientId, out var conn) && conn is BotConnection;

        /// <summary>The bot connection for this client id, or null if it's a human (or gone).</summary>
        public static BotConnection Find(ulong clientId) =>
            NetworkServer.connections.TryGetValue((int)(uint)clientId, out var conn) ? conn as BotConnection : null;
    }
}
