using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unity.BossRoom.Editor
{
    /// <summary>
    /// Re-grades the whole game for the cyberpunk look: HDR on, bloom tuned for neon, and a
    /// teal-shadow / cool-highlight colour grade. One menu item, because it is one decision.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists at all.</b> The restyle passes gave the world emissive neon —
    /// weapons at 2.2× emission, gear accents, generated wing panels — and it still looked flat,
    /// because the frame it renders into is the stock Boss Room frame. Two things cap it:</para>
    ///
    /// <para><b>1. HDR is off on every URP asset.</b> With an LDR colour buffer nothing can exceed
    /// brightness 1, so "2.2× emission" is clamped to plain white before bloom ever sees it, and
    /// the bloom threshold has to sit at 0.5 to catch anything — meaning it also catches ordinary
    /// bright floors. Turning HDR on is the single biggest visual change in this pass: neon gets
    /// to be brighter than the world around it, which is the entire premise of neon.</para>
    ///
    /// <para><b>2. The shared VolumeProfile is still the stock grade.</b> Warm pink bloom tint
    /// (torchlight), saturation 5, skipIterations 6 (a tight, cheap glow), and a LiftGammaGain
    /// whose gain drags red/green down for the dungeon's cold-stone look. Both scenes point at the
    /// one profile at Assets/URP/PostProcessProfile.asset and both cameras already render post FX,
    /// so retuning that single asset restyles MainMenu and BossRoom together with no scene
    /// edits.</para>
    ///
    /// <para>Everything is applied through the URP API on loaded assets (not by rewriting YAML on
    /// disk), so it lands correctly with the Editor open. Revert restores the stock numbers, which
    /// are hardcoded here — they were read out of the profile before the first Apply, and keeping
    /// them in code beats a backup asset that can go missing.</para>
    /// </remarks>
    public static class CyberpunkPostFxPass
    {
        const string k_ProfilePath = "Assets/URP/PostProcessProfile.asset";

        // ── The cyberpunk grade. These are the dials to turn if the look is off. ─────────────

        // Bloom: wide and cool. Threshold below 1 so strong emission blooms but plain white UI
        // and bright floors (about 1.0 after grading) mostly don't. maxIterations at the cap is
        // what turns a pinpoint glint into a soft halo — each iteration is another, wider blur
        // mip, so the count is the width of the glow.
        const float k_BloomThreshold = 0.85f;
        const float k_BloomIntensity = 1.7f;
        const float k_BloomScatter = 0.6f;
        const int k_BloomMaxIterations = 8;
        static readonly Color k_BloomTint = new Color(0.82f, 0.9f, 1f);

        // Grade: more contrast and saturation than stock (11/5), because neon against darkness is
        // a high-contrast look by definition. Exposure comes down a touch so the darks stay dark
        // enough for the glow to matter.
        const float k_Contrast = 18f;
        const float k_Saturation = 12f;
        const float k_PostExposure = 0.65f;

        // Cool the whole image slightly and push a hint of magenta — the two-tone cyberpunk cast.
        const float k_WhiteBalanceTemperature = -8f;
        const float k_WhiteBalanceTint = 4f;

        // Teal shadows, near-neutral mids, cool bright highlights. The stock grade's gain dragged
        // the whole frame down (w = -0.15) for dungeon gloom; that darkening moves to the shadows
        // (lift/gamma w) where it belongs, so the highlights are free to carry the neon.
        static readonly Vector4 k_Lift = new Vector4(0.97f, 1f, 1.04f, 0f);
        static readonly Vector4 k_Gamma = new Vector4(0.96f, 0.98f, 1.04f, -0.02f);
        static readonly Vector4 k_Gain = new Vector4(0.9f, 0.95f, 1.08f, -0.05f);

        // A slightly stronger, blue-black vignette pulls the eye to the lit centre.
        const float k_VignetteIntensity = 0.34f;
        static readonly Color k_VignetteColor = new Color(0.02f, 0.03f, 0.08f);

        // Barely-there lens fringing. At 0.08 it reads as "screen", not as a broken camera.
        const float k_ChromaticAberration = 0.08f;

        [MenuItem("Boss Room/Style/7. Cyberpunk Post FX (HDR + Bloom + Grade)")]
        public static void Apply()
        {
            int pipelines = ConfigurePipelines(enableHdr: true);
            bool graded = TuneProfile();

            AssetDatabase.SaveAssets();
            Debug.Log($"[PostFX] HDR enabled on {pipelines} URP asset(s) (Low quality left LDR on purpose) " +
                      $"and the shared profile {(graded ? "re-graded" : "NOT FOUND — no grading applied")}. " +
                      "Both scenes share that profile, so this covers the menu and the arena. " +
                      "Use 'Revert Post FX' to go back to stock.");
        }

        [MenuItem("Boss Room/Style/Revert Post FX")]
        public static void Revert()
        {
            int pipelines = ConfigurePipelines(enableHdr: false);
            bool restored = RestoreStockProfile();

            AssetDatabase.SaveAssets();
            Debug.Log($"[PostFX] Reverted: HDR off on {pipelines} URP asset(s), profile " +
                      (restored ? "restored to the stock Boss Room grade." : "not found."));
        }

        // ── Pipelines ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// HDR (and, on desktop, HDR-precision grading) across the quality ladder. Returns how
        /// many assets were touched.
        /// </summary>
        /// <remarks>
        /// The two "Low" assets are skipped when enabling: Low exists for the weakest phones, and
        /// an HDR colour buffer is exactly the kind of cost that tier is there to avoid. Everyone
        /// on Medium and up gets the real look. HDR-mode grading (with its bigger LUT) is desktop
        /// only for the same reason — mobile keeps LDR grading over an HDR buffer, which is the
        /// standard budget compromise and still blooms correctly.
        /// </remarks>
        static int ConfigurePipelines(bool enableHdr)
        {
            int touched = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (pipeline == null)
                {
                    continue;
                }

                bool isLowTier = path.Contains("_Low");
                bool isDesktop = path.Contains("Windows");

                pipeline.supportsHDR = enableHdr && !isLowTier;

                if (isDesktop)
                {
                    pipeline.colorGradingMode = pipeline.supportsHDR
                        ? ColorGradingMode.HighDynamicRange
                        : ColorGradingMode.LowDynamicRange;
                    pipeline.colorGradingLutSize = pipeline.supportsHDR ? 32 : 16;
                }

                EditorUtility.SetDirty(pipeline);
                touched++;
            }

            return touched;
        }

        // ── Profile ───────────────────────────────────────────────────────────────────────────

        static bool TuneProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(k_ProfilePath);
            if (profile == null)
            {
                Debug.LogWarning($"[PostFX] No VolumeProfile at {k_ProfilePath}.");
                return false;
            }

            if (profile.TryGet(out Bloom bloom))
            {
                bloom.threshold.Override(k_BloomThreshold);
                bloom.intensity.Override(k_BloomIntensity);
                bloom.scatter.Override(k_BloomScatter);
                bloom.tint.Override(k_BloomTint);
                bloom.maxIterations.Override(k_BloomMaxIterations);
            }

            if (profile.TryGet(out ColorAdjustments colour))
            {
                colour.postExposure.Override(k_PostExposure);
                colour.contrast.Override(k_Contrast);
                colour.saturation.Override(k_Saturation);
            }

            if (profile.TryGet(out LiftGammaGain liftGammaGain))
            {
                liftGammaGain.lift.Override(k_Lift);
                liftGammaGain.gamma.Override(k_Gamma);
                liftGammaGain.gain.Override(k_Gain);
            }

            if (profile.TryGet(out Vignette vignette))
            {
                vignette.intensity.Override(k_VignetteIntensity);
                vignette.color.Override(k_VignetteColor);
            }

            // These two aren't in the stock profile, so they are added rather than found. Added
            // once — TryGet on a re-run finds the earlier instance, so pressing the button twice
            // doesn't stack components.
            GetOrAdd<WhiteBalance>(profile, out var whiteBalance);
            whiteBalance.temperature.Override(k_WhiteBalanceTemperature);
            whiteBalance.tint.Override(k_WhiteBalanceTint);

            GetOrAdd<ChromaticAberration>(profile, out var fringe);
            fringe.intensity.Override(k_ChromaticAberration);

            EditorUtility.SetDirty(profile);
            return true;
        }

        /// <summary>
        /// The stock Boss Room numbers, as read from the profile before this pass first touched
        /// it. In code rather than in a backup asset so the revert can't be lost.
        /// </summary>
        static bool RestoreStockProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(k_ProfilePath);
            if (profile == null)
            {
                return false;
            }

            if (profile.TryGet(out Bloom bloom))
            {
                bloom.threshold.Override(0.5f);
                bloom.intensity.Override(1.1f);
                bloom.scatter.Override(0.23f);
                bloom.tint.Override(new Color(1f, 0.773f, 0.773f));
                // The stock profile's iteration count was authored against the old skipIterations
                // parameter, which this URP no longer has; its replacement defaults to 6, so
                // clearing the override IS the stock behaviour.
                bloom.maxIterations.overrideState = false;
                bloom.maxIterations.value = 6;
            }

            if (profile.TryGet(out ColorAdjustments colour))
            {
                colour.postExposure.Override(0.75f);
                colour.contrast.Override(11f);
                colour.saturation.Override(5f);
            }

            if (profile.TryGet(out LiftGammaGain liftGammaGain))
            {
                liftGammaGain.lift.Override(new Vector4(1f, 0.99057823f, 0.99764466f, 0f));
                liftGammaGain.gamma.Override(new Vector4(0.9584585f, 0.96702385f, 1f, 0f));
                liftGammaGain.gain.Override(new Vector4(0.8827872f, 0.91836137f, 1f, -0.15128592f));
            }

            if (profile.TryGet(out Vignette vignette))
            {
                vignette.intensity.Override(0.379f);
                // Stock never overrode the colour; clearing the flag puts the default black back.
                vignette.color.overrideState = false;
                vignette.color.value = Color.black;
            }

            // Ours entirely — stock didn't have them, so reverting means removing them.
            if (profile.Has<WhiteBalance>())
            {
                profile.Remove<WhiteBalance>();
            }

            if (profile.Has<ChromaticAberration>())
            {
                profile.Remove<ChromaticAberration>();
            }

            EditorUtility.SetDirty(profile);
            return true;
        }

        static void GetOrAdd<T>(VolumeProfile profile, out T component) where T : VolumeComponent
        {
            if (!profile.TryGet(out component))
            {
                component = profile.Add<T>();
            }
        }
    }
}
