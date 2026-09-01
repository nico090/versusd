using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.BossRoom.Editor
{
    /// <summary>Original bone scales, so the silhouette pass can be undone.</summary>
    public class SilhouetteBackup : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public GameObject Prefab;
            public List<string> BonePaths = new();
            public List<Vector3> OriginalScales = new();
        }

        public List<Entry> Entries = new();
    }

    /// <summary>
    /// Re-proportions the heroes so they stop reading as the stock Boss Room cast, by rescaling
    /// bones rather than by re-modelling anything.
    /// </summary>
    /// <remarks>
    /// <para><b>Why bones and not meshes.</b> The bodies are skinned meshes baked out of
    /// <c>CharacterSet.fbx</c> — re-modelling them needs a DCC tool. But silhouette, not surface
    /// detail, is what makes a character read as a different character: at gameplay distance you
    /// recognise a hero by head-to-body ratio and shoulder width long before you see a texture. The
    /// skeleton is a plain Transform hierarchy, so those proportions are fully scriptable, and a
    /// skinned mesh follows its bones' scales.</para>
    ///
    /// <para><b>Scaling is inherited.</b> A bone's scale multiplies through its children, which is
    /// used deliberately here: scaling <c>Bone_LeftArm</c> thickens the whole arm including the
    /// forearm and hand. It is also why <c>Bone_Hips</c> and <c>Bone_Spine</c> are handled
    /// carefully — they are the root of nearly everything, so a value there resizes the entire
    /// character rather than one feature.</para>
    ///
    /// <para><b>Known risk:</b> if any animation clip contains scale curves for these bones, the
    /// animator will overwrite these values at runtime and the re-proportioning will only be
    /// visible on the prefab. Humanoid clips almost always animate rotation (plus root position)
    /// only, so this is unlikely — but it is the first thing to check if the characters look
    /// re-proportioned in the inspector and stock in play mode.</para>
    /// </remarks>
    public static class CharacterSilhouettePass
    {
        const string k_CharacterFolder = "Assets/Prefabs/CharGFX/CharacterGraphics";
        const string k_BackupPath = "Assets/Prefabs/CharGFX/SilhouetteBackup.asset";

        /// <summary>The one bone that is not stock at scale 1 — it holds the rig's unit conversion.</summary>
        const string k_RigScaleBone = "Bone_Hips";

        /// <summary>
        /// Per-class bone scales. 1 = untouched. These are the knobs to turn after looking at the
        /// result — each line is one readable feature of the silhouette.
        /// </summary>
        /// <remarks>
        /// The direction of each class is deliberate and mutually contrasting, because the point is
        /// that four heroes read as four different people at a glance in a free-for-all:
        /// the Tank is a brick, the Archer is a whip, the Mage is top-heavy, the Rogue is compact.
        /// </remarks>
        /// <remarks>
        /// <para><b>These are UNIFORM scales — a single float per bone, deliberately not a
        /// Vector3.</b> An earlier version took a Vector3 and used values like (1.28, 1.12, 1.28)
        /// to "thicken without lengthening". On a skinned mesh that does not thin or fatten a
        /// limb, it <i>shears</i> it: every child bone inherits the parent's non-uniform scale
        /// expressed in a rotated frame, so the geometry skews diagonally at each joint and the
        /// character melts. Making the type a float means that mistake cannot be written again.</para>
        ///
        /// <para>Values are also far more conservative than before, and no bone is scaled while an
        /// ancestor of it is also scaled — scaling an arm and then its hand multiplied the two
        /// together (1.28 x 1.25) and blew the hands up.</para>
        /// </remarks>
        static readonly Dictionary<string, Dictionary<string, float>> k_ClassProportions = new()
        {
            // Brutish: small head reads as heavy shoulders without touching the shoulders at all —
            // head-to-body ratio does most of the work, and it is the safest bone to scale because
            // nothing but the helmet hangs off it.
            ["Tank"] = new Dictionary<string, float>
            {
                ["Bone_Head"] = 0.88f,
                ["Bone_LeftHand"] = 1.15f,
                ["Bone_RightHand"] = 1.15f,
            },

            // Small head + slightly longer legs = tall and lean at a glance.
            ["Archer"] = new Dictionary<string, float>
            {
                ["Bone_Head"] = 0.92f,
                ["Bone_LeftUpLeg"] = 1.08f,
                ["Bone_RightUpLeg"] = 1.08f,
            },

            // Big head, short legs: top-heavy, reads as a caster.
            ["Mage"] = new Dictionary<string, float>
            {
                ["Bone_Head"] = 1.16f,
                ["Bone_LeftUpLeg"] = 0.94f,
                ["Bone_RightUpLeg"] = 0.94f,
            },

            // Compact, with long forearms for the knife work.
            ["Rogue"] = new Dictionary<string, float>
            {
                ["Bone_Head"] = 0.96f,
                ["Bone_LeftForeArm"] = 1.1f,
                ["Bone_RightForeArm"] = 1.1f,
            },
        };

        static readonly string[] k_Classes = { "Tank", "Archer", "Mage", "Rogue" };
        static readonly string[] k_Genders = { "Boy", "Girl" };

        [MenuItem("Boss Room/Style/5. Re-proportion Characters")]
        public static void ApplySilhouettes()
        {
            var backup = LoadOrCreateBackup();
            int prefabsChanged = 0;
            int bonesChanged = 0;

            foreach (var className in k_Classes)
            {
                if (!k_ClassProportions.TryGetValue(className, out var proportions))
                {
                    continue;
                }

                foreach (var gender in k_Genders)
                {
                    string path = $"{k_CharacterFolder}/PlayerGraphics_{className}_{gender}.prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        Debug.LogWarning($"[Silhouette] Not found: {path}");
                        continue;
                    }

                    int changed = ApplyToPrefab(prefab, proportions, backup);
                    if (changed > 0)
                    {
                        EditorUtility.SetDirty(prefab);
                        PrefabUtility.SavePrefabAsset(prefab);
                        prefabsChanged++;
                        bonesChanged += changed;
                    }
                }
            }

            EditorUtility.SetDirty(backup);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Silhouette] Re-proportioned {prefabsChanged} character prefab(s), {bonesChanged} bone(s). " +
                      "Use 'Revert Character Proportions' to undo. Tell me which class looks wrong and how " +
                      "(\"the Mage's head is comically big\") and I'll adjust its row in k_ClassProportions.");
        }

        [MenuItem("Boss Room/Style/Revert Character Proportions")]
        public static void Revert()
        {
            var backup = AssetDatabase.LoadAssetAtPath<SilhouetteBackup>(k_BackupPath);
            if (backup == null)
            {
                Debug.LogWarning("[Silhouette] No backup found — nothing to revert.");
                return;
            }

            int reverted = 0;
            foreach (var entry in backup.Entries)
            {
                if (entry.Prefab == null)
                {
                    continue;
                }

                for (int i = 0; i < entry.BonePaths.Count && i < entry.OriginalScales.Count; i++)
                {
                    var bone = FindBoneByName(entry.Prefab.transform, entry.BonePaths[i]);
                    if (bone != null)
                    {
                        bone.localScale = entry.OriginalScales[i];
                    }
                }

                EditorUtility.SetDirty(entry.Prefab);
                PrefabUtility.SavePrefabAsset(entry.Prefab);
                reverted++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Silhouette] Reverted {reverted} character prefab(s) to stock proportions.");
        }

        /// <summary>
        /// Writes every bone on the eight hero prefabs back to its stock scale and throws the
        /// backup away. Run 'Re-proportion Characters' afterwards to put the intended silhouettes
        /// back on top of a clean rig.
        /// </summary>
        /// <remarks>
        /// <para>This exists because the backup on disk cannot be trusted. An earlier version of
        /// this pass took Vector3 scales and recorded the already-scaled bone as the "original",
        /// so the recorded originals are themselves scaled: reverting with them restores a
        /// squashed character, and re-applying multiplies on top of it. That is how the prefabs
        /// ended up carrying values like Bone_LeftArm at (1.64, 1.25, 1.64) — non-uniform, and a
        /// number this pass can no longer even produce.</para>
        ///
        /// <para>Stock is knowable without a backup, which is what makes the repair safe: every
        /// bone on these rigs ships at scale 1 except <c>Bone_Hips</c>, which carries the rig's
        /// 0.01 unit conversion. So the repair is to write 1 everywhere else and start recording
        /// again from a rig that is actually stock.</para>
        ///
        /// <para>It matters beyond proportions: gear is sized against the scale its bone has
        /// accumulated, so left-over bone scales make the shoulder pads and helmets come out
        /// different sizes on each class.</para>
        /// </remarks>
        [MenuItem("Boss Room/Style/Repair: Reset Bone Scales To Stock")]
        public static void ResetBoneScales()
        {
            if (!EditorUtility.DisplayDialog(
                    "Reset bone scales?",
                    "Sets every bone on the eight hero prefabs back to scale 1 (except Bone_Hips, " +
                    "which is the rig's own unit conversion) and deletes the proportions backup.\n\n" +
                    "Use this if the characters are carrying scales from an older version of the " +
                    "silhouette pass. Re-run '5. Re-proportion Characters' afterwards.",
                    "Reset", "Cancel"))
            {
                return;
            }

            int prefabsChanged = 0;
            int bonesReset = 0;

            foreach (var className in k_Classes)
            {
                foreach (var gender in k_Genders)
                {
                    string path = $"{k_CharacterFolder}/PlayerGraphics_{className}_{gender}.prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        continue;
                    }

                    int reset = ResetBonesUnder(prefab.transform);
                    if (reset > 0)
                    {
                        EditorUtility.SetDirty(prefab);
                        PrefabUtility.SavePrefabAsset(prefab);
                        prefabsChanged++;
                        bonesReset += reset;
                    }
                }
            }

            if (AssetDatabase.LoadAssetAtPath<SilhouetteBackup>(k_BackupPath) != null)
            {
                AssetDatabase.DeleteAsset(k_BackupPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Silhouette] Reset {bonesReset} bone(s) on {prefabsChanged} prefab(s) to stock scale " +
                      "and deleted the backup. Re-run '5. Re-proportion Characters', then " +
                      "'6. Add Shoulder Pads, Helmets And Wings' so the gear is re-sized against the clean rig.");
        }

        /// <summary>
        /// Sets every <c>Bone_*</c> transform under <paramref name="root"/> to scale 1, leaving
        /// <c>Bone_Hips</c> alone. Returns how many it actually had to change.
        /// </summary>
        static int ResetBonesUnder(Transform root)
        {
            int reset = 0;

            foreach (var bone in root.GetComponentsInChildren<Transform>(true))
            {
                if (!bone.name.StartsWith("Bone_") || bone.name == k_RigScaleBone)
                {
                    continue;
                }

                if (bone.localScale != Vector3.one)
                {
                    bone.localScale = Vector3.one;
                    reset++;
                }
            }

            return reset;
        }

        static int ApplyToPrefab(GameObject prefab, Dictionary<string, float> proportions, SilhouetteBackup backup)
        {
            var entry = FindOrCreateEntry(backup, prefab);
            int changed = 0;

            foreach (var pair in proportions)
            {
                var bone = FindBoneByName(prefab.transform, pair.Key);
                if (bone == null)
                {
                    continue;
                }

                // Record the stock scale once. Re-running must not capture the already-scaled
                // value as the "original" — that would make the revert a no-op and compound the
                // scaling on every run.
                if (!entry.BonePaths.Contains(pair.Key))
                {
                    entry.BonePaths.Add(pair.Key);
                    entry.OriginalScales.Add(bone.localScale);
                }

                // Applied against the recorded original rather than the current value, so running
                // this repeatedly converges on the same look instead of drifting.
                //
                // One uniform factor on all three axes. Anything else shears the skinned mesh at
                // every joint below this bone — see the note on k_ClassProportions.
                int index = entry.BonePaths.IndexOf(pair.Key);
                bone.localScale = entry.OriginalScales[index] * pair.Value;

                changed++;
            }

            return changed;
        }

        static SilhouetteBackup.Entry FindOrCreateEntry(SilhouetteBackup backup, GameObject prefab)
        {
            foreach (var existing in backup.Entries)
            {
                if (existing.Prefab == prefab)
                {
                    return existing;
                }
            }

            var entry = new SilhouetteBackup.Entry { Prefab = prefab };
            backup.Entries.Add(entry);
            return entry;
        }

        /// <summary>
        /// Depth-first search for a bone by name. The skeleton's depth varies between the class
        /// rigs (cloaked characters have extra bones), so a hard-coded transform path would break
        /// on some of them.
        /// </summary>
        static Transform FindBoneByName(Transform root, string boneName)
        {
            if (root.name == boneName)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindBoneByName(root.GetChild(i), boneName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        static SilhouetteBackup LoadOrCreateBackup()
        {
            var backup = AssetDatabase.LoadAssetAtPath<SilhouetteBackup>(k_BackupPath);
            if (backup != null)
            {
                return backup;
            }

            backup = ScriptableObject.CreateInstance<SilhouetteBackup>();
            AssetDatabase.CreateAsset(backup, k_BackupPath);
            return backup;
        }
    }
}
