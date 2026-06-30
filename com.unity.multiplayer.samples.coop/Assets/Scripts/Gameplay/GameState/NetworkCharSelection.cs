using System;
using Mirror;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Utils;

namespace Unity.BossRoom.Gameplay.GameState
{
    /// <summary>
    /// Common data and RPCs for the CharSelect stage.
    /// </summary>
    public class NetworkCharSelection : NetworkBehaviour
    {
        public enum SeatState : byte
        {
            Inactive,
            Active,
            LockedIn,
        }

        /// <summary>
        /// Describes one of the players in the session, and their current character-select status.
        /// </summary>
        public struct SessionPlayerState : IEquatable<SessionPlayerState>
        {
            public ulong ClientId;

            // Stable master-server player id. Unlike ClientId (the server-side
            // connectionId, which a remote client never learns), this is known to
            // both server and client, so a client can locate its own row.
            public string PlayerId;

            public string PlayerName;

            public int PlayerNumber; // this player's assigned "P#". (0=P1, 1=P2, etc.)
            public int SeatIdx; // the latest seat they were in. -1 means none
            public float LastChangeTime;

            public SeatState SeatState;


            public SessionPlayerState(ulong clientId, string playerId, string name, int playerNumber, SeatState state, int seatIdx = -1, float lastChangeTime = 0)
            {
                ClientId = clientId;
                PlayerId = playerId;
                PlayerNumber = playerNumber;
                SeatState = state;
                SeatIdx = seatIdx;
                LastChangeTime = lastChangeTime;
                PlayerName = name;
            }

            public bool Equals(SessionPlayerState other)
            {
                return ClientId == other.ClientId &&
                       PlayerId == other.PlayerId &&
                       PlayerName == other.PlayerName &&
                       PlayerNumber == other.PlayerNumber &&
                       SeatIdx == other.SeatIdx &&
                       LastChangeTime.Equals(other.LastChangeTime) &&
                       SeatState == other.SeatState;
            }
        }

        public readonly SyncList<SessionPlayerState> sessionPlayers = new SyncList<SessionPlayerState>();

        public Avatar[] AvatarConfiguration;

        [SyncVar]
        bool m_IsSessionClosed;

        /// <summary>
        /// When this becomes true, the session is closed and in process of terminating (switching to gameplay).
        /// </summary>
        public bool IsSessionClosed
        {
            get => m_IsSessionClosed;
            set => m_IsSessionClosed = value;
        }

        /// <summary>
        /// Server notification when a client requests a different session-seat, or locks in their seat choice
        /// </summary>
        public event Action<ulong, int, bool> OnClientChangedSeat;

        /// <summary>
        /// Command to notify the server that a client has chosen a seat. The calling client's identity is
        /// taken from the command sender (server-authoritative) rather than trusting a client-supplied id.
        /// </summary>
        [Command(requiresAuthority = false)]
        public void CmdChangeSeat(int seatIdx, bool lockedIn, NetworkConnectionToClient sender = null)
        {
            ulong clientId = (ulong)(uint)(sender?.connectionId ?? 0);
            OnClientChangedSeat?.Invoke(clientId, seatIdx, lockedIn);
        }
    }
}
