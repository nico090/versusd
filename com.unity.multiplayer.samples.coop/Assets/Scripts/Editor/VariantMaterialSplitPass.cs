using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.BossRoom.Editor
{
    /// <summary>
    /// Gives one hero variant its own copy of a material its twin shares, so the two can be
    /// coloured apart.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this is needed.</b> The armour colour comes from
    /// <see cref="HeroAccentPalette"/>, which <see cref="CyberpunkMaterialPass"/> reads per
    /// material. So a variant can only have its own colour if it has its own material — and most
    /// of them do not. <c>Rogue_Torso_Boy</c> and <c>Rogue_Torso_Girl</c> both point at
    /// <c>Torso_Rogue_Cyber</c>, so painting the Boy green would paint the Girl green too. (The
    /// Mage is the exception: it already ships <c>Torso_Mage_Boy_Cyber</c> and
    /// <c>Torso_Mage_Girl_Cyber</c>, which is the arrangement this pass reproduces.)</para>
    ///
    /// <para><b>Why the part prefabs and not the character prefab.</b> The pieces are nested
    /// prefabs, and the Boy's pieces are already Boy-specific assets shared between his gameplay
    /// and character-select graphics. Re-pointing <c>Rogue_Torso_Boy.prefab</c> therefore reaches
    /// both screens and cannot touch the Girl, where an override on the character prefab would
    /// reach one screen and have to be repeated on the other.</para>
    ///
    /// <para>Idempotent: re-running finds the split material already there and only re-assigns.
    /// The colour itself is not applied here — that stays
    /// <see cref="CyberpunkMaterialPass"/>'s job, so there remains exactly one place that decides
    /// what a surface looks like.</para>
    /// </remarks>
    public static class VariantMaterialSplitPass
    {
        const string k_PartFolder = "Assets/Prefabs/CharGFX";
        const string k_MaterialFolder = "Assets/Material/Characters/Cyberpunk";

        /// <summary>
        /// One split: the part prefabs to re-point, the material they currently share, and the
        /// name the copy takes. The new name has to contain the variant key
        /// <see cref="HeroAccentPalette"/> overrides on, or the copy comes out the class colour
        /// and the whole exercise achieves nothing.
        /// </summary>
        readonly struct Split
        {
            public readonly string[] Parts;
            public readonly string SharedMaterial;
            public readonly string VariantMaterial;

            /// <summary>
            /// Albedo to put on the copy, or null to keep the shared one's.
            /// </summary>
            /// <remarks>
            /// The palette alone cannot recolour this surface. <c>_Color</c> <b>multiplies</b> the
            /// albedo, and the Rogue's torso texture is a red tunic — so a green tint over it
            /// yields a dark muddy brown, because red has almost no green channel left to
            /// multiply. Anything more than a nudge in hue has to come from a repainted texture;
            /// the tint then rides on top of it instead of fighting it.
            /// </remarks>
            public readonly string VariantTexture;

            public Split(string sharedMaterial, string variantMaterial, string variantTexture,
                params string[] parts)
            {
                SharedMaterial = sharedMaterial;
                VariantMaterial = variantMaterial;
                VariantTexture = variantTexture;
                Parts = parts;
            }
        }

        static readonly Split[] k_Splits =
        {
            // The Rogue Boy's armour. Torso and both hands wear the body material, so all three
            // have to move together or he ends up green with violet forearms.
            new("Torso_Rogue_Cyber", "Torso_Rogue_Boy_Cyber",
                "Assets/Textures/Characters/Rogue/Rogue_Torso_Boy_Green_CLR.png",
                "Rogue_Torso_Boy", "Rogue_Hand_Lt_Boy", "Rogue_Hand_Rt_Boy"),
        };

        [MenuItem("Boss Room/Style/9. Split Variant Materials")]
        public static void Apply()
        {
            int repointed = 0;

            foreach (var split in k_Splits)
            {
                var variant = EnsureVariantMaterial(split);
                if (variant == null)
                {
                    continue;
                }

                // Re-applied on every run, not just on creation: the texture is the half of the
                // recolour the style passes do not own, so it has to survive them being re-run.
                ApplyVariantTexture(split, variant);

                foreach (var part in split.Parts)
                {
                    if (RepointPart(part, split.SharedMaterial, variant))
                    {
                        repointed++;
                    }
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[VariantSplit] {repointed} part prefab(s) re-pointed. " +
                      "Now re-run 'Assign Cyberpunk Materials To Characters' (or the full restyle) " +
                      "to colour them from HeroAccentPalette.");
        }

        /// <summary>The variant's own material, copied from the shared one on first run.</summary>
        static Material EnsureVariantMaterial(Split split)
        {
            string variantPath = $"{k_MaterialFolder}/{split.VariantMaterial}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(variantPath);
            if (existing != null)
            {
                return existing;
            }

            string sharedPath = $"{k_MaterialFolder}/{split.SharedMaterial}.mat";
            var shared = AssetDatabase.LoadAssetAtPath<Material>(sharedPath);
            if (shared == null)
            {
                Debug.LogError($"[VariantSplit] No material at {sharedPath} to copy.");
                return null;
            }

            if (!AssetDatabase.CopyAsset(sharedPath, variantPath))
            {
                Debug.LogError($"[VariantSplit] Could not copy {sharedPath} to {variantPath}.");
                return null;
            }

            Debug.Log($"[VariantSplit] Created {split.VariantMaterial} from {split.SharedMaterial}.");
            return AssetDatabase.LoadAssetAtPath<Material>(variantPath);
        }

        /// <summary>Puts the variant's own albedo on it, if it has one.</summary>
        static void ApplyVariantTexture(Split split, Material variant)
        {
            if (string.IsNullOrEmpty(split.VariantTexture))
            {
                return;
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(split.VariantTexture);
            if (texture == null)
            {
                Debug.LogError($"[VariantSplit] No texture at {split.VariantTexture}.");
                return;
            }

            bool assigned = false;
            foreach (var slot in new[] { "_MainTex", "_BaseMap" })
            {
                if (variant.HasProperty(slot))
                {
                    variant.SetTexture(slot, texture);
                    assigned = true;
                    break;
                }
            }

            if (assigned)
            {
                EditorUtility.SetDirty(variant);
            }
            else
            {
                Debug.LogWarning($"[VariantSplit] {split.VariantMaterial} has no _MainTex/_BaseMap slot.");
            }
        }

        /// <summary>
        /// Swaps <paramref name="shared"/> for <paramref name="variant"/> on one part prefab,
        /// leaving any other material it wears alone.
        /// </summary>
        static bool RepointPart(string partName, string shared, Material variant)
        {
            string path = $"{k_PartFolder}/{partName}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogWarning($"[VariantSplit] {path} not found — skipped.");
                return false;
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;

                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    var updated = new List<Material>(materials.Length);
                    bool touched = false;

                    foreach (var material in materials)
                    {
                        // Matched by name rather than by reference: after the first run the part
                        // already wears the variant, and the shared asset is no longer on it.
                        if (material != null && material.name == shared)
                        {
                            updated.Add(variant);
                            touched = true;
                        }
                        else
                        {
                            updated.Add(material);
                        }
                    }

                    if (touched)
                    {
                        renderer.sharedMaterials = updated.ToArray();
                        changed = true;
                    }
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }

                return changed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
