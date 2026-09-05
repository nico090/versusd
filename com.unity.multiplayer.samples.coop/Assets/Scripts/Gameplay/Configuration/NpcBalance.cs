using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.Configuration
{
    /// <summary>
    /// Central tuning for how strong the monsters (NPCs) are.
    /// </summary>
    /// <remarks>
    /// These numbers live in code instead of in the CharacterClass / Action .asset files for the
    /// same reason as ServerCharacterMovement's per-class speed multiplier: a value edited on disk
    /// can be silently replaced by Unity's cached copy when the build is made, so asset-only
    /// balance changes don't reliably reach the device.
    ///
    /// Reference points for the values below: heroes have 140-220 HP and hit for 25-48 per swing,
    /// so the shipped monster stats (8-40 HP, 4-8 damage) meant every imp died to a single hit and
    /// needed dozens of hits to threaten anyone.
    /// </remarks>
    public static class NpcBalance
    {
        /// <summary>
        /// Movement speed (m/s) forced on every NPC, overriding CharacterClass.Speed. Monsters are
        /// meant to be tanky and threatening but slow enough that a hero can always kite them
        /// (heroes move at 4-7 m/s). Forced-movement actions such as the boss's Trample set their
        /// own speed and are not affected by this.
        /// </summary>
        public const float MoveSpeed = 3f;

        /// <summary>
        /// Seconds a dead NPC's body stays up before the server despawns it, by type.
        /// </summary>
        /// <remarks>
        /// <para>The boss shipped with <c>m_KilledDestroyDelaySeconds = -1</c> on its prefab, and
        /// <c>ServerCharacter</c> only starts the despawn coroutine when that value is
        /// non-negative — so the boss's corpse was never removed at all. It lay where it fell for
        /// the rest of the match, blocking the arena and still drawing its defeat animation.</para>
        ///
        /// <para>Five seconds is long enough to watch it go down (<c>boss defeat start</c> runs
        /// into <c>boss defeat idle</c>, so the pose holds for as long as we leave it there) and
        /// short enough that the middle of the map comes back before the final scramble is over.
        /// In code rather than on the prefab for the usual reason: an asset edited on disk can be
        /// replaced by the Editor's cached copy when the dedicated-server build is made, and this
        /// is exactly the kind of value that has already been lost that way once.</para>
        /// </remarks>
        public static float GetKilledDestroyDelay(CharacterTypeEnum characterType, float configuredDelay)
        {
            return characterType == CharacterTypeEnum.ImpBoss ? k_BossDestroyDelaySeconds : configuredDelay;
        }

        const float k_BossDestroyDelaySeconds = 5f;

        /// <summary>
        /// Multiplier applied to all damage dealt *by* an NPC. Healing is never scaled.
        /// </summary>
        public const float DamageMultiplier = 3f;

        /// <summary>
        /// Extra multiplier on top of <see cref="DamageMultiplier"/> for the boss. Nobody revives
        /// anybody in deathmatch, so a boss combo that deletes a player outright is a much harsher
        /// punishment than it was in co-op: -30%.
        /// </summary>
        const float k_BossDamageMultiplier = 0.7f;

        /// <summary>
        /// Fallback health multiplier for any NPC type without an explicit value in
        /// <see cref="GetMaxHitPoints"/>.
        /// </summary>
        const float k_DefaultHealthMultiplier = 6f;

        // Explicit HP pools per monster, expressed as "how many hero swings does this take".
        //
        // PvPvE deathmatch pass: imps are consolation points, not a challenge — they have to die in
        // 2-3 hits so farming them is a fallback for whoever keeps losing straight fights, never a
        // fight of its own. The boss is balanced for a whole coordinated party in the original; here
        // it gets attacked by one or two players who also have to watch their backs, so its pool is
        // ~45% of what it was: about 30-40 seconds of solo work, long enough to be a real gamble.
        const int k_ImpHitPoints = 60;        // ~2 swings
        const int k_VandalImpHitPoints = 110; // ~3 swings: a ranged nuisance, but not a project
        // Doubled from 225. The boss is only on the board for the final minute (see
        // DeathmatchRules.BossSpawnTimeRemaining) and is worth a bit over two player kills, so it
        // has to survive long enough for taking it down to be a decision the room fights over
        // rather than something the first player to reach it finishes alone. At the ~30-40 seconds
        // of solo work above, it eats most of that minute — which is exactly the cost of choosing
        // it over hunting people, and the reason the two are worth checking together whenever
        // either number moves.
        const int k_ImpBossHitPoints = 450;

        /// <summary>
        /// Max HP for a character. Heroes are returned unchanged; monsters get their buffed pool.
        /// </summary>
        public static int GetMaxHitPoints(CharacterClass characterClass, int baseHitPoints)
        {
            if (characterClass == null || !characterClass.IsNpc)
            {
                return baseHitPoints;
            }

            switch (characterClass.CharacterType)
            {
                case CharacterTypeEnum.Imp:
                    return k_ImpHitPoints;
                case CharacterTypeEnum.VandalImp:
                    return k_VandalImpHitPoints;
                case CharacterTypeEnum.ImpBoss:
                    return k_ImpBossHitPoints;
                default:
                    return Mathf.Max(1, Mathf.RoundToInt(baseHitPoints * k_DefaultHealthMultiplier));
            }
        }

        /// <summary>
        /// Scales a positive damage value dealt by an NPC. Always returns at least the raw amount,
        /// so rounding can never make a monster weaker than its .asset says.
        /// </summary>
        /// <param name="rawDamage">Positive damage amount from the action asset.</param>
        /// <param name="attackerType">Who is swinging — the boss hits for less than the flat rate.</param>
        public static int ScaleDamage(int rawDamage, CharacterTypeEnum attackerType = CharacterTypeEnum.Imp)
        {
            if (rawDamage <= 0)
            {
                return rawDamage;
            }

            float multiplier = attackerType == CharacterTypeEnum.ImpBoss
                ? DamageMultiplier * k_BossDamageMultiplier
                : DamageMultiplier;

            return Mathf.Max(rawDamage, Mathf.RoundToInt(rawDamage * multiplier));
        }
    }
}
