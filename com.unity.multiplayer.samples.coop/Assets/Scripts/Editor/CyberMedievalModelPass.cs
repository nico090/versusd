using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.BossRoom.Editor
{
    /// <summary>
    /// Backup of everything the model pass overwrites, so the swap is reversible.
    /// </summary>
    /// <remarks>
    /// Kept as a ScriptableObject with live object references rather than as a JSON file of GUIDs:
    /// Unity then maintains the references itself, so a revert still works after assets have been
    /// moved or re-imported. This matters more than usual here, because the swap is being tuned
    /// blind — the revert is the thing that makes "try a value, look, try another" safe.
    /// </remarks>
    public class ModelSwapBackup : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public GameObject TargetPrefab;
            public Mesh OriginalMesh;
            public Material[] OriginalMaterials;
            public Vector3 OriginalPosition;
            public Quaternion OriginalRotation;
            public Vector3 OriginalScale;
        }

        public List<Entry> Entries = new();

        /// <summary>
        /// A weapon's transform as stored INSIDE a character prefab.
        /// </summary>
        /// <remarks>
        /// The weapons are nested prefab instances inside each PlayerGraphics_* prefab, and every
        /// one of those instances overrides m_LocalPosition and m_LocalRotation (but NOT
        /// m_LocalScale). So the position written on the weapon's own prefab asset is dead data —
        /// the character prefab wins. This is the record of the values that actually matter.
        /// </remarks>
        [System.Serializable]
        public class InstanceEntry
        {
            public GameObject CharacterPrefab;
            public string WeaponName;
            public Vector3 OriginalPosition;
            public Quaternion OriginalRotation;
            public Vector3 OriginalScale;

            // What the tool itself last wrote here. Compared against what is found on the next
            // run so a hand adjustment can be told apart from the tool's own output — see
            // CyberMedievalModelPass.WasMovedByHand.
            public bool HasLastWritten;
            public Vector3 LastWrittenPosition;
            public Quaternion LastWrittenRotation;
            public Vector3 LastWrittenScale;
        }

        public List<InstanceEntry> InstanceEntries = new();
    }

    /// <summary>
    /// Replaces the heroes' weapons with the (unused) Viking Warrior Weapons Pack already in the
    /// project, and bolts procedurally generated neon trim onto them — the "medieval" and the
    /// "cyberpunk" halves of the restyle respectively.
    /// </summary>
    /// <remarks>
    /// <para><b>On the transform guesswork.</b> The two weapon sets use completely different pivot
    /// conventions: the shipped weapons sit at a large baked offset from their origin
    /// (Tank_Weapon_Boy is at 22.4, -3.6, 30.2) while the Viking prefabs have clean zeroed pivots.
    /// So the old transform cannot simply be kept. Rather than hard-coding magic numbers, the swap
    /// <i>fits</i> the new mesh to the old one: it matches the longest-axis length and re-centres
    /// the new bounds where the old bounds were. That gets the weapon roughly right without anyone
    /// having to eyeball it, and the per-entry multipliers below exist for the remaining nudge.</para>
    ///
    /// <para>Mesh bounds are readable without the "Read/Write Enabled" import flag, which is why
    /// the fit is done from bounds rather than from vertices.</para>
    /// </remarks>
    public static class CyberMedievalModelPass
    {
        const string k_VikingPrefabFolder = "Assets/PolyRonin/Viking Warrior Weapons Pack/Prefabs";
        const string k_CharacterGraphicsFolder = "Assets/Prefabs/CharGFX/CharacterGraphics";
        const string k_BackupPath = "Assets/Prefabs/CharGFX/ModelSwapBackup.asset";
        const string k_GeneratedFolder = "Assets/Prefabs/CharGFX/Generated";
        const string k_NeonChildName = "NeonTrim";

        /// <summary>
        /// One weapon swap. <see cref="ScaleMultiplier"/>, <see cref="ExtraEuler"/> and
        /// <see cref="ExtraOffset"/> are the tuning knobs — the auto-fit handles size and centring,
        /// these correct orientation and any remaining drift once somebody has actually looked at it.
        /// </summary>
        class SwapEntry
        {
            public string TargetPrefab;
            public string VikingPrefab;
            public string ClassName;
            public float ScaleMultiplier = 1f;
            public Vector3 ExtraEuler = Vector3.zero;
            public Vector3 ExtraOffset = Vector3.zero;
        }

        // Class -> weapon mapping. The pack has no staff, so the Mage gets the Spear, which is the
        // closest thing to a caster's haft; everything else maps cleanly.
        static readonly SwapEntry[] k_Swaps =
        {
            new() { TargetPrefab = "Tank_Weapon_Boy",   VikingPrefab = "Sword",     ClassName = "Tank" },
            new() { TargetPrefab = "Tank_Weapon_Girl",  VikingPrefab = "Sword",     ClassName = "Tank" },
            new() { TargetPrefab = "Tank_Shield_Boy",   VikingPrefab = "Shield1",   ClassName = "Tank" },
            new() { TargetPrefab = "Tank_Shield_Girl",  VikingPrefab = "Shield1",   ClassName = "Tank" },
            new() { TargetPrefab = "Rogue_Weapon_Boy",  VikingPrefab = "SeaxKnife", ClassName = "Rogue" },
            new() { TargetPrefab = "Rogue_Weapon_Girl", VikingPrefab = "SeaxKnife", ClassName = "Rogue" },
            new() { TargetPrefab = "Archer_weapon_Boy", VikingPrefab = "Bow",       ClassName = "Archer" },
            new() { TargetPrefab = "Archer_weapon_Girl",VikingPrefab = "Bow",       ClassName = "Archer" },
            new() { TargetPrefab = "Mage_Weapon_Boy",   VikingPrefab = "Spear",     ClassName = "Mage" },
            new() { TargetPrefab = "Mage_Weapon_Girl",  VikingPrefab = "Spear",     ClassName = "Mage" },
        };

        /// <summary>
        /// The weapon prefab names this pass manages. Exposed so the placement capture looks for
        /// exactly the same set — if the two lists could drift, a weapon would be swapped but its
        /// hand-placed transform never captured, and the next swap would silently move it again.
        /// </summary>
        public static IEnumerable<string> WeaponPrefabNames
        {
            get
            {
                foreach (var swap in k_Swaps)
                {
                    yield return swap.TargetPrefab;
                }
            }
        }

        // Accent colours come from HeroAccentPalette — one shared table, so a colour change can't
        // land on the armour but miss the weapons.

        /// <summary>How bright the neon reads. Above 1 so it blooms if post-processing is on.</summary>
        const float k_NeonIntensity = 4f;

        /// <summary>Neon bar thickness as a fraction of the weapon's longest axis.</summary>
        const float k_NeonThicknessFraction = 0.035f;

        // ── Menu items ────────────────────────────────────────────────────────────────────────

        [MenuItem("Boss Room/Style/3. Swap Weapons To Viking Models")]
        public static void SwapWeapons()
        {
            var backup = LoadOrCreateBackup();
            var computed = new Dictionary<string, TRS>();
            int swapped = 0;

            // Phase 1: mesh, material and scale go on the weapon's own prefab. Those three are not
            // overridden by the character prefabs, so editing them there propagates.
            foreach (var swap in k_Swaps)
            {
                if (ApplySwap(swap, backup, computed))
                {
                    swapped++;
                }
            }

            // Phase 2: position and rotation must be written onto the nested instance inside each
            // character prefab. Writing them on the weapon prefab alone does nothing — the
            // character prefab overrides both, which is why the weapons kept their stock placement
            // however many times the source prefab was corrected.
            int instancesFixed = ApplyToCharacterInstances(computed, backup);

            EditorUtility.SetDirty(backup);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            bool usingCaptured = WeaponPlacementStore.Load() != null;
            Debug.Log($"[Models] Swapped {swapped}/{k_Swaps.Length} weapon prefabs and wrote " +
                      $"{instancesFixed} nested instance transform(s). " +
                      (usingCaptured
                          ? "Placement came from the captured hand-placed values, not the auto-fit."
                          : "Placement came from the auto-fit — capture your own with 'Capture Current Weapon Placement'.") +
                      " Use 'Revert Weapon Swap' to undo everything.");
        }

        /// <summary>A position/rotation/scale triple to push onto a nested weapon instance.</summary>
        struct TRS
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        /// <summary>
        /// Writes each weapon's computed transform onto its instance inside every character prefab.
        /// </summary>
        /// <remarks>
        /// Goes through <see cref="PrefabUtility.LoadPrefabContents"/> rather than editing the
        /// asset in place: that loads the prefab into a temporary scene with its nested instances
        /// resolved, so assigning a transform there is recorded as a proper instance override when
        /// the contents are saved back.
        /// </remarks>
        static int ApplyToCharacterInstances(Dictionary<string, TRS> computed, ModelSwapBackup backup)
        {
            int fixedCount = 0;

            // If weapons have been placed by hand and captured, that is what gets written.
            var placement = WeaponPlacementStore.Load();

            // Anything left alone because it looks hand-adjusted, reported at the end.
            var skippedManual = new List<string>();

            foreach (var className in new[] { "Tank", "Archer", "Mage", "Rogue" })
            {
                foreach (var gender in new[] { "Boy", "Girl" })
                {
                    string path = $"{k_CharacterGraphicsFolder}/PlayerGraphics_{className}_{gender}.prefab";
                    var characterAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (characterAsset == null)
                    {
                        continue;
                    }

                    var root = PrefabUtility.LoadPrefabContents(path);
                    bool dirty = false;

                    foreach (var pair in computed)
                    {
                        var weapon = FindChildByName(root.transform, pair.Key);
                        if (weapon == null)
                        {
                            continue;
                        }

                        RecordInstanceOriginal(backup, characterAsset, pair.Key, weapon);

                        // Hand-placed values beat the auto-fit, always. The fit is a starting
                        // estimate made without being able to see the result; once somebody who
                        // could see it has placed the weapon, recomputing would be throwing away
                        // better information for worse.
                        if (placement != null &&
                            placement.TryGet(characterAsset, pair.Key, out var captured))
                        {
                            weapon.localPosition = captured.Position;
                            weapon.localRotation = captured.Rotation;
                            weapon.localScale = captured.Scale;
                            dirty = true;
                            fixedCount++;
                            continue;
                        }

                        // Nothing captured — so before overwriting, check whether this weapon has
                        // been moved since the last time this tool wrote it. If it has, somebody
                        // adjusted it by hand and never captured, and silently recomputing would
                        // destroy that work. (It already did once. Hence this check.)
                        if (WasMovedByHand(backup, characterAsset, pair.Key, weapon))
                        {
                            skippedManual.Add($"{characterAsset.name}/{pair.Key}");
                            continue;
                        }

                        weapon.localPosition = pair.Value.Position;
                        weapon.localRotation = pair.Value.Rotation;
                        weapon.localScale = pair.Value.Scale;
                        RecordLastWritten(backup, characterAsset, pair.Key, weapon);

                        dirty = true;
                        fixedCount++;
                    }

                    if (dirty)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }

                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            if (skippedManual.Count > 0)
            {
                Debug.LogWarning($"[Models] Left {skippedManual.Count} hand-adjusted weapon(s) alone: " +
                                 string.Join(", ", skippedManual) +
                                 ". Run 'Capture Current Weapon Placement' to make those permanent.");
            }

            return fixedCount;
        }

        /// <summary>
        /// True if the weapon's transform differs from what this tool last wrote — i.e. a person
        /// moved it afterwards.
        /// </summary>
        /// <remarks>
        /// The safety rule this enforces: <b>never overwrite a value you did not write.</b> The
        /// tool records what it puts there; if what it finds next time doesn't match, the
        /// difference came from a human and wins by default. Without this, re-running the swap
        /// after adjusting the weapons by hand destroys the adjustment with no warning — which is
        /// exactly what happened before this existed.
        /// </remarks>
        static bool WasMovedByHand(ModelSwapBackup backup, GameObject characterPrefab,
            string weaponName, Transform weapon)
        {
            foreach (var entry in backup.InstanceEntries)
            {
                if (entry.CharacterPrefab != characterPrefab || entry.WeaponName != weaponName)
                {
                    continue;
                }

                if (!entry.HasLastWritten)
                {
                    return false; // never written by us, so nothing to protect yet
                }

                const float tolerance = 0.01f;
                return Vector3.Distance(entry.LastWrittenPosition, weapon.localPosition) > tolerance
                       || Quaternion.Angle(entry.LastWrittenRotation, weapon.localRotation) > 0.5f
                       || Vector3.Distance(entry.LastWrittenScale, weapon.localScale) > tolerance;
            }

            return false;
        }

        static void RecordLastWritten(ModelSwapBackup backup, GameObject characterPrefab,
            string weaponName, Transform weapon)
        {
            foreach (var entry in backup.InstanceEntries)
            {
                if (entry.CharacterPrefab == characterPrefab && entry.WeaponName == weaponName)
                {
                    entry.LastWrittenPosition = weapon.localPosition;
                    entry.LastWrittenRotation = weapon.localRotation;
                    entry.LastWrittenScale = weapon.localScale;
                    entry.HasLastWritten = true;
                    return;
                }
            }
        }

        static void RecordInstanceOriginal(ModelSwapBackup backup, GameObject characterPrefab,
            string weaponName, Transform weapon)
        {
            foreach (var existing in backup.InstanceEntries)
            {
                if (existing.CharacterPrefab == characterPrefab && existing.WeaponName == weaponName)
                {
                    return; // keep the true original
                }
            }

            backup.InstanceEntries.Add(new ModelSwapBackup.InstanceEntry
            {
                CharacterPrefab = characterPrefab,
                WeaponName = weaponName,
                OriginalPosition = weapon.localPosition,
                OriginalRotation = weapon.localRotation,
                OriginalScale = weapon.localScale,
            });
        }

        static Transform FindChildByName(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindChildByName(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        [MenuItem("Boss Room/Style/4. Add Neon Trim To Weapons")]
        public static void AddNeonTrim()
        {
            EnsureFolder(k_GeneratedFolder);
            int decorated = 0;

            foreach (var swap in k_Swaps)
            {
                if (AddNeonTo(swap))
                {
                    decorated++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Models] Added neon trim to {decorated}/{k_Swaps.Length} weapons.");
        }

        /// <summary>
        /// Prints the numbers the fit is derived from, for every weapon, without changing anything.
        /// </summary>
        /// <remarks>
        /// Exists because this swap is being tuned by someone who can see the result reporting back
        /// to someone who cannot. Guessing from a symptom description costs a full revert/re-run
        /// cycle per attempt; the mesh bounds and pivots say outright where a weapon will land, so
        /// one paste of this log settles it.
        /// </remarks>
        [MenuItem("Boss Room/Style/Diagnose Weapon Fit")]
        public static void DiagnoseFit()
        {
            var report = new System.Text.StringBuilder("[Models] Weapon fit diagnosis\n");

            foreach (var swap in k_Swaps)
            {
                var target = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/CharGFX/{swap.TargetPrefab}.prefab");
                var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{k_VikingPrefabFolder}/{swap.VikingPrefab}.prefab");
                if (target == null || source == null)
                {
                    report.AppendLine($"  {swap.TargetPrefab}: MISSING prefab");
                    continue;
                }

                var currentMesh = target.GetComponent<MeshFilter>()?.sharedMesh;
                var vikingMesh = source.GetComponent<MeshFilter>()?.sharedMesh;

                // The stock mesh, if the swap has already run — that is the one whose numbers
                // explain where the original weapon sat.
                Mesh stockMesh = currentMesh;
                var backup = AssetDatabase.LoadAssetAtPath<ModelSwapBackup>(k_BackupPath);
                if (backup != null)
                {
                    foreach (var entry in backup.Entries)
                    {
                        if (entry.TargetPrefab == target && entry.OriginalMesh != null)
                        {
                            stockMesh = entry.OriginalMesh;
                            break;
                        }
                    }
                }

                report.AppendLine($"  {swap.TargetPrefab} -> {swap.VikingPrefab}");
                report.AppendLine($"    stock mesh  : {Describe(stockMesh)}");
                report.AppendLine($"    viking mesh : {Describe(vikingMesh)}");
                report.AppendLine($"    now at      : pos {target.transform.localPosition} scale {target.transform.localScale}");
            }

            Debug.Log(report.ToString());
        }

        static string Describe(Mesh mesh)
        {
            if (mesh == null)
            {
                return "<none>";
            }

            var bounds = mesh.bounds;
            return $"'{mesh.name}' size {bounds.size} centre {bounds.center} " +
                   $"(pivot is {(bounds.center.magnitude < bounds.size.magnitude * 0.1f ? "CENTRED on geometry" : "OFFSET from geometry")})";
        }

        [MenuItem("Boss Room/Style/Revert Weapon Swap")]
        public static void RevertSwap()
        {
            var backup = AssetDatabase.LoadAssetAtPath<ModelSwapBackup>(k_BackupPath);
            if (backup == null)
            {
                Debug.LogWarning("[Models] No backup asset found — nothing to revert.");
                return;
            }

            int reverted = 0;
            foreach (var entry in backup.Entries)
            {
                if (entry.TargetPrefab == null)
                {
                    continue;
                }

                var meshFilter = entry.TargetPrefab.GetComponent<MeshFilter>();
                var renderer = entry.TargetPrefab.GetComponent<MeshRenderer>();
                if (meshFilter == null || renderer == null)
                {
                    continue;
                }

                meshFilter.sharedMesh = entry.OriginalMesh;
                renderer.sharedMaterials = entry.OriginalMaterials;
                entry.TargetPrefab.transform.localPosition = entry.OriginalPosition;
                entry.TargetPrefab.transform.localRotation = entry.OriginalRotation;
                entry.TargetPrefab.transform.localScale = entry.OriginalScale;

                RemoveNeon(entry.TargetPrefab);

                EditorUtility.SetDirty(entry.TargetPrefab);
                PrefabUtility.SavePrefabAsset(entry.TargetPrefab);
                reverted++;
            }

            // The instance overrides inside the character prefabs have to be put back too, or the
            // weapons would keep the swapped placement while showing the stock mesh again.
            int instancesReverted = RevertCharacterInstances(backup);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Models] Reverted {reverted} weapon prefab(s) and {instancesReverted} nested instance(s).");
        }

        static int RevertCharacterInstances(ModelSwapBackup backup)
        {
            int reverted = 0;

            // Group by character prefab so each one is loaded and saved exactly once.
            var byCharacter = new Dictionary<GameObject, List<ModelSwapBackup.InstanceEntry>>();
            foreach (var entry in backup.InstanceEntries)
            {
                if (entry.CharacterPrefab == null)
                {
                    continue;
                }

                if (!byCharacter.TryGetValue(entry.CharacterPrefab, out var list))
                {
                    list = new List<ModelSwapBackup.InstanceEntry>();
                    byCharacter[entry.CharacterPrefab] = list;
                }

                list.Add(entry);
            }

            foreach (var pair in byCharacter)
            {
                string path = AssetDatabase.GetAssetPath(pair.Key);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                var root = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;

                foreach (var entry in pair.Value)
                {
                    var weapon = FindChildByName(root.transform, entry.WeaponName);
                    if (weapon == null)
                    {
                        continue;
                    }

                    weapon.localPosition = entry.OriginalPosition;
                    weapon.localRotation = entry.OriginalRotation;
                    weapon.localScale = entry.OriginalScale;
                    dirty = true;
                    reverted++;
                }

                if (dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }

                PrefabUtility.UnloadPrefabContents(root);
            }

            return reverted;
        }

        // ── Swapping ──────────────────────────────────────────────────────────────────────────

        static bool ApplySwap(SwapEntry swap, ModelSwapBackup backup, Dictionary<string, TRS> computed)
        {
            var target = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/CharGFX/{swap.TargetPrefab}.prefab");
            if (target == null)
            {
                Debug.LogWarning($"[Models] Target prefab not found: {swap.TargetPrefab}");
                return false;
            }

            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{k_VikingPrefabFolder}/{swap.VikingPrefab}.prefab");
            if (source == null)
            {
                Debug.LogWarning($"[Models] Viking prefab not found: {swap.VikingPrefab}");
                return false;
            }

            var targetFilter = target.GetComponent<MeshFilter>();
            var targetRenderer = target.GetComponent<MeshRenderer>();
            var sourceFilter = source.GetComponent<MeshFilter>();
            if (targetFilter == null || targetRenderer == null || sourceFilter == null)
            {
                Debug.LogWarning($"[Models] {swap.TargetPrefab} or {swap.VikingPrefab} has no MeshFilter/MeshRenderer.");
                return false;
            }

            var oldMesh = targetFilter.sharedMesh;
            var newMesh = sourceFilter.sharedMesh;
            if (oldMesh == null || newMesh == null)
            {
                Debug.LogWarning($"[Models] Missing mesh on {swap.TargetPrefab} or {swap.VikingPrefab}.");
                return false;
            }

            // ALREADY SWAPPED: refresh the material and stop. No mesh write, no transform write,
            // and — critically — no phase 2 entry, so the placement inside the character prefabs
            // is not touched either.
            //
            // The prefab itself is the witness here, not the backup. The backup can lie: a compile
            // error once broke this assembly, Unity re-serialised the backup as a missing-script
            // asset and emptied it, and the next run — with no memory that a swap had ever
            // happened — recorded the Viking sword as "the stock original", measured it against
            // itself, and compounded the scale (the Tank's shield came out at 210×). The mesh
            // already being the Viking mesh is proof of a previous run no wipe can erase, so it,
            // and not the backup, gates the transform writes.
            if (oldMesh == newMesh)
            {
                targetRenderer.sharedMaterial = BuildWeaponMaterial(swap.ClassName, source);
                return false;
            }

            // Record the original before touching anything, but only once — running the swap twice
            // must not overwrite the backup with the already-swapped state, which would make the
            // revert a no-op and lose the originals for good. (The already-swapped early-out above
            // means this can no longer record a swapped state even with an amnesiac backup.)
            RecordOriginal(backup, target, oldMesh, targetRenderer.sharedMaterials);

            // Everything below is computed from the RECORDED ORIGINAL, never from the prefab's
            // current values. Re-running the swap must converge on the same result rather than
            // compounding the previous run's scale and offset.
            var original = FindEntry(backup, target);
            Mesh stockMesh = original != null && original.OriginalMesh != null ? original.OriginalMesh : oldMesh;
            Vector3 stockScale = original != null ? original.OriginalScale : target.transform.localScale;
            Vector3 stockPosition = original != null ? original.OriginalPosition : target.transform.localPosition;
            Quaternion stockRotation = original != null ? original.OriginalRotation : target.transform.localRotation;

            // Size: match the stock weapon's overall length.
            float oldLongest = LongestAxis(stockMesh.bounds.size);
            float newLongest = LongestAxis(newMesh.bounds.size);
            float fitScale = newLongest > 0.0001f && oldLongest > 0.0001f
                ? oldLongest / newLongest
                : 1f;

            float finalScale = fitScale * swap.ScaleMultiplier;
            Vector3 newLocalScale = new Vector3(
                stockScale.x * finalScale,
                stockScale.y * finalScale,
                stockScale.z * finalScale);

            targetFilter.sharedMesh = newMesh;
            targetRenderer.sharedMaterial = BuildWeaponMaterial(swap.ClassName, source);

            Vector3 newPosition =
                ComputeGripAlignedPosition(stockMesh, stockScale, stockPosition, newMesh, newLocalScale)
                + swap.ExtraOffset;
            Quaternion newRotation = stockRotation * Quaternion.Euler(swap.ExtraEuler);

            target.transform.localScale = newLocalScale;
            target.transform.localRotation = newRotation;
            target.transform.localPosition = newPosition;

            // Handed to phase 2, which is where the position and rotation actually take effect.
            computed[swap.TargetPrefab] = new TRS
            {
                Position = newPosition,
                Rotation = newRotation,
                Scale = newLocalScale,
            };

            EditorUtility.SetDirty(target);
            PrefabUtility.SavePrefabAsset(target);
            return true;
        }

        /// <summary>
        /// Works out where to put the new weapon by putting its grip where the old weapon's grip
        /// was. Neither "keep the old position" nor "zero it" is right — the first inherits an
        /// offset that only made sense for the old mesh's geometry, the second assumes the hand
        /// socket's origin is exactly a grip. This assumes neither.
        /// </summary>
        /// <remarks>
        /// <para>The one thing we know for certain is that the shipped weapon was held correctly.
        /// So: take the two ends of the stock weapon along its long axis, transform them into
        /// hand-socket space, and the end nearer the socket origin is the grip — that is what
        /// "being held" means. Do the same on the new mesh to find its grip, then place the object
        /// so the two coincide.</para>
        ///
        /// <para>This is exact for anything held by one end (sword, knife, spear, bow). A shield is
        /// gripped in the middle of its back rather than at an end, so it may need a nudge via
        /// <see cref="SwapEntry.ExtraOffset"/> — that is the one shape this rule doesn't fit.</para>
        /// </remarks>
        static Vector3 ComputeGripAlignedPosition(Mesh stockMesh, Vector3 stockScale, Vector3 stockPosition,
            Mesh newMesh, Vector3 newScale)
        {
            Vector3 stockGripInSocket = stockPosition + Vector3.Scale(GripPointOf(stockMesh, stockScale, stockPosition), stockScale);
            Vector3 newGripLocal = GripPointOf(newMesh, newScale, Vector3.zero);
            return stockGripInSocket - Vector3.Scale(newGripLocal, newScale);
        }

        /// <summary>
        /// The end of a mesh's bounds, along its longest axis, that ends up closest to the hand
        /// socket's origin once placed — i.e. the end being held.
        /// </summary>
        static Vector3 GripPointOf(Mesh mesh, Vector3 scale, Vector3 position)
        {
            Bounds bounds = mesh.bounds;
            int axis = DominantAxis(bounds.size);

            Vector3 endA = bounds.center;
            endA[axis] = bounds.min[axis];
            Vector3 endB = bounds.center;
            endB[axis] = bounds.max[axis];

            float distanceA = (position + Vector3.Scale(endA, scale)).sqrMagnitude;
            float distanceB = (position + Vector3.Scale(endB, scale)).sqrMagnitude;

            return distanceA <= distanceB ? endA : endB;
        }

        static ModelSwapBackup.Entry FindEntry(ModelSwapBackup backup, GameObject target)
        {
            foreach (var entry in backup.Entries)
            {
                if (entry.TargetPrefab == target)
                {
                    return entry;
                }
            }

            return null;
        }

        static void RecordOriginal(ModelSwapBackup backup, GameObject target, Mesh mesh, Material[] materials)
        {
            foreach (var existing in backup.Entries)
            {
                if (existing.TargetPrefab == target)
                {
                    return; // already recorded; keep the true original
                }
            }

            backup.Entries.Add(new ModelSwapBackup.Entry
            {
                TargetPrefab = target,
                OriginalMesh = mesh,
                OriginalMaterials = materials,
                OriginalPosition = target.transform.localPosition,
                OriginalRotation = target.transform.localRotation,
                OriginalScale = target.transform.localScale,
            });
        }

        /// <summary>
        /// The Viking pack ships one shared colour material. Reusing it directly would make every
        /// class's weapon identical, so each class gets its own copy carrying its accent — the same
        /// colour coding the rest of the restyle uses.
        /// </summary>
        static Material BuildWeaponMaterial(string className, GameObject vikingSource)
        {
            EnsureFolder(k_GeneratedFolder);
            string path = $"{k_GeneratedFolder}/VikingWeapon_{className}.mat";

            // An existing material is RE-TINTED, not returned untouched. Returning early meant a
            // palette change never reached anything already generated — the armour would move to
            // the new colour and the weapons would silently keep the old one.
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            Material material;
            if (existing != null)
            {
                material = existing;
            }
            else
            {
                var sourceRenderer = vikingSource.GetComponent<MeshRenderer>();
                var template = sourceRenderer != null ? sourceRenderer.sharedMaterial : null;
                material = template != null
                    ? new Material(template)
                    : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            }

            Color accent = AccentFor(className);

            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetColor("_EmissionColor", accent * 0.8f);
            }

            // Dark metal, so the neon trim on top of it reads. Set to an ABSOLUTE value rather
            // than multiplying whatever is already there: multiplying compounds every time the
            // pass runs, so three runs would leave the weapons practically black.
            foreach (var colorProperty in new[] { "_Color", "_BaseColor" })
            {
                if (material.HasProperty(colorProperty))
                {
                    material.SetColor(colorProperty, k_WeaponMetalColor);
                    break;
                }
            }

            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        /// <summary>Base colour of the swapped weapons' metal. Dark enough for the trim to read.</summary>
        static readonly Color k_WeaponMetalColor = new(0.16f, 0.16f, 0.18f);

        // ── Procedural neon ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bolts a generated neon strip onto a weapon: two thin glowing bars running the length of
        /// the blade, one down each edge.
        /// </summary>
        /// <remarks>
        /// Generated from the weapon's bounds rather than authored, because the shape only has to
        /// follow the silhouette's dominant axis to read as an energised edge — and generating it
        /// means it automatically fits whichever mesh is currently on the prefab, including after
        /// a re-swap.
        /// </remarks>
        static bool AddNeonTo(SwapEntry swap)
        {
            var target = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/CharGFX/{swap.TargetPrefab}.prefab");
            if (target == null)
            {
                return false;
            }

            var filter = target.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return false;
            }

            // Re-running replaces the old trim rather than stacking a second copy on top.
            RemoveNeon(target);

            Bounds bounds = filter.sharedMesh.bounds;
            Mesh neonMesh = BuildNeonMesh(bounds, swap.TargetPrefab);
            if (neonMesh == null)
            {
                return false;
            }

            var child = new GameObject(k_NeonChildName);
            child.transform.SetParent(target.transform, false);
            child.layer = target.layer;

            child.AddComponent<MeshFilter>().sharedMesh = neonMesh;
            child.AddComponent<MeshRenderer>().sharedMaterial = BuildNeonMaterial(swap.ClassName);

            EditorUtility.SetDirty(target);
            PrefabUtility.SavePrefabAsset(target);
            return true;
        }

        static void RemoveNeon(GameObject target)
        {
            var existing = target.transform.Find(k_NeonChildName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject, true);
            }
        }

        /// <summary>
        /// Builds the trim mesh: two thin bars along the mesh's dominant axis, pushed out to the
        /// two extremes of its narrowest axis (i.e. down the edges rather than through the middle).
        /// </summary>
        static Mesh BuildNeonMesh(Bounds bounds, string assetName)
        {
            Vector3 size = bounds.size;
            int longAxis = DominantAxis(size);
            int thinAxis = NarrowestAxis(size);
            if (longAxis == thinAxis)
            {
                return null;
            }

            float length = size[longAxis] * 0.8f;      // stop short of the tip and the grip
            float thickness = size[longAxis] * k_NeonThicknessFraction;
            if (length <= 0.0001f || thickness <= 0.0001f)
            {
                return null;
            }

            // Bar dimensions: long on the dominant axis, thin on the other two.
            Vector3 barSize = Vector3.one * thickness;
            barSize[longAxis] = length;

            // Offset the pair to either side along the mesh's third (width) axis, so they sit on
            // the edges of the blade rather than inside it.
            int widthAxis = 3 - longAxis - thinAxis;
            float edgeOffset = size[widthAxis] * 0.5f;

            Vector3 offsetA = bounds.center;
            offsetA[widthAxis] += edgeOffset;
            Vector3 offsetB = bounds.center;
            offsetB[widthAxis] -= edgeOffset;

            var combined = new Mesh { name = $"Neon_{assetName}" };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            AppendBox(vertices, triangles, offsetA, barSize);
            AppendBox(vertices, triangles, offsetB, barSize);

            combined.SetVertices(vertices);
            combined.SetTriangles(triangles, 0);
            combined.RecalculateNormals();
            combined.RecalculateBounds();

            string path = $"{k_GeneratedFolder}/Neon_{assetName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                // Overwrite in place so any prefab already pointing at it picks up the new shape.
                existing.Clear();
                existing.SetVertices(vertices);
                existing.SetTriangles(triangles, 0);
                existing.RecalculateNormals();
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                return existing;
            }

            AssetDatabase.CreateAsset(combined, path);
            return combined;
        }

        static void AppendBox(List<Vector3> vertices, List<int> triangles, Vector3 centre, Vector3 size)
        {
            int baseIndex = vertices.Count;
            Vector3 half = size * 0.5f;

            // 8 corners.
            for (int i = 0; i < 8; i++)
            {
                vertices.Add(centre + new Vector3(
                    (i & 1) == 0 ? -half.x : half.x,
                    (i & 2) == 0 ? -half.y : half.y,
                    (i & 4) == 0 ? -half.z : half.z));
            }

            // 12 triangles, wound outwards.
            int[] boxTriangles =
            {
                0, 2, 1,  1, 2, 3,   // -Z
                4, 5, 6,  5, 7, 6,   // +Z
                0, 1, 4,  1, 5, 4,   // -Y
                2, 6, 3,  3, 6, 7,   // +Y
                0, 4, 2,  2, 4, 6,   // -X
                1, 3, 5,  3, 7, 5,   // +X
            };

            foreach (int index in boxTriangles)
            {
                triangles.Add(baseIndex + index);
            }
        }

        static Material BuildNeonMaterial(string className)
        {
            EnsureFolder(k_GeneratedFolder);
            string path = $"{k_GeneratedFolder}/Neon_{className}.mat";

            // Re-tinted if it already exists, so a palette change reaches it — see the note in
            // BuildWeaponMaterial.
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            // Unlit: the trim is meant to be a light source, not a surface that reacts to the
            // scene's lighting. A Lit material would go dim in shadow, which is exactly wrong.
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = existing != null ? existing : new Material(shader);

            Color accent = AccentFor(className) * k_NeonIntensity;

            foreach (var colorProperty in new[] { "_BaseColor", "_Color" })
            {
                if (material.HasProperty(colorProperty))
                {
                    material.SetColor(colorProperty, accent);
                    break;
                }
            }

            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", accent);
            }

            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────────

        static Color AccentFor(string className) => HeroAccentPalette.For(className);

        static float LongestAxis(Vector3 size) => Mathf.Max(size.x, Mathf.Max(size.y, size.z));

        static int DominantAxis(Vector3 size)
        {
            if (size.x >= size.y && size.x >= size.z) return 0;
            return size.y >= size.z ? 1 : 2;
        }

        static int NarrowestAxis(Vector3 size)
        {
            if (size.x <= size.y && size.x <= size.z) return 0;
            return size.y <= size.z ? 1 : 2;
        }

        static ModelSwapBackup LoadOrCreateBackup()
        {
            var backup = AssetDatabase.LoadAssetAtPath<ModelSwapBackup>(k_BackupPath);
            if (backup != null)
            {
                return backup;
            }

            backup = ScriptableObject.CreateInstance<ModelSwapBackup>();
            AssetDatabase.CreateAsset(backup, k_BackupPath);
            return backup;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            int lastSlash = folder.LastIndexOf('/');
            string parent = folder.Substring(0, lastSlash);
            string leaf = folder.Substring(lastSlash + 1);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
