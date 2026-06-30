using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Unity.Multiplayer.Samples.Utilities
{
    /// <summary>
    /// Tracks scene loading progress for the local client and, on the server, spawns one
    /// <see cref="NetworkedLoadingProgressTracker"/> per connection so every player can see each
    /// other's loading bars. Lives on a DontDestroyOnLoad bootstrap object (Startup scene).
    /// </summary>
    public class LoadingProgressManager : MonoBehaviour
    {
        public static LoadingProgressManager Instance { get; private set; }

        [SerializeField]
        GameObject m_ProgressTrackerPrefab;

        public Dictionary<ulong, NetworkedLoadingProgressTracker> ProgressTrackers { get; } =
            new Dictionary<ulong, NetworkedLoadingProgressTracker>();

        // Server-side: tracker spawned for each connection, keyed by connectionId, so we can
        // despawn it when that client disconnects.
        readonly Dictionary<int, NetworkedLoadingProgressTracker> m_ServerTrackers = new();

        public AsyncOperation LocalLoadOperation
        {
            set
            {
                m_IsLoading = true;
                LocalProgress = 0;
                m_LocalLoadOperation = value;
            }
        }

        AsyncOperation m_LocalLoadOperation;

        float m_LocalProgress;

        bool m_IsLoading;

        public event Action onTrackersUpdated;

        public float LocalProgress
        {
            get => m_LocalProgress;
            private set => m_LocalProgress = value;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        void Update()
        {
            if (m_LocalLoadOperation != null && m_IsLoading)
            {
                if (m_LocalLoadOperation.isDone)
                {
                    m_IsLoading = false;
                    LocalProgress = 1;
                }
                else
                {
                    LocalProgress = m_LocalLoadOperation.progress;
                }
            }
        }

        /// <summary>
        /// Called by a <see cref="NetworkedLoadingProgressTracker"/> on every client (including the
        /// owner) so the loading screen can list it. The screen identifies the local player's own
        /// tracker via NetworkBehaviour.isOwned, so we register all of them here.
        /// </summary>
        public void RegisterTracker(NetworkedLoadingProgressTracker tracker)
        {
            ProgressTrackers[tracker.netId] = tracker;
            onTrackersUpdated?.Invoke();
        }

        public void UnregisterTracker(NetworkedLoadingProgressTracker tracker)
        {
            if (ProgressTrackers.Remove(tracker.netId))
            {
                onTrackersUpdated?.Invoke();
            }
        }

        /// <summary>
        /// Server-only. Spawns a progress tracker owned by <paramref name="conn"/>. Idempotent per
        /// connection. Call once the connection is ready (so it actually receives the spawn).
        /// </summary>
        public void ServerSpawnTrackerFor(NetworkConnectionToClient conn)
        {
            if (!NetworkServer.active || conn == null)
            {
                return;
            }
            if (m_ProgressTrackerPrefab == null)
            {
                Debug.LogError("[LoadingProgressManager] m_ProgressTrackerPrefab is not assigned — " +
                    "cannot spawn per-client loading trackers. Assign the tracker prefab and add it " +
                    "to NetworkManager.spawnPrefabs.");
                return;
            }
            if (m_ServerTrackers.ContainsKey(conn.connectionId))
            {
                return; // already has one
            }

            var go = Instantiate(m_ProgressTrackerPrefab);
            var tracker = go.GetComponent<NetworkedLoadingProgressTracker>();
            if (tracker == null)
            {
                Debug.LogError("[LoadingProgressManager] Tracker prefab has no NetworkedLoadingProgressTracker component.");
                Destroy(go);
                return;
            }
            NetworkServer.Spawn(go, conn);
            m_ServerTrackers[conn.connectionId] = tracker;
        }

        /// <summary>
        /// Server-only. Despawns the tracker that was spawned for <paramref name="conn"/>.
        /// </summary>
        public void ServerDespawnTrackerFor(NetworkConnectionToClient conn)
        {
            if (conn == null)
            {
                return;
            }
            if (m_ServerTrackers.Remove(conn.connectionId, out var tracker) && tracker != null)
            {
                NetworkServer.Destroy(tracker.gameObject);
            }
        }
    }
}
