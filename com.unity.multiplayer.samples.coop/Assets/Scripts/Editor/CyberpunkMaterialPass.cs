using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.BossRoom.Editor
{
    /// <summary>
    /// Restyles the heroes towards a cyberpunk-medieval look: takes the existing
    /// <c>Assets/Material/Characters/Cyberpunk</c> material set, gives every material a class-tinted
    /// emissive glow (strongest on weapons), and assigns the set onto the character prefabs in
    /// place of the flat Toon materials.
    /// </summary>
    /// <remarks>
    /// <para>The Cyberpunk materials already existed in the project but nothing referenced them —
    /// every character prefab still pointed at the Toon set. So this does two separable jobs: the
    /// emissive pass (which is what actually makes them read as cyberpunk rather than as slightly
    /// different toon colours), and the assignment (which is what puts them on screen). They are
    /// separate menu items so a bad-looking emission pass can be re-tuned and re-run without
    /// touching prefabs.</para>
    ///
    /// <para>The look is deliberately "dark armour, glowing trim" rather than "everything neon":
    /// emission is driven through the material's own base map, so the glow follows the existing
    /// texture's detail instead of flooding the whole silhouette, and it is tinted per class so
    /// players stay tellable apart at a glance — which in a free-for-all matters more than the
    /// styling does.</para>
    ///
    /// <para>Runs through AssetDatabase rather than by editing the .mat/.prefab YAML on disk,
    /// because with the project open the Editor serves its own cached copies and a disk-side edit
    /// is silently reverted when the build is made.</para>
    /// </remarks>
    public static class CyberpunkMaterialPass
    {
        const string k_CyberpunkFolder = "Assets/Material/Characters/Cyberpunk";
        const string k_CharacterPrefabFolder = "Assets/Prefabs/CharGFX";

        // The characters run on the Shader Graph at Assets/Shaders/SG_Toon.shadergraph, which
        // exposes exactly these eight properties and outputs only SurfaceDescription.BaseColor.
        //
        // THERE IS NO EMISSION CHANNEL. An earlier version of this pass wrote _EmissionColor,
        // _EmissionMap, _Metallic and _Smoothness; a Shader Graph only has the properties its
        // author declared, so every one of those writes was a no-op hidden behind a HasProperty
        // guard, and the pass reported success while changing nothing. That is why the characters
        // still looked flat. (Some .mat files do contain stale _EmissionColor/_Metallic entries —
        // leftover serialised data from when they used the Standard shader. They are dead weight,
        // not evidence that the property works.)
        //
        // With no emission and no bloom in the project, "glow" has to be built out of what a toon
        // shader actually gives you: a bright rim, a coloured specular hit, and a dark tinted
        // ambient so the lit areas have something to contrast against. That combination is what
        // reads as stylised rather than washed out — and it costs nothing on mobile, which matters
        // given this ships to Android and iOS.
        static readonly int k_Color = Shader.PropertyToID("_Color");
        static readonly int k_AmbientColor = Shader.PropertyToID("_AmbientColor");
        static readonly int k_SpecularColor = Shader.PropertyToID("_SpecularColor");
        static readonly int k_Glossiness = Shader.PropertyToID("_Glossiness");
        static readonly int k_RimColor = Shader.PropertyToID("_RimColor");
        static readonly int k_RimAmount = Shader.PropertyToID("_RimAmount");
        static readonly int k_RimThreshold = Shader.PropertyToID("_RimThreshold");

        // Rim geometry, in this shader's terms. The rim is thresholded with
        // smoothstep(_RimAmount - e, _RimAmount + e, rim), so a LOWER _RimAmount shows more of it.
        // (An earlier comment in this file had that backwards and pushed the value up, which made
        // the rim thinner — the opposite of the intent.)
        const float k_WeaponRimAmount = 0.45f;   // widest: the weapons carry the look
        // Widened from 0.55 (lower shows more). The rim is the actual edge between a character
        // and whatever is behind them, so it does more for separation from the floor than the
        // base colour does — and unlike brightness it costs nothing in readability.
        const float k_ArmourRimAmount = 0.46f;
        const float k_SkinRimAmount = 0.70f;     // just a kiss of light on faces

        // _RimThreshold gates the rim by how lit the surface is. Near zero puts the rim all the way
        // around the silhouette instead of only on the lit side, which is what makes a character
        // pop against a dark arena.
        const float k_WrapAroundRim = 0.05f;

        // The ambient (shadow) colour. Deep and tinted rather than the stock flat grey — coloured
        // shadows are most of the difference between "toon" and "washed out toon".
        static readonly Color k_ShadowTint = new(0.10f, 0.09f, 0.16f);

        // Accent colours live in HeroAccentPalette — one table shared by every pass, so a colour
        // change can't land on the armour but miss the weapons.

        // How hard each kind of surface glows. Weapons carry the look — a glowing blade reads as
        // cyberpunk instantly, where a glowing torso just reads as a bug.
        const float k_WeaponEmission = 2.2f;
        const float k_ArmourEmission = 0.5f;
        const float k_SkinEmission = 0.15f;

        // Darkening applied to the base colour. Medieval armour, not plastic: the glow only reads
        // if the material it sits on is dark.
        const float k_WeaponDarkening = 0.7f;
        // Above 1 now, i.e. a brightening rather than a darkening. The dark-armour rule is right
        // for the weapons, which are read against the character; the torso is read against the
        // *ground*, and the arena floor is dark too — so at 0.85 the clothing and the floor sat in
        // the same value range and the characters lost their outline as soon as they stopped
        // moving. The weapons keep the original treatment; only the clothing is lifted.
        const float k_ArmourDarkening = 1.2f;

        // How far the armour's shadow side is lifted out of the shared shadow tint. This is the
        // half of the contrast problem that brightness alone cannot fix: the lit side can be as
        // bright as you like and the character will still merge into a dark floor along whichever
        // side is turned away from the light.
        const float k_ArmourAmbientLift = 1.5f;

        static readonly string[] k_WeaponKeywords =
        {
            "weapon", "sword", "bow", "staff", "dagger", "shield", "axe", "hammer", "wand", "arrow", "quiver",
        };

        static readonly string[] k_SkinKeywords = { "head", "hair", "eyes", "mouth", "skin", "face" };

        /// <summary>
        /// Both steps in order. Exists so the restyle can be driven headlessly via
        /// <c>Unity.exe -batchmode -quit -executeMethod</c>, which only accepts one entry point —
        /// and because the two steps are order-dependent (assigning materials that have not had
        /// the emission pass yet just puts flat materials on the characters).
        /// </summary>
        [MenuItem("Boss Room/Style/Apply Full Restyle (both steps)")]
        public static void ApplyAll()
        {
            ApplyEmissionPass();
            AssignMaterialsToCharacters();
        }

        [MenuItem("Boss Room/Style/1. Add Emission To Cyberpunk Materials")]
        public static void ApplyEmissionPass()
        {
            var materials = LoadCyberpunkMaterials();
            if (materials.Count == 0)
            {
                Debug.LogError($"[Style] No materials found in {k_CyberpunkFolder}.");
                return;
            }

            int touched = 0;
            foreach (var material in materials)
            {
                if (ApplyEmission(material))
                {
                    touched++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Style] Emission pass applied to {touched}/{materials.Count} cyberpunk materials. " +
                      "Run step 2 to put them on the characters.");
        }

        [MenuItem("Boss Room/Style/2. Assign Cyberpunk Materials To Characters")]
        public static void AssignMaterialsToCharacters()
        {
            var byName = new Dictionary<string, Material>();
            foreach (var material in LoadCyberpunkMaterials())
            {
                // Prefer the exact "<Toon name>_Cyber" match. The folder also contains stray
                // duplicates like "Weapons_Mage_Cyber 1", which must never win.
                byName.TryAdd(material.name, material);
            }

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { k_CharacterPrefabFolder });
            int prefabsChanged = 0;
            int slotsChanged = 0;

            foreach (var guid in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                int changedHere = SwapPrefabMaterials(prefab, byName);
                if (changedHere > 0)
                {
                    EditorUtility.SetDirty(prefab);
                    PrefabUtility.SavePrefabAsset(prefab);
                    prefabsChanged++;
                    slotsChanged += changedHere;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Style] Assigned cyberpunk materials: {slotsChanged} material slot(s) across {prefabsChanged} prefab(s).");
        }

        // ── Emission ──────────────────────────────────────────────────────────────────────────

        static bool ApplyEmission(Material material)
        {
            // The one property every SG_Toon material must have. Anything without it is on a
            // different shader (the shadow blob, the eye/mouth sheets) and is left alone.
            if (!material.HasProperty(k_RimColor))
            {
                return false;
            }

            Color accent = AccentFor(material.name);
            SurfaceKind kind = ClassifySurface(material.name);

            float rimIntensity = kind switch
            {
                SurfaceKind.Weapon => k_WeaponEmission,
                SurfaceKind.Skin => k_SkinEmission,
                // Hair takes the hottest rim of anything on the character. A lit fringe is what
                // makes a face read as backlit rather than as flatly painted, and unlike the face
                // itself hair can carry a saturated colour without looking ill.
                SurfaceKind.Hair => 3f,
                // Eyes and mouth are tiny; they need to be pushed hard to register at all.
                SurfaceKind.Feature => 4f,
                _ => k_ArmourEmission,
            };

            // The rim IS the glow here. Pushed past 1 so it reads as light rather than as paint;
            // with no bloom in the project it will clamp, but a clamped hot edge still separates
            // the character from the background, which is the job.
            material.SetColor(k_RimColor, accent * rimIntensity);

            if (material.HasProperty(k_RimAmount))
            {
                material.SetFloat(k_RimAmount, kind switch
                {
                    SurfaceKind.Weapon => k_WeaponRimAmount,
                    SurfaceKind.Skin => k_SkinRimAmount,
                    _ => k_ArmourRimAmount,
                });
            }

            if (material.HasProperty(k_RimThreshold))
            {
                // Faces keep the stock behaviour (rim only where the light is); everything else
                // gets it wrapped all the way round.
                material.SetFloat(k_RimThreshold, kind == SurfaceKind.Skin ? 0.3f : k_WrapAroundRim);
            }

            // Coloured shadows. This is the change that does the most to kill the "flat and
            // washed out" look, and it costs nothing.
            if (material.HasProperty(k_AmbientColor))
            {
                Color ambient = kind == SurfaceKind.Skin
                    ? Color.Lerp(k_ShadowTint, accent, 0.15f) * 1.6f   // keep faces readable
                    : Color.Lerp(k_ShadowTint, accent, 0.35f);

                if (kind == SurfaceKind.Armour)
                {
                    ambient *= k_ArmourAmbientLift;
                }

                material.SetColor(k_AmbientColor, ambient);
            }

            // Coloured specular hit, so highlights carry the class colour instead of being white.
            if (material.HasProperty(k_SpecularColor))
            {
                material.SetColor(k_SpecularColor, accent * (kind == SurfaceKind.Weapon ? 6f : 3f));
            }

            // Higher Glossiness = a tighter, harder highlight, which is what sells metal on a
            // toon shader. Left alone for skin, where a pinpoint highlight looks like plastic.
            if (material.HasProperty(k_Glossiness) && (kind == SurfaceKind.Weapon || kind == SurfaceKind.Armour))
            {
                material.SetFloat(k_Glossiness, kind == SurfaceKind.Weapon ? 24f : 12f);
            }

            // Base tint. Always an ABSOLUTE value, never a multiply of what is already there, so
            // re-running the pass converges instead of fading everything to black one run at a time.
            if (material.HasProperty(k_Color))
            {
                switch (kind)
                {
                    // Tinted towards the class accent, NOT to grey. Setting these to a neutral grey
                    // was what drained the colour out of the whole cast: _Color multiplies the
                    // texture, so a grey tint desaturates everything the texture was carrying. A
                    // dark accent-tinted base keeps the armour reading as coloured metal while
                    // still being dark enough for the rim to stand off it.
                    case SurfaceKind.Weapon:
                        material.SetColor(k_Color, Tint(accent, k_WeaponDarkening, k_WeaponTintStrength));
                        break;

                    case SurfaceKind.Armour:
                        material.SetColor(k_Color, Tint(accent, k_ArmourDarkening, k_ArmourTintStrength));
                        break;

                    case SurfaceKind.Skin:
                        // Cooled and slightly desaturated. The texture underneath still carries
                        // the actual face, so this is a grade over it, not a repaint.
                        material.SetColor(k_Color, k_SkinTint);
                        break;

                    case SurfaceKind.Hair:
                        // Dyed to the class colour, kept dark enough that the hot rim on top of it
                        // still reads as a separate highlight rather than disappearing into it.
                        material.SetColor(k_Color, accent * 0.65f);
                        break;

                    case SurfaceKind.Feature:
                        // Eyes and mouth pushed bright. This is the closest thing to lit eyes that
                        // a shader with no emission channel can give.
                        material.SetColor(k_Color, accent * 1.8f);
                        break;
                }
            }

            EditorUtility.SetDirty(material);
            return true;
        }

        enum SurfaceKind
        {
            Armour,
            Weapon,
            /// <summary>Faces. Treated apart from hair because the two want opposite things.</summary>
            Skin,
            /// <summary>Hair. The one part of a face that can take a saturated colour and still
            /// look deliberate — which makes it the cheapest cyberpunk cue on a character.</summary>
            Hair,
            /// <summary>Eye and mouth sheets. Small, and read as lights rather than as surfaces.</summary>
            Feature,
        }

        static readonly string[] k_HairKeywords = { "hair" };
        static readonly string[] k_FeatureKeywords = { "eyes", "mouth" };

        static SurfaceKind ClassifySurface(string materialName)
        {
            string lower = materialName.ToLowerInvariant();

            foreach (var keyword in k_WeaponKeywords)
            {
                if (lower.Contains(keyword))
                {
                    return SurfaceKind.Weapon;
                }
            }

            // Checked before the generic skin list, which also matches "hair" and "eyes".
            foreach (var keyword in k_FeatureKeywords)
            {
                if (lower.Contains(keyword))
                {
                    return SurfaceKind.Feature;
                }
            }

            foreach (var keyword in k_HairKeywords)
            {
                if (lower.Contains(keyword))
                {
                    return SurfaceKind.Hair;
                }
            }

            foreach (var keyword in k_SkinKeywords)
            {
                if (lower.Contains(keyword))
                {
                    return SurfaceKind.Skin;
                }
            }

            return SurfaceKind.Armour;
        }

        /// <summary>
        /// Cool, slightly desaturated skin. Not grey — a grey face reads as a corpse, not as a
        /// cyborg. Just enough blue in it to sit under coloured rim light without going muddy.
        /// </summary>
        static readonly Color k_SkinTint = new(0.82f, 0.80f, 0.86f);

        /// <summary>
        /// A base tint at the given brightness, pulled <paramref name="strength"/> of the way from
        /// neutral towards the class accent. Brightness and hue are separate knobs on purpose:
        /// "darker" and "more colourful" are different complaints and need different dials.
        /// </summary>
        static Color Tint(Color accent, float brightness, float strength)
        {
            Color tinted = Color.Lerp(Color.white, accent, Mathf.Clamp01(strength));
            return new Color(tinted.r * brightness, tinted.g * brightness, tinted.b * brightness, 1f);
        }

        /// <summary>How far the base colour is pulled towards the class accent. This is the
        /// "more colour" dial — raise it for a stronger tint, drop it to go back to neutral metal.</summary>
        const float k_WeaponTintStrength = 0.5f;
        const float k_ArmourTintStrength = 0.65f;

        static Color AccentFor(string materialName) => HeroAccentPalette.ForAssetNamed(materialName);

        // ── Assignment ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Swaps every Toon material on a prefab for its <c>_Cyber</c> counterpart, matched by name.
        /// </summary>
        /// <remarks>
        /// Walks <i>every</i> renderer in the hierarchy including inactive ones, and every material
        /// slot on each. Both matter here: character parts are separate child objects (custom heads
        /// in particular hang off the original body part), and a renderer with two slots where only
        /// the first is swapped produces a character that is half-restyled.
        /// </remarks>
        static int SwapPrefabMaterials(GameObject prefab, Dictionary<string, Material> cyberMaterials)
        {
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            int changed = 0;

            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                bool rendererChanged = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    var current = materials[i];
                    if (current == null)
                    {
                        continue;
                    }

                    // Already restyled — running this twice must be a no-op, not a double swap.
                    if (current.name.EndsWith("_Cyber"))
                    {
                        continue;
                    }

                    if (!cyberMaterials.TryGetValue(current.name + "_Cyber", out var replacement))
                    {
                        continue;
                    }

                    materials[i] = replacement;
                    rendererChanged = true;
                    changed++;
                }

                if (rendererChanged)
                {
                    renderer.sharedMaterials = materials;
                    EditorUtility.SetDirty(renderer);
                }
            }

            return changed;
        }

        static List<Material> LoadCyberpunkMaterials()
        {
            var result = new List<Material>();
            var guids = AssetDatabase.FindAssets("t:Material", new[] { k_CyberpunkFolder });

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null)
                {
                    result.Add(material);
                }
            }

            return result;
        }
    }
}
