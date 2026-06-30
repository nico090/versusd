using System;
using Mirror;
using VContainer;

namespace Unity.BossRoom.Gameplay.GameState
{
    public class NetworkPostGame : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnWinStateSync))]
        public WinState WinState;

        public event Action<WinState, WinState> OnWinStateChangedEvent;

        /// <summary>Final scoreboard sorted by score descending; populated by the server before PostGame loads.</summary>
        public readonly SyncList<ScoreEntry> FinalScoreboard = new SyncList<ScoreEntry>();

        [Inject]
        public void Construct(PersistentGameState persistentGameState)
        {
            if (isServer)
            {
                WinState = persistentGameState.WinState;
            }
        }

        void OnWinStateSync(WinState oldVal, WinState newVal) => OnWinStateChangedEvent?.Invoke(oldVal, newVal);
    }
}
