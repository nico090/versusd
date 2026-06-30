using System;
using Mirror;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.GameplayObjects
{
    public enum LifeState
    {
        Alive,
        Fainted,
        Dead,
    }

    /// <summary>
    /// MonoBehaviour containing only one SyncVar of type LifeState which represents this object's life state.
    /// </summary>
    public class NetworkLifeState : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnLifeStateChanged))]
        LifeState m_LifeState = LifeState.Alive;

        /// <summary>
        /// The current life state value. Callers can subscribe to <see cref="LifeStateChanged"/> for change notifications.
        /// </summary>
        public LifeState LifeState
        {
            get => m_LifeState;
            set
            {
                var oldVal = m_LifeState;
                m_LifeState = value;

                // Mirror only invokes the SyncVar hook on clients (and on a host, where
                // NetworkServer.activeHost is true). On a pure dedicated server the hook
                // never fires, so the server-side death pipeline (scoring, respawn,
                // action cleanup) would never see the change. Raise the event here for
                // dedicated-server listeners; guard with !isClient so we don't double-fire
                // on a host, where the generated hook already runs.
                if (isServer && !isClient && oldVal != value)
                {
                    LifeStateChanged?.Invoke(oldVal, value);
                }
            }
        }

        /// <summary>
        /// Fired when LifeState changes: on clients via the SyncVar hook, and on a dedicated
        /// server via the property setter (where Mirror does not run the hook).
        /// Signature: (oldValue, newValue)
        /// </summary>
        public event Action<LifeState, LifeState> LifeStateChanged;

        void OnLifeStateChanged(LifeState oldVal, LifeState newVal)
        {
            LifeStateChanged?.Invoke(oldVal, newVal);
        }

        // NOTE: This SyncVar must NOT be conditionally compiled. Mirror's Weaver generates
        // OnSerialize/OnDeserialize from the SyncVars present in each build; gating it behind
        // UNITY_EDITOR/DEVELOPMENT_BUILD made the release dedicated-server payload (1 byte) and the
        // editor client (2 bytes) disagree, causing per-object "OnDeserialize size mismatch 02 vs 01".
        // The networked field set must be identical on both sides; only the cheat *usage* is gated.
        [SyncVar(hook = nameof(OnIsGodModeChanged))]
        bool m_IsGodMode = false;

        /// <summary>
        /// Indicates whether this character is in "god mode" (cannot be damaged).
        /// </summary>
        public bool IsGodMode
        {
            get => m_IsGodMode;
            set => m_IsGodMode = value;
        }

        public event Action<bool, bool> IsGodModeChanged;

        void OnIsGodModeChanged(bool oldVal, bool newVal)
        {
            IsGodModeChanged?.Invoke(oldVal, newVal);
        }
    }
}
