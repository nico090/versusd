using System;
using Mirror;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
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
        /// <summary>Players killed. Used as the tie-breaker for the final ranking.</summary>
        public int PlayerKills;
        /// <summary>Minor NPCs (imps) killed. Informational, shown on the final table.</summary>
        public int NpcKills;
        /// <summary>True if this player landed the killing blow on the boss.</summary>
        public bool KilledBoss;

        /// <summary>
        /// True if this row is a bot rather than a person.
        /// </summary>
        /// <remarks>
        /// Recorded at registration, on the server, where the connection type still answers the
        /// question. It cannot be worked out later: by the time the final table is built the bots
        /// have gone, and their names are ordinary ones drawn from a pool, so nothing about the
        /// entry itself gives them away. Two things need it — the table, so a player can see who
        /// they were actually up against, and the ranked report, which must not submit them.
        /// </remarks>
        public bool IsBot;

        public bool Equals(ScoreEntry other) =>
            ClientId == other.ClientId &&
            PlayerId == other.PlayerId &&
            PlayerName == other.PlayerName &&
            PlayerNumber == other.PlayerNumber &&
            IsBot == other.IsBot &&
            Score == other.Score &&
            PlayerKills == other.PlayerKills &&
            NpcKills == other.NpcKills &&
            KilledBoss == other.KilledBoss;

        /// <summary>
        /// Deterministic ranking order: highest score first, then most player kills (the tie-break
        /// rule from the design doc — killing players beats farming imps), then ascending
        /// PlayerNumber as a final deterministic fallback. Every client/server sorts identically,
        /// so they all agree on the ordering — and therefore on who the winner is (sorted[0]).
        /// A plain score-only sort left tied rows in an undefined order, so different peers could
        /// disagree.
        /// </summary>
        public static int CompareForRanking(ScoreEntry a, ScoreEntry b)
        {
            int byScore = b.Score.CompareTo(a.Score);
            if (byScore != 0) return byScore;

            int byKills = b.PlayerKills.CompareTo(a.PlayerKills);
            if (byKills != 0) return byKills;

            return a.PlayerNumber.CompareTo(b.PlayerNumber);
        }
    }

    /// <summary>
    /// Networked game state for a deathmatch session: owns the countdown timer, the match phase
    /// and the live scoreboard, and syncs all three to every client. This is the
    /// "MatchTimerManager + ScoreManager" of the PvPvE design doc.
    /// Lives on the same GameObject as ServerBossRoomState.
    /// </summary>
    /// <remarks>
    /// The timer and phase live here rather than in a separate MatchTimerManager component because
    /// the game-state GameObject is a scene object with a baked Mirror component layout: a
    /// NetworkBehaviour cannot be added to an already-spawned NetworkIdentity at runtime, and
    /// adding one in the scene would need an Editor session. Same logic, one fewer moving part.
    /// </remarks>
    public class NetworkGameState : NetworkBehaviour
    {
        /// <summary>Match length in seconds.</summary>
        public const float MatchDuration = DeathmatchRules.MatchDuration;

        [SyncVar(hook = nameof(OnTimeRemainingSync))]
        float m_TimeRemaining = MatchDuration;

        public float TimeRemaining => m_TimeRemaining;

        public event Action<float, float> OnTimeRemainingChangedEvent;

        void OnTimeRemainingSync(float oldVal, float newVal) =>
            OnTimeRemainingChangedEvent?.Invoke(oldVal, newVal);

        /// <summary>Seconds left of the warm-up, before the match clock starts.</summary>
        /// <remarks>
        /// A clock of its own rather than a reuse of <see cref="m_TimeRemaining"/>. The match
        /// timer is read as "how much match is left" by everything downstream —
        /// <see cref="ServerBossSpawner"/> waits for it to fall under
        /// <see cref="DeathmatchRules.BossSpawnTimeRemaining"/> — so running the warm-up through it
        /// would put the boss on the map before the match had even started.
        /// </remarks>
        [SyncVar(hook = nameof(OnWarmupRemainingSync))]
        float m_WarmupRemaining;

        public float WarmupRemaining => m_WarmupRemaining;

        public event Action<float, float> OnWarmupRemainingChangedEvent;

        void OnWarmupRemainingSync(float oldVal, float newVal) =>
            OnWarmupRemainingChangedEvent?.Invoke(oldVal, newVal);

        [SyncVar(hook = nameof(OnPhaseSync))]
        MatchPhase m_Phase = MatchPhase.PreGame;

        public MatchPhase Phase => m_Phase;

        public event Action<MatchPhase, MatchPhase> OnPhaseChangedEvent;

        void OnPhaseSync(MatchPhase oldVal, MatchPhase newVal) =>
            OnPhaseChangedEvent?.Invoke(oldVal, newVal);

        /// <summary>Fired client-side when the server announces a phase change (banner/sfx).</summary>
        public event Action<MatchPhase> OnPhaseAnnouncedEvent;

        /// <summary>Fired client-side for every kill: killer, victim, points awarded.</summary>
        public event Action<string, string, int> OnKillFeedEvent;

        public readonly SyncList<ScoreEntry> Scores = new SyncList<ScoreEntry>();

        /// <summary>
        /// The effect zones currently on the ground. Server writes, everyone reads.
        /// </summary>
        /// <remarks>
        /// Lives here rather than on an object of its own because this component is already a
        /// NetworkBehaviour sitting on the match-state object, so the whole zone feature costs no
        /// new prefab and no new spawn registration — which in this project is the difference
        /// between a code change and an asset change that the Editor cache can quietly revert.
        /// <see cref="ServerZoneSpawner"/> owns every write; clients only draw what they find here.
        /// </remarks>
        public readonly SyncList<ZoneState> Zones = new SyncList<ZoneState>();

        /// <summary>
        /// Accumulator for the once-per-second timer write. Updating the SyncVar every frame would
        /// push 60 messages/second at every client for a number that only ever shows two digits;
        /// the HUD interpolates the in-between second locally.
        /// </summary>
        float m_SecondAccumulator;

        public override void OnStartClient()
        {
            DeathmatchHUD.EnsureInstance();
        }

        /// <summary>
        /// Server-only: open the warm-up. The match clock itself starts
        /// <see cref="DeathmatchRules.WarmupDuration"/> seconds later, in <see cref="Update"/>.
        /// Idempotent.
        /// </summary>
        public void StartMatch()
        {
            if (!isServer || m_Phase != MatchPhase.PreGame) return;

            m_TimeRemaining = MatchDuration;
            m_WarmupRemaining = DeathmatchRules.WarmupDuration;
            m_SecondAccumulator = 0f;
            SetPhase(MatchPhase.Warmup);
        }

        void Update()
        {
            if (!isServer) return;

            if (m_Phase == MatchPhase.Warmup)
            {
                TickWarmup();
                return;
            }

            if (m_Phase != MatchPhase.Normal && m_Phase != MatchPhase.DoubleKills) return;

            m_SecondAccumulator += Time.deltaTime;
            if (m_SecondAccumulator < 1f && m_TimeRemaining > 1f)
            {
                // Not a full second yet, and we're not about to hit zero — nothing to publish.
                return;
            }

            m_TimeRemaining = Mathf.Max(0f, m_TimeRemaining - m_SecondAccumulator);
            m_SecondAccumulator = 0f;

            if (m_TimeRemaining <= 0f)
            {
                SetPhase(MatchPhase.Ended);
            }
            else if (m_Phase == MatchPhase.Normal && m_TimeRemaining <= DeathmatchRules.DoubleKillsThreshold)
            {
                SetPhase(MatchPhase.DoubleKills);
            }
        }

        /// <summary>
        /// Server-only: run down the warm-up clock, then hand over to the match proper.
        /// </summary>
        /// <remarks>
        /// Published once a second for the same reason the match timer is — the number on screen
        /// only ever shows two digits, and the HUD ticks the in-between second locally.
        /// </remarks>
        void TickWarmup()
        {
            m_SecondAccumulator += Time.deltaTime;
            if (m_SecondAccumulator < 1f && m_WarmupRemaining > 1f)
            {
                return;
            }

            m_WarmupRemaining = Mathf.Max(0f, m_WarmupRemaining - m_SecondAccumulator);
            m_SecondAccumulator = 0f;

            if (m_WarmupRemaining <= 0f)
            {
                // The match clock has been sitting at its full length all through the warm-up, so
                // there is nothing to reset here: this is simply the moment it starts moving.
                SetPhase(MatchPhase.Normal);
            }
        }

        /// <summary>Server-only: change phase and announce it to clients.</summary>
        void SetPhase(MatchPhase phase)
        {
            if (m_Phase == phase) return;
            m_Phase = phase;

            // Damage immunity is a property of the phase, not of any one character, so it is
            // switched here — the one place the phase can change — rather than pushed to every
            // ServerCharacter as players spawn, respawn and join late.
            ServerCharacter.MatchWarmup = phase == MatchPhase.Warmup;

            RpcAnnouncePhase(phase);
        }

        [ClientRpc]
        void RpcAnnouncePhase(MatchPhase phase)
        {
            OnPhaseAnnouncedEvent?.Invoke(phase);
        }

        /// <summary>Server-only: broadcast one line of kill feed to everyone.</summary>
        public void BroadcastKill(string killerName, string victimName, int points)
        {
            if (!isServer) return;
            RpcKillFeed(killerName, victimName, points);
        }

        [ClientRpc]
        void RpcKillFeed(string killerName, string victimName, int points)
        {
            OnKillFeedEvent?.Invoke(killerName, victimName, points);
        }

        /// <summary>
        /// Points a player kill is worth right now — doubled during the final phase.
        /// </summary>
        public int CurrentPlayerKillValue =>
            m_Phase == MatchPhase.DoubleKills
                ? DeathmatchRules.PointsPerPlayerKill * DeathmatchRules.DoubleKillsMultiplier
                : DeathmatchRules.PointsPerPlayerKill;

        /// <summary>Server-only: add a player to the scoreboard at match start.</summary>
        public void RegisterPlayer(ulong clientId, string playerId, string playerName, int playerNumber,
            bool isBot = false)
        {
            for (int i = 0; i < Scores.Count; i++)
                if (Scores[i].ClientId == clientId) return;

            Scores.Add(new ScoreEntry
            {
                ClientId = clientId,
                PlayerId = playerId,
                PlayerName = playerName,
                PlayerNumber = playerNumber,
                IsBot = isBot,
                Score = 0,
            });
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

        /// <summary>Server-only: name shown for a player on the scoreboard, or null if unknown.</summary>
        public string GetPlayerName(ulong clientId)
        {
            for (int i = 0; i < Scores.Count; i++)
                if (Scores[i].ClientId == clientId) return Scores[i].PlayerName;
            return null;
        }

        /// <summary>Server-only: apply a score delta (positive or negative) to a player.</summary>
        public void ApplyScoreDelta(ulong clientId, int delta)
        {
            AwardKill(clientId, delta, 0, 0, false);
        }

        /// <summary>
        /// Server-only: credit a kill. Bumps the score plus whichever per-category counter applies,
        /// in a single SyncList write so clients never see a half-updated row.
        /// </summary>
        public void AwardKill(ulong clientId, int points, int playerKills, int npcKills, bool killedBoss)
        {
            for (int i = 0; i < Scores.Count; i++)
            {
                if (Scores[i].ClientId == clientId)
                {
                    var entry = Scores[i];
                    entry.Score += points;
                    entry.PlayerKills += playerKills;
                    entry.NpcKills += npcKills;
                    entry.KilledBoss |= killedBoss;
                    Scores[i] = entry;
                    return;
                }
            }
        }
    }
}
