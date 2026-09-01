using System;
using System.Collections.Generic;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Unity.BossRoom.Gameplay.Bots
{
    /// <summary>
    /// The five bot personalities. A personality decides <i>how</i> a bot plays — which class it
    /// reaches for, the distance it wants to fight at, how readily it disengages. It deliberately
    /// says nothing about how <i>well</i> it plays: that is <see cref="BotDifficulty"/>, a separate
    /// axis, so "a sniper" and "a good player" are independent knobs.
    /// </summary>
    public enum BotPersonality
    {
        /// <summary>Closes to melee and stays there. Rarely backs off.</summary>
        Brawler,

        /// <summary>Fights at the edge of its range and kites anything that gets close.</summary>
        Sniper,

        /// <summary>Hunts whoever is already hurt, and leaves the moment a fight turns even.</summary>
        Opportunist,

        /// <summary>Holds the middle of the map and out-lasts people rather than chasing them.</summary>
        Guardian,

        /// <summary>Erratic. Wanders, over-commits, fires early. Reads as a new player.</summary>
        Wildcard,
    }

    /// <summary>
    /// The tunables of one bot personality. All the timing/accuracy values here are <i>base</i>
    /// values at neutral difficulty; read them through the <c>Effective*</c> properties, which fold
    /// in <see cref="BotDifficulty"/>.
    /// </summary>
    public class BotProfile
    {
        public BotPersonality Personality;

        /// <summary>Classes this bot reaches for in CharSelect, best first. It falls back to any
        /// free seat if all of these are taken — same as a player who finds their main is gone.</summary>
        public CharacterTypeEnum[] PreferredClasses = Array.Empty<CharacterTypeEnum>();

        /// <summary>Fraction of its attack range the bot tries to hold. 0.4 hugs, 0.95 snipes.</summary>
        public float PreferredRangeFactor = 0.7f;

        /// <summary>0..1. How much it pushes towards a foe rather than repositioning around one.</summary>
        public float Aggression = 0.5f;

        /// <summary>0..1. How much lateral (circling) movement it mixes into its approach.</summary>
        public float StrafeAmount = 0.4f;

        /// <summary>Seconds before it reacts to a foe it has just noticed.</summary>
        public float BaseReactionSeconds = 0.35f;

        /// <summary>Degrees of aim error. Applied as a slow wobble, not per-shot jitter, so misses
        /// come in believable streaks instead of looking like white noise.</summary>
        public float BaseAimErrorDegrees = 8f;

        /// <summary>Seconds between attempts to use a secondary/special skill.</summary>
        public float BaseSkillIntervalSeconds = 4f;

        /// <summary>HP fraction below which it starts backing away from fights.</summary>
        public float RetreatHealthFraction = 0.3f;

        /// <summary>
        /// Seconds it may spend backing away from a foe that has closed inside its preferred range
        /// before it has to plant its feet.
        /// </summary>
        /// <remarks>
        /// Kiting is by far the strongest thing a bot does, because a retreating bot moves at
        /// exactly the same speed as the player chasing it — <c>SetMovementDirection</c> normalises
        /// the direction, so there is no such thing as backing off slowly. Left unbudgeted, the bot
        /// walks backwards for the whole of its cooldown, attacks the moment it is up, and walks
        /// backwards again: unhittable, and not a thing a human can do. The budget is what forces
        /// it to take the trade.
        /// </remarks>
        public float RetreatBudgetSeconds = 1f;

        /// <summary>Seconds it must stand and fight once the retreat budget runs out.</summary>
        public float HoldGroundSeconds = 1.6f;

        /// <summary>
        /// Seconds after committing to an attack during which it will not back away. A player who
        /// swings follows through; a bot that dances backwards on the same frame it attacks is the
        /// thing that reads as cheating.
        /// </summary>
        public float FollowThroughSeconds = 0.7f;

        /// <summary>
        /// Seconds of unbroken pressure on one foe before the bot takes a breather.
        /// </summary>
        /// <remarks>
        /// A bot has no reason to ever stop coming at you, and three of them that never stop is
        /// not a hard fight, it is an exhausting one — there is no moment in it where a player
        /// gets to do anything except react. Real players break off: they reposition, they look
        /// around, they wait for a cooldown. This clock is what gives the fight that rhythm, and
        /// it is also what stops a pack from pinning one person indefinitely.
        /// </remarks>
        public float PressureSeconds = 4f;

        /// <summary>
        /// Seconds the breather lasts. The bot keeps circling and stays in the fight, but it does
        /// not attack and gives a little ground — the window a player uses to heal, to reposition
        /// or to pick off somebody else.
        /// </summary>
        public float BreatherSeconds = 1.8f;

        /// <summary>0..1. How strongly it prefers an already-wounded target.</summary>
        public float WoundedTargetBias = 0.35f;

        /// <summary>0..1. Willingness to spend time killing imps instead of players.</summary>
        public float NpcInterest = 0.4f;

        /// <summary>0..1 of full charge before it releases a charged shot. 1 always holds to max.</summary>
        public float ChargeHoldFraction = 1f;

        /// <summary>Seconds it "looks at" the roster in CharSelect before picking a seat.</summary>
        public float SeatDecisionSeconds = 2.5f;

        /// <summary>Seconds between picking a seat and hitting Ready.</summary>
        public float SeatLockInSeconds = 3f;

        /// <summary>Chance it changes its mind once before locking in.</summary>
        public float SeatIndecisionChance = 0.25f;

        /// <summary>Names this personality draws from, so the same personality doesn't always
        /// appear under the same name.</summary>
        public string[] NamePool = Array.Empty<string>();

        /// <summary>The name this particular bot plays under, dealt from <see cref="NamePool"/> by
        /// <see cref="BotProfileLibrary.CreateRoster"/>. Shown in the lobby, kill feed and final
        /// table exactly like a player's.</summary>
        public string AssignedName;

        // ── Difficulty-adjusted values ────────────────────────────────────────────────────────
        // Every read of a skill-dependent number goes through here, so difficulty is applied in
        // exactly one place and can never be half-applied.

        public float EffectiveReactionSeconds => BotDifficulty.ScaleReaction(BaseReactionSeconds);

        public float EffectiveAimErrorDegrees => BotDifficulty.ScaleAimError(BaseAimErrorDegrees);

        public float EffectiveSkillIntervalSeconds => BotDifficulty.ScaleSkillInterval(BaseSkillIntervalSeconds);

        public float EffectiveRetreatBudgetSeconds => BotDifficulty.ScaleRetreatBudget(RetreatBudgetSeconds);

        public float EffectiveHoldGroundSeconds => BotDifficulty.ScaleHoldGround(HoldGroundSeconds);

        public float EffectiveFollowThroughSeconds => BotDifficulty.ScaleFollowThrough(FollowThroughSeconds);

        public float EffectivePressureSeconds => BotDifficulty.ScalePressure(PressureSeconds);

        public float EffectiveBreatherSeconds => BotDifficulty.ScaleBreather(BreatherSeconds);

        public BotProfile Clone() => (BotProfile)MemberwiseClone();
    }

    /// <summary>
    /// The global "how good are the bots" dial, deliberately separate from personality. 0 is a
    /// bot that flails, 1 is one that rarely misses and reacts almost instantly.
    /// </summary>
    /// <remarks>
    /// Lives in code (with an env-var override) rather than in a ScriptableObject for the same
    /// reason as <see cref="Unity.BossRoom.Gameplay.Configuration.DeathmatchRules"/>: an asset
    /// edited on disk can be silently replaced by the Editor's cached copy when the dedicated
    /// server build is made, so asset-only tuning does not reliably reach the server.
    /// </remarks>
    public static class BotDifficulty
    {
        /// <summary>
        /// Neutral default: clearly beatable, but it will punish standing still.
        /// </summary>
        /// <remarks>
        /// <para>Walked down twice on playtest feedback: 0.55 → 0.35 → 0.22. A bot never gets
        /// tired, never misclicks and never loses track of a target, so "even" on paper still
        /// plays as an uphill fight; the dial has to sit well below the middle for the match to
        /// feel winnable.</para>
        ///
        /// <para>There is a second reason for this drop. The bots aim by pointing straight at the
        /// target and adding <c>BaseAimErrorDegrees</c> of wobble — they never used the player's
        /// aim assist. So tightening that assist (a narrower cone and a smaller cursor snap, for
        /// precision) takes something away from the player side only, and the bot level has to
        /// come down to match or the net effect of that change is bots getting relatively
        /// stronger.</para>
        /// </remarks>
        public const float DefaultLevel = 0.22f;

        static float s_Level = float.NaN;

        /// <summary>0..1. Read once from the <c>BOTS_DIFFICULTY</c> env var, then cached.</summary>
        public static float Level
        {
            get
            {
                if (float.IsNaN(s_Level))
                {
                    s_Level = ReadLevelFromEnvironment();
                }

                return s_Level;
            }
            set => s_Level = Mathf.Clamp01(value);
        }

        static float ReadLevelFromEnvironment()
        {
            var raw = Environment.GetEnvironmentVariable("BOTS_DIFFICULTY");
            if (!string.IsNullOrEmpty(raw) &&
                float.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                return Mathf.Clamp01(parsed);
            }

            return DefaultLevel;
        }

        /// <summary>A worse bot dithers before reacting; a better one reacts almost at once.</summary>
        public static float ScaleReaction(float baseSeconds) =>
            baseSeconds * Mathf.Lerp(2f, 0.4f, Level);

        /// <summary>A worse bot's aim wanders much further off the target.</summary>
        public static float ScaleAimError(float baseDegrees) =>
            baseDegrees * Mathf.Lerp(2.2f, 0.3f, Level);

        /// <summary>A better bot gets its cooldowns out more promptly.</summary>
        public static float ScaleSkillInterval(float baseSeconds) =>
            baseSeconds * Mathf.Lerp(1.8f, 0.55f, Level);

        /// <summary>A worse bot barely kites at all; only a good one buys itself space.</summary>
        public static float ScaleRetreatBudget(float baseSeconds) =>
            baseSeconds * Mathf.Lerp(0.3f, 1.15f, Level);

        /// <summary>...and once it is out of space, a worse bot is stuck fighting for longer.</summary>
        public static float ScaleHoldGround(float baseSeconds) =>
            baseSeconds * Mathf.Lerp(1.9f, 0.8f, Level);

        /// <summary>A worse bot over-commits to its own swing for longer after throwing it.</summary>
        public static float ScaleFollowThrough(float baseSeconds) =>
            baseSeconds * Mathf.Lerp(1.6f, 0.8f, Level);

        /// <summary>A better bot can keep the pressure on for longer before it needs a breather.</summary>
        public static float ScalePressure(float baseSeconds) =>
            baseSeconds * Mathf.Lerp(0.6f, 1.6f, Level);

        /// <summary>...and needs a shorter one when it does.</summary>
        public static float ScaleBreather(float baseSeconds) =>
            baseSeconds * Mathf.Lerp(1.5f, 0.6f, Level);

        /// <summary>
        /// The pause between a basic attack coming off cooldown and the bot actually pressing the
        /// button. Firing on the exact frame the cooldown ends, every time, is the single most
        /// machine-like thing a bot does and the reason it wins straight trades it should lose.
        /// </summary>
        /// <remarks>
        /// Roughly doubled after playtesting. A hero's basic attack has little or no cooldown of
        /// its own, so this pause <i>is</i> the bot's rate of fire: at the old values a bot swung
        /// again the moment its animation ended, and a couple of them together produced a stream
        /// of hits with no gap in it to answer. The gap is the point — it is where a player gets
        /// to move, aim and swing back.
        /// </remarks>
        public static float BasicAttackHesitationSeconds() =>
            Random.Range(0.25f, 0.7f) + Mathf.Lerp(0.9f, 0.15f, Level);
    }

    /// <summary>
    /// The five stock personalities and the names they play under.
    /// </summary>
    public static class BotProfileLibrary
    {
        /// <summary>
        /// Fresh copies of the five personalities, in a fixed order. Callers get clones: a bot
        /// tweaks its own profile at runtime (it picks a name out of the pool), and a shared
        /// instance would leak those tweaks into every future match in the same process.
        /// </summary>
        public static List<BotProfile> CreateAll()
        {
            var all = new List<BotProfile>
            {
                new BotProfile
                {
                    Personality = BotPersonality.Brawler,
                    PreferredClasses = new[] { CharacterTypeEnum.Rogue, CharacterTypeEnum.Tank },
                    PreferredRangeFactor = 0.45f,
                    Aggression = 0.95f,
                    StrafeAmount = 0.25f,
                    BaseReactionSeconds = 0.28f,
                    BaseAimErrorDegrees = 7f,
                    BaseSkillIntervalSeconds = 3f,
                    RetreatHealthFraction = 0.15f,
                    RetreatBudgetSeconds = 0.4f,
                    HoldGroundSeconds = 2.5f,
                    FollowThroughSeconds = 1.2f,
                    // Presses the longest and rests the least — that is what makes it the Brawler.
                    PressureSeconds = 5.5f,
                    BreatherSeconds = 1.2f,
                    WoundedTargetBias = 0.2f,
                    NpcInterest = 0.3f,
                    ChargeHoldFraction = 0.6f,
                    SeatDecisionSeconds = 1.6f,
                    SeatLockInSeconds = 1.8f,
                    SeatIndecisionChance = 0.1f,
                    NamePool = new[] { "Tomi", "Rulo", "Bruno", "Kaji", "Nacho" },
                },
                new BotProfile
                {
                    Personality = BotPersonality.Sniper,
                    PreferredClasses = new[] { CharacterTypeEnum.Archer, CharacterTypeEnum.Mage },
                    PreferredRangeFactor = 0.95f,
                    Aggression = 0.25f,
                    StrafeAmount = 0.55f,
                    BaseReactionSeconds = 0.4f,
                    BaseAimErrorDegrees = 5f,
                    BaseSkillIntervalSeconds = 3.5f,
                    RetreatHealthFraction = 0.45f,
                    RetreatBudgetSeconds = 1.5f,
                    HoldGroundSeconds = 1.4f,
                    FollowThroughSeconds = 0.5f,
                    // Fires in bursts and then resets the distance, like someone playing a bow.
                    PressureSeconds = 3.2f,
                    BreatherSeconds = 2.4f,
                    WoundedTargetBias = 0.3f,
                    NpcInterest = 0.25f,
                    ChargeHoldFraction = 1f,
                    SeatDecisionSeconds = 3.2f,
                    SeatLockInSeconds = 3.5f,
                    SeatIndecisionChance = 0.2f,
                    NamePool = new[] { "Lira", "Sombra", "Vex", "Miru", "Calder" },
                },
                new BotProfile
                {
                    Personality = BotPersonality.Opportunist,
                    PreferredClasses = new[] { CharacterTypeEnum.Rogue, CharacterTypeEnum.Archer },
                    PreferredRangeFactor = 0.6f,
                    Aggression = 0.7f,
                    StrafeAmount = 0.6f,
                    BaseReactionSeconds = 0.3f,
                    BaseAimErrorDegrees = 6.5f,
                    BaseSkillIntervalSeconds = 3.2f,
                    RetreatHealthFraction = 0.5f,
                    RetreatBudgetSeconds = 1.1f,
                    HoldGroundSeconds = 1.5f,
                    FollowThroughSeconds = 0.6f,
                    // In and out: it commits briefly, then goes looking for a better fight.
                    PressureSeconds = 2.8f,
                    BreatherSeconds = 2.2f,
                    WoundedTargetBias = 0.9f,
                    NpcInterest = 0.55f,
                    ChargeHoldFraction = 0.75f,
                    SeatDecisionSeconds = 2.4f,
                    SeatLockInSeconds = 2.6f,
                    SeatIndecisionChance = 0.45f,
                    NamePool = new[] { "Zorro", "Pipa", "Kess", "Duna", "Milo" },
                },
                new BotProfile
                {
                    Personality = BotPersonality.Guardian,
                    PreferredClasses = new[] { CharacterTypeEnum.Tank, CharacterTypeEnum.Mage },
                    PreferredRangeFactor = 0.7f,
                    Aggression = 0.45f,
                    StrafeAmount = 0.3f,
                    BaseReactionSeconds = 0.45f,
                    BaseAimErrorDegrees = 8f,
                    BaseSkillIntervalSeconds = 4.5f,
                    RetreatHealthFraction = 0.2f,
                    RetreatBudgetSeconds = 0.7f,
                    HoldGroundSeconds = 2.2f,
                    FollowThroughSeconds = 0.9f,
                    // Steady rather than relentless: long stints, long rests.
                    PressureSeconds = 4.5f,
                    BreatherSeconds = 2f,
                    WoundedTargetBias = 0.25f,
                    NpcInterest = 0.7f,
                    ChargeHoldFraction = 1f,
                    SeatDecisionSeconds = 2.8f,
                    SeatLockInSeconds = 3.2f,
                    SeatIndecisionChance = 0.15f,
                    NamePool = new[] { "Muro", "Otto", "Bastian", "Ren", "Goliat" },
                },
                new BotProfile
                {
                    Personality = BotPersonality.Wildcard,
                    PreferredClasses = new[]
                    {
                        CharacterTypeEnum.Mage, CharacterTypeEnum.Tank,
                        CharacterTypeEnum.Archer, CharacterTypeEnum.Rogue,
                    },
                    PreferredRangeFactor = 0.65f,
                    Aggression = 0.6f,
                    StrafeAmount = 0.85f,
                    BaseReactionSeconds = 0.7f,
                    BaseAimErrorDegrees = 16f,
                    BaseSkillIntervalSeconds = 2.5f,
                    RetreatHealthFraction = 0.35f,
                    RetreatBudgetSeconds = 0.8f,
                    HoldGroundSeconds = 1.2f,
                    FollowThroughSeconds = 0.4f,
                    // Attention span of a new player: short bursts, long distractions.
                    PressureSeconds = 2.2f,
                    BreatherSeconds = 3f,
                    WoundedTargetBias = 0.1f,
                    NpcInterest = 0.8f,
                    ChargeHoldFraction = 0.35f,
                    SeatDecisionSeconds = 4.5f,
                    SeatLockInSeconds = 4.5f,
                    SeatIndecisionChance = 0.7f,
                    NamePool = new[] { "Fede", "Luchi", "Pepo", "Yuki", "Tato" },
                },
            };

            return all;
        }

        /// <summary>
        /// A roster of <paramref name="count"/> distinct-feeling bots: the five personalities are
        /// shuffled and dealt out, so a two-bot match isn't always the same two, and asking for
        /// more than five simply starts a second pass rather than failing.
        /// </summary>
        /// <param name="count">How many bots are wanted.</param>
        /// <param name="excludedNames">Names already in use in this match (real players included),
        /// so a bot never appears under a name the lobby already shows.</param>
        public static List<BotProfile> CreateRoster(int count, ICollection<string> excludedNames)
        {
            var pool = CreateAll();
            Shuffle(pool);

            var roster = new List<BotProfile>(Mathf.Max(0, count));
            var used = new HashSet<string>(excludedNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; i++)
            {
                var profile = pool[i % pool.Count].Clone();
                profile.AssignedName = PickName(profile.NamePool, used);
                roster.Add(profile);
            }

            return roster;
        }

        /// <summary>Deals an unused name out of <paramref name="pool"/>, marking it used.</summary>
        static string PickName(string[] pool, HashSet<string> used)
        {
            if (pool != null)
            {
                // Start at a random offset so the same personality doesn't always lead with the
                // same name, then walk the pool so a taken name falls through to the next.
                int offset = Random.Range(0, Mathf.Max(1, pool.Length));
                for (int i = 0; i < pool.Length; i++)
                {
                    var candidate = pool[(offset + i) % pool.Length];
                    if (used.Add(candidate))
                    {
                        return candidate;
                    }
                }
            }

            // Every name in this personality's pool is taken. Fall back to a numbered variant of
            // the first one rather than duplicating a name that's already on the scoreboard.
            string basis = pool != null && pool.Length > 0 ? pool[0] : "Jugador";
            for (int n = 2; n < 100; n++)
            {
                var candidate = basis + n;
                if (used.Add(candidate))
                {
                    return candidate;
                }
            }

            return basis;
        }

        static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
