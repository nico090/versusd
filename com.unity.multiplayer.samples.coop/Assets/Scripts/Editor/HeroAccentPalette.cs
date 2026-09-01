using System.Collections.Generic;
using UnityEngine;

namespace Unity.BossRoom.Editor
{
    /// <summary>
    /// The one place the hero accent colours are defined.
    /// </summary>
    /// <remarks>
    /// <para>These colours drive every part of the restyle at once — material emission, the toon
    /// shader's rim light, the weapon metal tint and the procedural neon trim. They were previously
    /// copy-pasted into each pass, which meant "change the Mage to gold" was a four-file edit that
    /// was one missed file away from a character whose armour and whose sword disagreed. One table,
    /// read by everything.</para>
    ///
    /// <para><b>Editing these is the intended way to retune the look.</b> Change a value here, then
    /// re-run the Style menu items — the passes are all idempotent and recompute from the recorded
    /// originals, so re-running converges rather than compounding.</para>
    ///
    /// <para>The constraint the palette has to satisfy is gameplay, not taste: this is a
    /// free-for-all, so four heroes have to be tellable apart instantly and at distance. That means
    /// four well-separated hues at similar saturation — which is why they are spread around the
    /// wheel rather than chosen for how they look one at a time.</para>
    /// </remarks>
    public static class HeroAccentPalette
    {
        // Heraldic rather than neon-tech: red, blue, gold and violet read as banners lit from
        // behind, which sits better with medieval armour than the previous cyan/magenta/acid-green
        // did. Still four clearly separated hues.
        static readonly Dictionary<string, Color> k_Accents = new()
        {
            ["Tank"] = new Color(1f, 0.13f, 0.18f),      // crimson — the heavy, blood and forge
            ["Archer"] = new Color(0.15f, 0.45f, 1f),    // electric blue — cold, precise, distant
            ["Mage"] = new Color(1f, 0.78f, 0.18f),      // gold — arcane, warm, expensive
            ["Rogue"] = new Color(0.62f, 0.2f, 1f),      // violet — poison and shadow

            // Monsters sit outside the hero palette on purpose: they must never be mistaken for a
            // player at a glance.
            ["Boss"] = new Color(1f, 0.35f, 0.05f),
            ["Imp"] = new Color(0.9f, 0.25f, 0.05f),
            ["VandalImp"] = new Color(0.95f, 0.6f, 0.05f),
        };

        /// <summary>
        /// Overrides for a single hero variant, checked before <see cref="k_Accents"/>.
        /// </summary>
        /// <remarks>
        /// <para>The table above is per class, which is the right default: the four hues exist so
        /// the four classes are tellable apart at distance. This one exists for when a specific
        /// Boy/Girl variant is wanted in its own colour anyway — in a free-for-all, two players on
        /// the same class in different colours is a readability gain rather than a loss.</para>
        ///
        /// <para>Keys are matched as substrings of an asset name, so they must be more specific
        /// than the class key they override ("Rogue_Boy", not "Rogue") — and they are checked
        /// first, because "Torso_Rogue_Boy_Cyber" contains both.</para>
        /// </remarks>
        static readonly Dictionary<string, Color> k_VariantAccents = new()
        {
            // Green: well clear of the other three heroes, and of the violet the Rogue Girl keeps.
            ["Rogue_Boy"] = new Color(0.22f, 0.88f, 0.36f),
        };

        /// <summary>Used for anything whose name matches no known class.</summary>
        public static readonly Color Default = new(0.4f, 0.7f, 1f);

        /// <summary>The accent for an exact class or variant name.</summary>
        public static Color For(string className) =>
            k_VariantAccents.TryGetValue(className, out var variant) ? variant
            : k_Accents.TryGetValue(className, out var accent) ? accent
            : Default;

        /// <summary>
        /// The accent for an asset whose name merely contains a class name — e.g. the material
        /// "Weapons_Tank_Cyber" or "Head_Mage_Girl_Cyber".
        /// </summary>
        public static Color ForAssetNamed(string assetName)
        {
            // Variants first: their keys contain the class key, so class-first would always win.
            foreach (var pair in k_VariantAccents)
            {
                if (assetName.Contains(pair.Key))
                {
                    return pair.Value;
                }
            }

            foreach (var pair in k_Accents)
            {
                if (assetName.Contains(pair.Key))
                {
                    return pair.Value;
                }
            }

            return Default;
        }
    }
}
