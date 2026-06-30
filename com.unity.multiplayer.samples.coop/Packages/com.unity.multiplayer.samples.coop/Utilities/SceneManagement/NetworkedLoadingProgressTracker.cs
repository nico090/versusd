using System;
using Mirror;
using UnityEngine;

namespace Unity.Multiplayer.Samples.Utilities
{
    /// <summary>
    /// Tracks scene-loading progress for a single client and replicates it to everyone.
    ///
    /// The server spawns one tracker per connection (see <see cref="LoadingProgressManager"/>) and
    /// assigns ownership to that client. The owning client pushes its local load progress to the
    /// server via <see cref="CmdSetProgress"/>; the server relays it through a SyncVar so every
    /// client can display every other player's loading bar.
    ///
    /// <see cref="Progress"/> keeps the NGO-style NetworkVariable API so <see cref="ClientLoadingScreen"/>
    /// requires no changes — its value is driven by the SyncVar hook.
    /// </summary>
    public class NetworkedLoadingProgressTracker : NetworkBehaviour
    {
        public class ProgressVariable
        {
            float m_Value;

            public event Action<float, float> OnValueChanged;

            public float Value
            {
                get => m_Value;
                set
                {
                    var old = m_Value;
                    m_Value = value;
                    if (!Mathf.Approximately(old, value))
                        OnValueChanged?.Invoke(old, value);
                }
            }
        }

        public ProgressVariable Progress { get; } = new ProgressVariable();

        // Server-authoritative synced progress, relayed to all clients. NOTE: never gate a SyncVar
        // behind #if UNITY_EDITOR/DEVELOPMENT_BUILD — the Weaver would generate a different
        // OnSerialize for the Editor client vs the release dedicated server and every payload would
        // mismatch. This field must always compile.
        [SyncVar(hook = nameof(OnProgressSynced))]
        float m_SyncedProgress;

        // Last value the owner pushed to the server, to avoid spamming Commands every frame.
        float m_LastSent = -1f;

        void Awake()
        {
            DontDestroyOnLoad(this);
        }

        void OnProgressSynced(float oldValue, float newValue)
        {
            Progress.Value = newValue;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Seed the local mirror with whatever the server already has (covers late joins into an
            // in-progress load), then register so the loading screen can show this player's bar.
            Progress.Value = m_SyncedProgress;
            LoadingProgressManager.Instance?.RegisterTracker(this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            LoadingProgressManager.Instance?.UnregisterTracker(this);
        }

        void Update()
        {
            // Only the owning client reports its own local progress; every other client just
            // receives it via the SyncVar.
            if (!isOwned || LoadingProgressManager.Instance == null)
                return;

            float local = LoadingProgressManager.Instance.LocalProgress;
            if (!Mathf.Approximately(local, m_LastSent))
            {
                m_LastSent = local;
                CmdSetProgress(local);
            }
        }

        [Command]
        void CmdSetProgress(float value)
        {
            m_SyncedProgress = value;
        }
    }
}
