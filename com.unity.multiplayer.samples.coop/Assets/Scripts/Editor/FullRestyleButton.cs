using UnityEditor;
using UnityEngine;

namespace Unity.BossRoom.Editor
{
    /// <summary>
    /// One menu item that runs the whole restyle in the order it has to happen in.
    /// </summary>
    /// <remarks>
    /// <para>The passes are order-dependent in ways that are not obvious from their names, which
    /// is the actual reason this exists — running them out of order does not error, it just
    /// quietly produces a worse result:</para>
    /// <list type="number">
    /// <item>Materials get their colours before they are assigned, or the characters receive the
    /// old flat versions and have to be re-assigned later.</item>
    /// <item>Weapons are swapped before neon trim is generated, because the trim is built from the
    /// bounds of whatever mesh is on the prefab at that moment — generate it first and you get
    /// trim shaped for the old weapon.</item>
    /// <item>Gear goes on after the silhouette pass, because helmet and shoulder scaling is solved
    /// against the bones' accumulated scale, which the silhouette pass changes.</item>
    /// </list>
    ///
    /// <para>Everything it calls is idempotent and recomputes from recorded originals, so pressing
    /// this repeatedly converges rather than compounding. Hand-adjusted weapons are left alone.</para>
    /// </remarks>
    public static class FullRestyleButton
    {
        [MenuItem("Boss Room/Style/★ DO EVERYTHING", priority = -100)]
        public static void DoEverything()
        {
            if (!EditorUtility.DisplayDialog(
                    "Apply full restyle?",
                    "This will:\n\n" +
                    "  1. Recolour the cyberpunk materials\n" +
                    "  2. Assign them to the characters\n" +
                    "  3. Re-proportion the characters\n" +
                    "  4. Swap in the Viking weapons\n" +
                    "  5. Add neon trim\n" +
                    "  6. Add shoulder pads, helmets and wings\n" +
                    "  7. Enable HDR and re-grade the post FX for neon\n\n" +
                    "Weapons you have moved by hand are left alone.\n" +
                    "WARNING: step 3 rewrites bone scales, so proportions you have adjusted by " +
                    "hand are NOT left alone. To restyle without touching them, run the numbered " +
                    "menu items individually and skip 3.\n" +
                    "Every step has its own Revert in the same menu.",
                    "Apply", "Cancel"))
            {
                return;
            }

            try
            {
                // Steps are wrapped so one failure doesn't leave the rest unrun with no
                // explanation — each reports for itself in the console.
                EditorUtility.DisplayProgressBar("Full restyle", "Recolouring materials...", 0.1f);
                CyberpunkMaterialPass.ApplyEmissionPass();

                EditorUtility.DisplayProgressBar("Full restyle", "Assigning materials...", 0.25f);
                CyberpunkMaterialPass.AssignMaterialsToCharacters();

                EditorUtility.DisplayProgressBar("Full restyle", "Re-proportioning characters...", 0.4f);
                CharacterSilhouettePass.ApplySilhouettes();

                EditorUtility.DisplayProgressBar("Full restyle", "Swapping weapons...", 0.6f);
                CyberMedievalModelPass.SwapWeapons();

                EditorUtility.DisplayProgressBar("Full restyle", "Adding neon trim...", 0.75f);
                CyberMedievalModelPass.AddNeonTrim();

                EditorUtility.DisplayProgressBar("Full restyle", "Adding gear...", 0.85f);
                HeroGearPass.AddGear();

                // Last, and not because of asset order: the post grade is judged against the
                // world the other passes produced, so it is tuned for (and applied to) the
                // finished look. It touches only the URP assets and the shared VolumeProfile —
                // never the character prefabs — so it is also safe to run on its own.
                EditorUtility.DisplayProgressBar("Full restyle", "Re-grading post FX...", 0.95f);
                CyberpunkPostFxPass.Apply();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Restyle] Full restyle complete. Check the messages above for per-step details.");
        }

        /// <summary>Undoes every step this button applies, in reverse order.</summary>
        [MenuItem("Boss Room/Style/★ UNDO EVERYTHING", priority = -99)]
        public static void UndoEverything()
        {
            if (!EditorUtility.DisplayDialog(
                    "Undo full restyle?",
                    "Removes the added gear, puts the stock weapons back, restores the stock " +
                    "character proportions and the stock post FX grade.\n\n" +
                    "Material colours are NOT reverted — they are edits to the Cyberpunk material " +
                    "set itself, which had no 'before' worth restoring.",
                    "Undo", "Cancel"))
            {
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("Undo restyle", "Restoring post FX...", 0.1f);
                CyberpunkPostFxPass.Revert();

                EditorUtility.DisplayProgressBar("Undo restyle", "Removing gear...", 0.3f);
                HeroGearPass.RemoveGear();

                EditorUtility.DisplayProgressBar("Undo restyle", "Restoring weapons...", 0.5f);
                CyberMedievalModelPass.RevertSwap();

                EditorUtility.DisplayProgressBar("Undo restyle", "Restoring proportions...", 0.8f);
                CharacterSilhouettePass.Revert();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Restyle] Undo complete.");
        }
    }
}
