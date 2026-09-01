using Unity.BossRoom.Gameplay.Actions;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.Configuration
{
    /// <summary>
    /// The PvP balance pass for the hero classes (section 7 of the deathmatch design doc).
    /// </summary>
    /// <remarks>
    /// <para>The design doc says to tune these in the CharacterClass / Action .asset files. In this
    /// project that doesn't stick: the GameData assets are stored in Git LFS and Unity serves its
    /// own cached copy of an open project, so a value edited on disk can be silently reverted when
    /// the dedicated-server build is made. Same reasoning as <see cref="NpcBalance"/> and
    /// ServerCharacterMovement's per-class speed multiplier — the numbers live in code so they
    /// actually reach the build.</para>
    ///
    /// <para>Design principle behind the specific nerfs: in PvE, balance is "damage per second
    /// against bags of HP". In PvP what breaks the game is <b>control</b> (stuns, slows, immunity)
    /// and <b>burst</b> (killing before the target can react). So the nerfs land there rather than
    /// on flat damage.</para>
    ///
    /// <para>When iterating: change ONE number at a time, by 10-15%, and play 3-4 matches before
    /// the next change. Target metric — in an even group no class should sustain more than ~35% of
    /// wins, and none less than ~15%.</para>
    /// </remarks>
    public static class HeroBalance
    {
        // ---------------------------------------------------------------- health

        // Post-pass HP pools. Shipped values were Tank 220 / Rogue 180 / Archer 160 / Mage 140.
        const int k_TankHitPoints = 160;   // -27%: still the fattest, but killable inside one fight
        const int k_ArcherHitPoints = 135; // the squishiest in the game: a glass cannon, as intended
        // -17%. The Tank pass left the Rogue on 180 against the Tank's 160, so the assassin was
        // also the tankiest class in the game *and* held the only damage buff in this file. That
        // inversion is most of why it felt unbeatable; the class is supposed to buy its burst with
        // fragility. Now: Tank 160 > Rogue 150 > Mage 140 > Archer 135.
        const int k_RogueHitPoints = 150;
        const int k_MageHitPoints = 140;   // unchanged

        /// <summary>
        /// PvP-adjusted max HP for a hero class. Non-hero types fall through to their asset value
        /// (monsters go through <see cref="NpcBalance.GetMaxHitPoints"/> instead).
        /// </summary>
        public static int GetMaxHitPoints(CharacterTypeEnum characterType, int baseHitPoints)
        {
            switch (characterType)
            {
                case CharacterTypeEnum.Tank:
                    return k_TankHitPoints;
                case CharacterTypeEnum.Archer:
                    return k_ArcherHitPoints;
                case CharacterTypeEnum.Rogue:
                    return k_RogueHitPoints;
                case CharacterTypeEnum.Mage:
                    return k_MageHitPoints;
                default:
                    return baseHitPoints;
            }
        }

        // ---------------------------------------------------------------- damage

        const float k_ArcherChargedShotMultiplier = 0.78f; // -22%: a reward for aiming, not a one-shot
        const float k_MageBoltMultiplier = 0.85f;          // -15% damage, radius untouched — the area IS the role

        // Went 1.10 -> 0.92 -> 1.20 across playtests, and the middle value was the wrong read.
        // The Rogue's problem was never this ability: it was 180 HP against the Tank's 160 and a
        // permanent movement-speed lead, both since corrected. With those gone the dash was left
        // paying for a durability the class no longer has.
        //
        // Above 1.0 now on purpose. The dash is a committed, telegraphed approach on a cooldown
        // that ends with the Rogue standing in the open next to whoever they hit — the most
        // punishable thing the class can do. It should be worth doing.
        const float k_RogueDashMultiplier = 1.20f;

        // Class-wide, not per-ability. The Tank started with no damage nerf at all (only a shield
        // uptime cap), then got Trample and its basic trimmed separately — and it still killed too
        // fast, which says the problem was never one outlier ability but the class's whole damage
        // budget sitting too high for something built to absorb hits rather than end fights. One
        // number covers every attack it has, including the ones added later, instead of leaving a
        // list that has to be remembered every time the Tank gains a power.
        const float k_TankDamageMultiplier = 0.80f;

        // ── Basic attacks ─────────────────────────────────────────────────────────────────────
        // The basic used to be left alone on the grounds that constant chip damage is
        // self-limiting. That holds for the ranged classes, which pay for every hit with an aim; it
        // does not hold for a melee class, whose basic lands whenever it is simply standing next to
        // you. The Tank's basic is covered by its class-wide multiplier above.
        const float k_RogueBasicMultiplier = 0.90f;

        /// <summary>
        /// Multiplier on the damage of one hero ability, keyed by who is casting it and which
        /// ActionLogic it runs. Anything not listed is unchanged (1.0) — which now includes the
        /// two ranged basic attacks, but no longer the melee ones.
        /// </summary>
        public static float GetDamageMultiplier(CharacterTypeEnum characterType, ActionLogic logic)
        {
            switch (characterType)
            {
                case CharacterTypeEnum.Archer when logic == ActionLogic.ChargedLaunchProjectile:
                    return k_ArcherChargedShotMultiplier;
                case CharacterTypeEnum.Mage when logic == ActionLogic.RangedFXTargeted:
                    return k_MageBoltMultiplier;
                case CharacterTypeEnum.Rogue when logic == ActionLogic.DashAttack:
                    return k_RogueDashMultiplier;
                // Every Tank attack, present and future. Its shield buff and Frost Nova deal no
                // damage, so this only ever reaches things meant to hurt.
                case CharacterTypeEnum.Tank:
                    return k_TankDamageMultiplier;
                // The Mage's self-heal also runs Melee logic, but ScaleDamage passes non-positive
                // values straight through, so healing never reaches this table.
                case CharacterTypeEnum.Rogue when logic == ActionLogic.Melee:
                    return k_RogueBasicMultiplier;
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// Applies <see cref="GetDamageMultiplier"/> to a raw damage number. Non-positive values
        /// (healing, the debug kill cheat) are passed through untouched.
        /// </summary>
        public static int ScaleDamage(ServerCharacter caster, ActionLogic logic, int rawDamage)
        {
            if (rawDamage <= 0 || caster == null || caster.IsNpc)
            {
                return rawDamage;
            }

            float multiplier = GetDamageMultiplier(caster.CharacterType, logic);
            return multiplier == 1f ? rawDamage : Mathf.Max(1, Mathf.RoundToInt(rawDamage * multiplier));
        }

        // ------------------------------------------------------- control & uptime

        /// <summary>
        /// Minimum cooldown on the Tank's shield buff. It ships at 1.2s against a 5s effect, i.e.
        /// permanent damage reduction — the single reason the Tank was unbeatable 1v1. This is the
        /// Tank's "control" nerf: the class has no stun, its damage mitigation is the thing that
        /// had to be put on a real timer.
        /// </summary>
        const float k_TankShieldReuseSeconds = 12f;

        /// <summary>
        /// Minimum cooldown on Stealth. It ships at 0, so the Rogue could drop out of a fight and
        /// re-enter it seconds later, forever. A full cooldown means one stealth per fight.
        /// </summary>
        const float k_StealthReuseSeconds = 12f;

        /// <summary>
        /// Hard cap on how long Stealth can last. It ships with DurationSeconds 0, which the action
        /// system reads as "indefinite" — the Rogue stayed invisible until they chose to attack.
        /// </summary>
        const float k_StealthMaxDurationSeconds = 8f;

        /// <summary>
        /// The cooldown actually enforced for an ability: whatever the asset says, but never below
        /// the code-side floor for the abilities that need one.
        /// </summary>
        public static float GetReuseTime(CharacterTypeEnum characterType, ActionLogic logic, float configuredReuseTime)
        {
            switch (logic)
            {
                case ActionLogic.StealthMode:
                    return Mathf.Max(configuredReuseTime, k_StealthReuseSeconds);
                case ActionLogic.ChargedShield when characterType == CharacterTypeEnum.Tank:
                    return Mathf.Max(configuredReuseTime, k_TankShieldReuseSeconds);
                case ActionLogic.SpinAttack:
                    return Mathf.Max(configuredReuseTime, k_SpinAttackReuseSeconds);
                case ActionLogic.MeteorStrike:
                    return Mathf.Max(configuredReuseTime, k_MeteorStrikeReuseSeconds);
                case ActionLogic.FrostNova:
                    return Mathf.Max(configuredReuseTime, k_FrostNovaReuseSeconds);
                default:
                    return configuredReuseTime;
            }
        }

        // The three added powers get code-side cooldown floors for the same reason everything else
        // in this file does: their .asset values can be silently reverted by Unity's cache when the
        // dedicated-server build is made, and a control or burst power that comes back instantly is
        // the worst possible failure mode for that to have.

        /// <summary>Twisting Slash. Multi-tick area damage the Rogue can walk around with, so it
        /// has to be a commitment rather than something to spam between basic attacks.</summary>
        const float k_SpinAttackReuseSeconds = 9f;

        /// <summary>Meteor. Telegraphed, avoidable, and hits a crowd — long enough that landing one
        /// on somebody is an event.</summary>
        const float k_MeteorStrikeReuseSeconds = 14f;

        /// <summary>Frost Nova. This is the only hard control in the game; the cooldown is what
        /// stops the Tank from chain-freezing a target out of the match entirely.</summary>
        const float k_FrostNovaReuseSeconds = 16f;

        /// <summary>Long enough for the 1.5s fall plus a tail for the burst to start on time.</summary>
        const float k_MeteorDurationSeconds = 1.9f;

        /// <summary>
        /// The duration actually enforced for an ability. Returns 0 for "indefinite", matching the
        /// ActionConfig sentinel.
        /// </summary>
        public static float GetDuration(ActionLogic logic, float configuredDuration)
        {
            // The meteor's fall was given a code-side floor (MeteorStrikeAction), and the action
            // has to outlive it or the whole thing is torn down in mid-air — the rock vanishes and
            // the impact never plays. Floored here rather than raised in the asset for the same
            // reason the fall time is: the two numbers have to agree, and one of them could not
            // live in the asset safely.
            if (logic == ActionLogic.MeteorStrike)
            {
                return Mathf.Max(configuredDuration, k_MeteorDurationSeconds);
            }

            if (logic != ActionLogic.StealthMode)
            {
                return configuredDuration;
            }

            // configuredDuration <= 0 is the "runs forever" sentinel, which is exactly what we're
            // capping; otherwise take whichever is shorter.
            return configuredDuration <= 0f
                ? k_StealthMaxDurationSeconds
                : Mathf.Min(configuredDuration, k_StealthMaxDurationSeconds);
        }

        /// <summary>
        /// True if this ability's cooldown must survive the action being cancelled.
        /// </summary>
        /// <remarks>
        /// The action player normally clears the cooldown timestamp when an action is cancelled, so
        /// a move interrupted by the player isn't punished. Stealth ends by being cancelled — that
        /// is what attacking out of it does — so under the normal rule its cooldown was wiped every
        /// single time and the floor above would never have applied.
        /// </remarks>
        public static bool KeepsCooldownOnCancel(ActionLogic logic)
        {
            return logic == ActionLogic.StealthMode;
        }
    }
}
