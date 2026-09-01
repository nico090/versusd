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
        /// <summary>Match length in seconds (5 minutes).</summary>
        public const float MatchDuration = 300f;

        /// <summary>
        /// Seconds remaining at which the match flips to <see cref="MatchPhase.DoubleKills"/>
        /// (the last 2 minutes).
        /// </summary>
        public const float DoubleKillsThreshold = 120f;

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
        /// Seconds left on the clock when the boss appears. It shows up for the final stretch only
        /// (same moment as <see cref="MatchPhase.DoubleKills"/>), so the last two minutes are the
        /// scramble: double-value player kills *and* the boss on the board at the same time.
        /// </summary>
        public const float BossSpawnTimeRemaining = 120f;

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
