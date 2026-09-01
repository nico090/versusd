using System.Collections.Generic;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.GameplayObjects
{
    /// <summary>What a zone on the ground does to whoever stands in it.</summary>
    public enum ZoneKind
    {
        /// <summary>Green. Heals while you stand in it.</summary>
        Heal,

        /// <summary>Blue. Grants a burst of movement speed that outlives the zone.</summary>
        Speed,

        /// <summary>Violet. Doubles outgoing damage, and outlives the zone.</summary>
        Damage,

        /// <summary>Red. Hurts while you stand in it.</summary>
        Hazard,
    }

    /// <summary>
    /// One zone, as the server publishes it. Kept to plain fields so it can live in a
    /// <c>SyncList</c> without a custom writer.
    /// </summary>
    public struct ZoneState
    {
        public int Id;
        public ZoneKind Kind;
        public Vector3 Position;
        public float Radius;

        /// <summary>Server time the zone disappears at. Clients use it to fade it out.</summary>
        public double ExpiresAt;

        public bool Equals(ZoneState other) =>
            Id == other.Id && Kind == other.Kind && Position == other.Position
            && Mathf.Approximately(Radius, other.Radius) && ExpiresAt.Equals(other.ExpiresAt);
    }

    /// <summary>
    /// Every tunable number of the zone system, in one place.
    /// </summary>
    /// <remarks>
    /// In code rather than a ScriptableObject for the same reason as
    /// <see cref="Configuration.DeathmatchRules"/> and <see cref="Configuration.NpcBalance"/>: a
    /// GameData asset edited on disk can be silently replaced by the Editor's cached copy when the
    /// dedicated-server build is made, and these numbers have to be identical on both sides — the
    /// server applies the effect, the client draws the circle it happens in.
    /// </remarks>
    public static class ZoneRules
    {
        /// <summary>Seconds between one zone appearing and the next.</summary>
        public const float SpawnIntervalSeconds = 18f;

        /// <summary>Random slack on that interval, so the map does not tick like a metronome.</summary>
        public const float SpawnJitterSeconds = 7f;

        /// <summary>How long a zone stays on the ground.</summary>
        public const float LifetimeSeconds = 25f;

        /// <summary>Most zones alive at once. Past this the oldest is retired early.</summary>
        public const int MaxConcurrent = 3;

        /// <summary>Ground radius of a zone.</summary>
        public const float Radius = 4.5f;

        /// <summary>
        /// How far from the centre of the arena a zone may appear.
        /// </summary>
        /// <remarks>
        /// Measured from the centroid of the player spawn points, the same anchor the boss uses, so
        /// zones land inside the space the fight actually happens in rather than in a corner of the
        /// map nobody walks through.
        /// </remarks>
        public const float SpawnSpread = 22f;

        /// <summary>Minimum gap between two live zones, so they never overlap into one blob.</summary>
        public const float MinSeparation = 11f;

        /// <summary>How often the server applies zone effects. Not every frame — this is a sweep
        /// over every character, and a heal that ticks four times a second is legible.</summary>
        public const float TickSeconds = 0.25f;

        /// <summary>HP restored per second inside a green zone.</summary>
        public const int HealPerSecond = 14;

        /// <summary>HP lost per second inside a red zone.</summary>
        public const int HazardPerSecond = 12;

        /// <summary>Seconds the speed and damage boons last, counted from leaving the zone.</summary>
        public const float BoonSeconds = 30f;

        /// <summary>Movement multiplier the blue zone grants.</summary>
        public const float SpeedMultiplier = 1.45f;

        /// <summary>Outgoing damage multiplier the violet zone grants.</summary>
        public const float DamageMultiplier = 2f;

        public static Color ColorFor(ZoneKind kind)
        {
            switch (kind)
            {
                case ZoneKind.Heal: return new Color(0.30f, 0.95f, 0.45f);
                case ZoneKind.Speed: return new Color(0.25f, 0.65f, 1f);
                case ZoneKind.Damage: return new Color(0.72f, 0.35f, 1f);
                case ZoneKind.Hazard: return new Color(1f, 0.25f, 0.22f);
                default: return Color.white;
            }
        }
    }

    /// <summary>
    /// Server-side record of who currently has a zone boon, and until when.
    /// </summary>
    /// <remarks>
    /// <para>A static table rather than a component on the character, deliberately. Adding a
    /// component means editing the character prefabs, and in a Mirror project a new
    /// <c>NetworkBehaviour</c> on a prefab also shifts component indices — this project has already
    /// been bitten by prefab edits that the Editor's cache quietly reverted before a build. The
    /// boons are pure server state (speed is applied by ServerCharacterMovement, damage by
    /// ServerCharacter) so nothing needs to replicate, and a lookup by netId costs nothing at the
    /// rate these are read.</para>
    ///
    /// <para>Cleared when the match state tears down, so a boon cannot survive into the next one.</para>
    /// </remarks>
    public static class ZoneBoons
    {
        struct Boon
        {
            public float SpeedUntil;
            public float DamageUntil;
        }

        static readonly Dictionary<uint, Boon> s_Boons = new Dictionary<uint, Boon>();

        public static void GrantSpeed(uint netId)
        {
            var boon = s_Boons.TryGetValue(netId, out var existing) ? existing : default;
            // Refreshed, not stacked: standing in the zone keeps topping the clock back up to the
            // full duration, which is what a player expects and what stops a camper accumulating
            // minutes of it.
            boon.SpeedUntil = Time.time + ZoneRules.BoonSeconds;
            s_Boons[netId] = boon;
        }

        public static void GrantDamage(uint netId)
        {
            var boon = s_Boons.TryGetValue(netId, out var existing) ? existing : default;
            boon.DamageUntil = Time.time + ZoneRules.BoonSeconds;
            s_Boons[netId] = boon;
        }

        public static bool HasSpeed(uint netId) =>
            s_Boons.TryGetValue(netId, out var boon) && Time.time < boon.SpeedUntil;

        public static bool HasDamage(uint netId) =>
            s_Boons.TryGetValue(netId, out var boon) && Time.time < boon.DamageUntil;

        /// <summary>Seconds of speed boon left, for the HUD. Zero when there is none.</summary>
        public static float SpeedRemaining(uint netId) =>
            s_Boons.TryGetValue(netId, out var boon) ? Mathf.Max(0f, boon.SpeedUntil - Time.time) : 0f;

        /// <summary>Seconds of damage boon left, for the HUD. Zero when there is none.</summary>
        public static float DamageRemaining(uint netId) =>
            s_Boons.TryGetValue(netId, out var boon) ? Mathf.Max(0f, boon.DamageUntil - Time.time) : 0f;

        public static void Clear() => s_Boons.Clear();

        public static void Forget(uint netId) => s_Boons.Remove(netId);
    }
}
