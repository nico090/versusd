namespace Unity.BossRoom.Gameplay.Configuration
{
    /// <summary>
    /// The phases a deathmatch match moves through. Server-authoritative, synced to clients so
    /// the HUD can show the "x2" multiplier and the end-of-match state.
    /// </summary>
    public enum MatchPhase
    {
        /// <summary>Before the countdown has started (players still loading in).</summary>
        PreGame,

        /// <summary>
        /// Everyone is in and can move, but the match clock has not started and nothing scores.
        /// This is the window the on-screen control tutorial runs in.
        /// </summary>
        Warmup,

        /// <summary>Normal scoring.</summary>
        Normal,

        /// <summary>Final stretch: player kills are worth double.</summary>
        DoubleKills,

        /// <summary>Time is up. Input is frozen and the final table is shown.</summary>
        Ended,
    }

    /// <summary>
    /// Every tunable number of the PvPvE deathmatch mode, in one place.
    /// </summary>
    /// <remarks>
    /// These live in code rather than in a ScriptableObject for the same reason as
    /// <see cref="NpcBalance"/>: the GameData assets are stored in Git LFS and an edit made on
    /// disk while the Editor is open is silently replaced by the Editor's cached copy when the
    /// build is made, so asset-only tuning does not reliably reach the dedicated server build.
    /// </remarks>
    public static class DeathmatchRules
    {
        /// <summary>Match length in seconds (3 minutes).</summary>
        public const float MatchDuration = 180f;

        /// <summary>
        /// Seconds of <see cref="MatchPhase.Warmup"/> before the clock starts.
        /// </summary>
        /// <remarks>
        /// Long enough to walk the player through the controls one at a time (see
        /// <c>WarmupTutorial</c>) and short enough that someone who already knows them is not kept
        /// waiting. Players move and cast freely during it, but they cannot be hurt and nothing
        /// scores, so the practice can't cost or win anything.
        /// </remarks>
        public const float WarmupDuration = 30f;

        /// <summary>
        /// Seconds remaining at which the match flips to <see cref="MatchPhase.DoubleKills"/>
        /// (the last 45 seconds).
        /// </summary>
        /// <remarks>
        /// A quarter of the match rather than the old 40% of it, and deliberately <i>later</i> than
        /// <see cref="BossSpawnTimeRemaining"/> rather than simultaneous with it. The two used to
        /// fire together, which made the endgame one undifferentiated scramble; staggering them
        /// gives the boss a window of its own before the hunt opens, so committing to it becomes a
        /// decision taken in advance and paid for afterwards rather than a coin flip.
        /// </remarks>
        public const float DoubleKillsThreshold = 45f;

        /// <summary>Points for killing an imp or any other minor NPC.</summary>
        public const int PointsPerNpcKill = 1;

        /// <summary>Points for killing another player during <see cref="MatchPhase.Normal"/>.</summary>
        public const int PointsPerPlayerKill = 5;

        /// <summary>Multiplier applied to player kills during <see cref="MatchPhase.DoubleKills"/>.</summary>
        public const int DoubleKillsMultiplier = 2;

        /// <summary>
        /// Points for landing the final blow on the boss.
        /// </summary>
        /// <remarks>
        /// <para>Cut from 20 on playtest feedback. At four player kills for one last hit, the boss
        /// was not a rich target among several — it was the only thing on the board worth doing,
        /// and the last two minutes collapsed into everyone standing in the same place waiting to
        /// steal it.</para>
        ///
        /// <para>At 12 it is still the single biggest prize (a player kill is 5, or 10 while it is
        /// up) and stealing the last hit is still a real play, but committing to it costs about two
        /// double-value kills instead of four — so ignoring the boss and hunting people stays a
        /// defensible way to win.</para>
        ///
        /// <para>Note this moved alongside the boss's HP doubling to 450: the reward went down
        /// while the effort went up, which is deliberate. The boss should be a fight the room
        /// commits to together, not a coin the fastest player picks up.</para>
        /// </remarks>
        public const int PointsPerBossKill = 12;

        /// <summary>Seconds a dead player waits before respawning.</summary>
        public const float RespawnDelay = 5f;

        /// <summary>
        /// Seconds of damage immunity granted on respawn. Cancelled early the moment the player
        /// uses an offensive action, so it can't be used to open a fight for free.
        /// </summary>
        public const float RespawnInvulnerability = 2f;

        /// <summary>Seconds between the match ending and the switch to the PostGame scene.</summary>
        public const float PostMatchDelay = 7f;

        /// <summary>
        /// Seconds left on the clock when the boss appears — the final minute, and a little before
        /// <see cref="DoubleKillsThreshold"/> opens the double-value hunt.
        /// </summary>
        /// <remarks>
        /// <para>The gap between the two is the whole mechanic. The boss lands on an otherwise
        /// normal board, so whoever wants it has to break off and start on it while player kills
        /// are still worth their ordinary five; then the doubling arrives and everyone standing
        /// around the boss is suddenly the most valuable target on the map.</para>
        ///
        /// <para>When the two fired together this was a coin flip nobody could plan, and the
        /// shorter match made it worse: with 45 seconds for both, ignoring the boss was simply
        /// correct. Moving it earlier gives it back a window without lengthening the endgame.</para>
        /// </remarks>
        public const float BossSpawnTimeRemaining = 60f;

        /// <summary>
        /// Name of an optional empty Transform placed in the middle of the map to mark where the
        /// boss spawns. If no object with this name exists, the boss spawns at the centroid of the
        /// player spawn points instead.
        /// </summary>
        public const string BossSpawnPointName = "BossSpawnPoint";

        /// <summary>
        /// Seconds between imp respawn waves. The "1 point" source must never dry up completely —
        /// it's what gives the player who keeps losing straight fights something to do.
        /// </summary>
        public const float ImpRespawnPeriod = 35f;
    }
}
