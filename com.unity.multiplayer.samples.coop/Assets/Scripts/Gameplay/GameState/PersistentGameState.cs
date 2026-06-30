using System.Collections.Generic;

namespace Unity.BossRoom.Gameplay.GameState
{
    public enum WinState
    {
        Invalid,
        Win,
        Loss
    }

    /// <summary>
    /// Carries match outcome between ServerBossRoomState and PostGameState.
    /// </summary>
    public class PersistentGameState
    {
        public WinState WinState { get; private set; }

        public IReadOnlyList<ScoreEntry> FinalScoreboard => m_FinalScoreboard;
        List<ScoreEntry> m_FinalScoreboard = new List<ScoreEntry>();

        public void SetWinState(WinState winState) => WinState = winState;

        public void SetFinalScoreboard(List<ScoreEntry> sorted) => m_FinalScoreboard = sorted;

        public void Reset()
        {
            WinState = WinState.Invalid;
            m_FinalScoreboard.Clear();
        }
    }
}
