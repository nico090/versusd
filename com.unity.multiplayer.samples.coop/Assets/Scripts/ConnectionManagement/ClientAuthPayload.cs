namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// Credentials the client hands to Mirror's NetworkAuthenticator during the
    /// connection handshake. Set this (via the active ConnectionMethod) BEFORE
    /// calling NetworkManager.StartClient()/StartHost().
    ///
    /// Lives in the ConnectionManagement assembly (not the Mirror one) so the
    /// ConnectionMethod can populate it without a circular asmdef reference; the
    /// authenticator in Unity.BossRoom.Mirror reads it back.
    /// </summary>
    public static class ClientAuthPayload
    {
        public static ClientAuthPayloadData Current { get; private set; }

        public static void Set(string playerId, string playerName, bool isDebug,
            string joinToken = "", string sessionId = "")
        {
            Current = new ClientAuthPayloadData
            {
                PlayerId = playerId,
                PlayerName = playerName,
                IsDebug = isDebug,
                JoinToken = joinToken ?? string.Empty,
                SessionId = sessionId ?? string.Empty,
            };
        }

        public static void Clear() => Current = null;
    }

    public class ClientAuthPayloadData
    {
        public string PlayerId;
        public string PlayerName;
        public bool IsDebug;
        public string JoinToken;
        public string SessionId;
    }
}
