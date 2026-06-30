using Mirror;

namespace Unity.BossRoom.Infrastructure
{
    /// <summary>
    /// Utility helpers to look up NetworkIdentity objects by netId, mirroring the NGO
    /// pattern of NetworkManager.Singleton.SpawnManager.SpawnedObjects[id].
    ///
    /// On the server use NetworkServer.spawned; on the client use NetworkClient.spawned.
    /// This helper checks both so call-sites don't need to know which side they are on.
    /// </summary>
    public static class NetworkIdentityUtils
    {
        /// <summary>
        /// Find a spawned NetworkIdentity by its netId.
        /// Returns null if not found.
        /// </summary>
        public static NetworkIdentity FindByNetId(uint netId)
        {
            if (netId == 0) return null;

            // Server-side lookup
            if (NetworkServer.active && NetworkServer.spawned.TryGetValue(netId, out var serverIdentity))
            {
                return serverIdentity;
            }

            // Client-side lookup
            if (NetworkClient.spawned.TryGetValue(netId, out var clientIdentity))
            {
                return clientIdentity;
            }

            return null;
        }
    }
}
