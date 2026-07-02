using System;
using Mirror;
using Unity.BossRoom.Gameplay.UI;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.GameState
{
    public struct ScoreEntry : IEquatable<ScoreEntry>
    {
        // Server-assigned connectionId. NOTE: never told to a remote client
        // (LocalConnectionId is 0 on every client), so clients must NOT use this
        // to identify their own row — match on PlayerId instead.
        public ulong ClientId;
        // Stable master-server PlayerId. Known on both sides (server via
        // SessionManager.GetPlayerId, client via ClientAuthPayload.Current.PlayerId),
        // so this is the correct key for a client to find its own entry.
        public string PlayerId;
        public string PlayerName;
        public int PlayerNumber;
        public int Score;

        public bool Equals(ScoreEntry other) =>
            ClientId == other.ClientId &&
            PlayerId == other.PlayerId &&
            PlayerName == other.PlayerName &&
            PlayerNumber == other.PlayerNumber &&
            Score == other.Score;

        /// <summary>
        /// Deterministic ranking order: highest score first, ties broken by ascending
        /// PlayerNumber. Every client/server sorts identically, so they all agree on the
        /// ordering — and therefore on who the winner is (sorted[0]). A plain score-only
        /// sort left tied rows in an undefined order, so different peers could disagree.
        /// </summary>
        public static int CompareForRanking(ScoreEntry a, ScoreEntry b)
        {
            int byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : a.PlayerNumber.CompareTo(b.PlayerNumber);
        }
    }

    /// <summary>
    /// Networked game state for a deathmatch session: syncs the countdown timer
    /// and the live scoreboard to all clients.
    /// Lives on the same GameObject as ServerBossRoomState.
    /// </summary>
    public class NetworkGameState : NetworkBehaviour
    {
        public const float MatchDuration = 300f;

        /// <summary>
        /// Score that wins the match. First player to reach it ends the game (free-for-all).
        /// The countdown timer is only a fallback: if nobody reaches this before time runs
        /// out, the highest score wins.
        /// </summary>
        public const int TargetScore = 25;

        [SyncVar(hook = nameof(OnTimeRemainingSync))]
        float m_TimeRemaining = MatchDuration;

        public float TimeRemaining => m_TimeRemaining;

        public event Action<float, float> OnTimeRemainingChangedEvent;

        void OnTimeRemainingSync(float oldVal, float newVal) =>
            OnTimeRemainingChangedEvent?.Invoke(oldVal, newVal);

        public readonly SyncList<ScoreEntry> Scores = new SyncList<ScoreEntry>();

        public override void OnStartClient()
        {
            DeathmatchHUD.EnsureInstance();
        }

        void Update()
        {
            if (!isServer || m_TimeRemaining <= 0f) return;
            m_TimeRemaining = Mathf.Max(0f, m_TimeRemaining - Time.deltaTime);
        }

        /// <summary>Server-only: add a player to the scoreboard at match start.</summary>
        public void RegisterPlayer(ulong clientId, string playerId, string playerName, int playerNumber)
        {
            for (int i = 0; i < Scores.Count; i++)
                if (Scores[i].ClientId == clientId) return;

            Scores.Add(new ScoreEntry
            {
                ClientId = clientId,
                PlayerId = playerId,
                PlayerName = playerName,
                PlayerNumber = playerNumber,
                Score = 0,
            });
        }

        /// <summary>Server-only: true once any player's score has reached <see cref="TargetScore"/>.</summary>
        public bool HasPlayerReachedTarget()
        {
            for (int i = 0; i < Scores.Count; i++)
                if (Scores[i].Score >= TargetScore) return true;
            return false;
        }

        /// <summary>Server-only: remove a player from the scoreboard (e.g. on disconnect),
        /// so a player who left can't linger on the board or "win" while absent.</summary>
        public void RemovePlayer(ulong clientId)
        {
            for (int i = 0; i < Scores.Count; i++)
            {
                if (Scores[i].ClientId == clientId)
                {
                    Scores.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>Server-only: apply a score delta (positive or negative) to a player.</summary>
        public void ApplyScoreDelta(ulong clientId, int delta)
        {
            for (int i = 0; i < Scores.Count; i++)
            {
                if (Scores[i].ClientId == clientId)
                {
                    var entry = Scores[i];
                    entry.Score += delta;
                    Scores[i] = entry;
                    return;
                }
            }
        }
    }
}
