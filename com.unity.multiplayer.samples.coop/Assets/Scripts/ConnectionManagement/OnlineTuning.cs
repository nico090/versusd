using System.Reflection;
using kcp2k;
using Mirror;
using UnityEngine;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// Network settings for internet play, applied in code at startup rather than left to the
    /// serialized values in Startup.unity.
    /// </summary>
    /// <remarks>
    /// The scene was tuned for a LAN: sendRate 60, a snapshot buffer sized for near-zero jitter,
    /// and a 10s KCP timeout. Every online game now goes through the LRM relay, where the shape of
    /// the traffic is very different from a LAN:
    ///
    /// - The host is a player's PC and *all* of its outbound traffic — one batch per client per
    ///   send tick — is multiplexed onto a SINGLE KCP connection to the relay. That one link is
    ///   the choke point for the whole match, and it is a home upload, not a datacenter one.
    /// - Characters carry a reliable NetworkTransform with syncInterval 0, so every mover
    ///   serializes once per send tick. At sendRate 60 with a full lobby that is several hundred
    ///   reliable deltas per second per client, all sharing that one link. Because it is reliable
    ///   KCP, a single lost segment head-of-line blocks every other client's data behind it.
    ///
    /// The result is a link that sits near saturation during normal play, so any burst — the spawn
    /// flood when someone joins, or a death (life state + score SyncList + the wave spawner's
    /// replacement imp) — pushes it over, latency climbs, and the 10s timeout fires. That matches
    /// the reported failures: stutter during play, plus drops that cluster around joining and
    /// around the first kill.
    ///
    /// These live here instead of in the scene because the scene's serialized values are only
    /// reachable through the Editor, and an Editor with the project open serves its cached copy of
    /// an asset rather than what is on disk — so a scene edit made outside it does not reliably
    /// reach a build. <see cref="Mirror.NetworkManager.ApplyConfiguration"/> re-reads the
    /// NetworkManager fields every Update, so assigning them once in Awake is enough to make them
    /// stick.
    /// </remarks>
    public static class OnlineTuning
    {
        /// <summary>
        /// Server broadcast rate. Halving 60 → 30 halves the bytes crossing the host's single
        /// relay link, which is the one resource the whole match shares. 30Hz with snapshot
        /// interpolation is the normal choice for online action games; the smoothing below is what
        /// makes it look identical to 60.
        ///
        /// One side effect to know about if the dedicated-server mode is ever switched back on
        /// (MasterServerConfig.enableDedicatedServers is currently off, so every room is a relay
        /// room): NetworkManager.ConfigureHeadlessFrameRate pins Application.targetFrameRate to
        /// sendRate on a headless build, so a dedicated server would now simulate at 30Hz instead
        /// of 60. Player and host builds are unaffected — ApplicationController sets 120 and
        /// nothing headless runs there.
        /// </summary>
        public const int SendRate = 30;

        /// <summary>
        /// Starting snapshot buffer, in send intervals. Only used for the first moments of a
        /// connection: once jitter has been measured, dynamic adjustment overwrites the live
        /// multiplier (see <see cref="DynamicAdjustmentTolerance"/>). It matters anyway, because
        /// "the first moments of a connection" is one of the windows where things visibly break.
        /// </summary>
        public const double InitialBufferTimeMultiplier = 3;

        /// <summary>
        /// Safety margin added on top of the measured jitter when sizing the snapshot buffer.
        /// This — not bufferTimeMultiplier — is the real lever once a connection is running, since
        /// dynamic adjustment recomputes the multiplier from jitter + this tolerance every
        /// snapshot. Mirror's own note on the field: 1 is fine for a stable link, 2 is "very very
        /// safe even for 20% jitter". A relay hop to a phone on mobile data is the 20% case, and
        /// an undersized buffer starves the interpolator, which is what rubberbanding is.
        /// </summary>
        public const float DynamicAdjustmentTolerance = 2;

        /// <summary>
        /// How long KCP tolerates hearing nothing before declaring the connection dead.
        /// </summary>
        /// <remarks>
        /// 10s is short for a phone, which can lose its uplink for longer than that on a cell
        /// handover or a few seconds in the background and still be perfectly fine afterwards.
        ///
        /// IMPORTANT: this only buys the client half the fix. The relay applies its own timeout to
        /// the same link, and it defaults to 10000 as well — so until the VPS runs the relay with
        /// KCP_CONNECTION_TIMEOUT set to match this value, the relay still evicts the peer at 10s
        /// and the longer client-side timeout just means the client notices later. Set the env var
        /// on the relay service and the two agree.
        /// </remarks>
        public const int KcpTimeoutMs = 20000;

        /// <summary>
        /// Applies the settings above. Safe to call more than once.
        /// </summary>
        public static void Apply(NetworkManager manager)
        {
            if (manager == null)
            {
                return;
            }

            manager.sendRate = SendRate;

            if (manager.snapshotSettings != null)
            {
                manager.snapshotSettings.bufferTimeMultiplier = InitialBufferTimeMultiplier;
                manager.snapshotSettings.dynamicAdjustmentTolerance = DynamicAdjustmentTolerance;
            }

            // Tune every KcpTransport on the NetworkManager. In relay games this is the transport
            // the LRM wrapper sends through, so it is the connection that actually times out.
            foreach (var kcp in manager.GetComponents<KcpTransport>())
            {
                ApplyKcpTimeout(kcp, KcpTimeoutMs);
            }

            Debug.Log($"[OnlineTuning] sendRate={SendRate} bufferTimeMultiplier={InitialBufferTimeMultiplier} " +
                      $"dynamicAdjustmentTolerance={DynamicAdjustmentTolerance} kcpTimeout={KcpTimeoutMs}ms");
        }

        static void ApplyKcpTimeout(KcpTransport kcp, int timeoutMs)
        {
            // The serialized field is read by KcpTransport.Awake() when it builds its KcpConfig,
            // so setting it covers the case where we run first.
            kcp.Timeout = timeoutMs;

            // If its Awake already ran, that config object is the very instance KcpClient and
            // KcpServer hold, and KcpPeer copies Timeout out of it when a peer is constructed —
            // which happens at connect time, still ahead of us. Mutating it in place therefore
            // takes effect for every connection made from here on, whichever order the two Awakes
            // ran in. Awake order between components of one GameObject is undefined, so we cannot
            // rely on either path alone.
            var field = typeof(KcpTransport).GetField("config", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                // Only worth a warning if the field is gone entirely (a Mirror update renamed it),
                // which is the one case where neither path applies. A null *value* is the normal,
                // fine case of having run before KcpTransport.Awake(), which reads the field above.
                Debug.LogWarning("[OnlineTuning] KcpTransport has no 'config' field any more — a Mirror " +
                                 $"update likely renamed it. The {timeoutMs}ms timeout now only applies when " +
                                 "this runs before KcpTransport.Awake(), which is not guaranteed.");
                return;
            }

            if (field.GetValue(kcp) is KcpConfig config)
            {
                config.Timeout = timeoutMs;
            }
        }
    }
}
