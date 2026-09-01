using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Unity.BossRoom.Editor
{
    /// <summary>
    /// Gives every hero the Tank's body proportions, without changing the size of anything they
    /// are holding.
    /// </summary>
    /// <remarks>
    /// <para><b>What "proportions" are here.</b> The four classes share one skeleton; what makes
    /// them read as different builds is the <c>localScale</c> written on its bones — head size,
    /// arm thickness, leg length. This copies the Tank's values onto the other three, bone by
    /// bone, matched by name.</para>
    ///
    /// <para><b>Why the hands are left alone.</b> The Tank's hand bones are not proportions, they
    /// are prop compensation: <c>Tank_Boy</c> carries a left hand at 2.38 while <c>Tank_Girl</c>
    /// carries 1.32 with the same shield. Copying that onto a Rogue would hand it one enormous
    /// glove. <see cref="k_ExcludedBones"/> is the list, and it is the first place to look if the
    /// result is nearly right.</para>
    ///
    /// <para><b>Why weapons need extra work.</b> A weapon is parented to the hand bone, so it
    /// inherits every scale between it and the hips. Changing an arm from 0.5 to the Tank's 0.7
    /// would silently enlarge the sword hanging off it by 40%. So each held prop's world scale is
    /// measured before the change and its <c>localScale</c> re-solved afterwards to land back on
    /// the same number. Worn gear — shoulder pads, the Archer's quiver — is deliberately <i>not</i>
    /// compensated: clothing is supposed to follow the body it is worn on.</para>
    ///
    /// <para><b>Why the backup is a file outside the project.</b> The proportions being replaced
    /// were tuned by hand and exist nowhere else. A ScriptableObject backup inside
    /// <c>Assets/</c> is not safe enough for that: a compile error in this assembly has, in this
    /// project, re-serialised exactly such an asset into an empty one. A plain JSON file next to
    /// the project cannot be touched by Unity at all.</para>
    /// </remarks>
    public static class BodyProportionPass
    {
        const string k_CharacterFolder = "Assets/Prefabs/CharGFX/CharacterGraphics";

        /// <summary>The build every other class is matched to.</summary>
        const string k_ReferenceClass = "Tank";

        static readonly string[] k_TargetClasses = { "Archer", "Mage", "Rogue" };
        static readonly string[] k_Genders = { "Boy", "Girl" };

        /// <summary>Only the rig is copied; class-specific holders keep whatever they have.</summary>
        const string k_BonePrefix = "Bone_";

        /// <summary>
        /// Bones whose scale is prop compensation rather than body shape. See the class remarks.
        /// </summary>
        static readonly HashSet<string> k_ExcludedBones = new HashSet<string>
        {
            "Bone_LeftHand",
            "Bone_RightHand",
        };

        /// <summary>Name fragments that mark an object as something the character is holding.</summary>
        static readonly string[] k_HeldPropMarkers = { "_Weapon_", "_weapon_", "_Shield_", "_shield_" };

        // ── Apply ─────────────────────────────────────────────────────────────────────────────

        [MenuItem("Boss Room/Style/Preview Body Proportions vs Tank")]
        public static void Preview()
        {
            var plan = BuildPlan();
            if (plan == null)
            {
                return;
            }

            // Same walk as the real thing, on prefab copies nothing is saved from, so the numbers
            // in the console are exactly the numbers that would be written.
            Run(plan, dryRun: true, backupPath: null);
        }

        [MenuItem("Boss Room/Style/Match Body Proportions To Tank")]
        public static void MatchToTank()
        {
            var plan = BuildPlan();
            if (plan == null)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Igualar proporciones al Tank",
                    $"Se van a reescribir las escalas de hueso de {plan.Count} prefab(s) para que " +
                    "coincidan con las del Tank.\n\n" +
                    "Las proporciones actuales están ajustadas a mano y no existen en ningún otro " +
                    "lado: antes de escribir nada se guarda un backup .json fuera del proyecto, y " +
                    "'Restore Body Proportions From Backup...' lo revierte.\n\n" +
                    "Las manos no se copian. Las armas y el escudo mantienen su tamaño actual; " +
                    "el gear que se lleva puesto (hombreras, carcaj) sigue al cuerpo.",
                    "Aplicar", "Cancelar"))
            {
                return;
            }

            string backupPath = WriteBackup(plan);
            if (backupPath == null)
            {
                return;
            }

            Run(plan, dryRun: false, backupPath: backupPath);
        }

        /// <summary>
        /// Walks the plan, rewriting bones and re-solving held props. With
        /// <paramref name="dryRun"/> the same walk runs but nothing is saved, so the report is a
        /// promise rather than a record.
        /// </summary>
        static void Run(List<Target> plan, bool dryRun, string backupPath)
        {
            var report = new StringBuilder();
            int prefabsChanged = 0;
            int bonesChanged = 0;
            int propsCompensated = 0;

            foreach (var target in plan)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(target.Path);
                if (prefab == null)
                {
                    continue;
                }

                // A preview works on a throwaway copy. Measuring what the props would do means
                // actually moving the bones, and doing that to the asset — even without saving it
                // — would leave it dirty for whatever calls SaveAssets next.
                var working = dryRun ? UnityEngine.Object.Instantiate(prefab) : prefab;

                // Every held prop's world scale, taken before a single bone moves.
                var propScales = new Dictionary<Transform, Vector3>();
                foreach (var prop in HeldProps(working.transform))
                {
                    propScales[prop] = AccumulatedScale(prop.parent, working.transform);
                }

                int changed = 0;
                int compensated = 0;
                report.AppendLine($"{Path.GetFileNameWithoutExtension(target.Path)}:");

                foreach (var bone in Bones(working.transform))
                {
                    if (!target.Reference.TryGetValue(bone.name, out var referenceScale))
                    {
                        continue;
                    }

                    if (Approximately(bone.localScale, referenceScale))
                    {
                        continue;
                    }

                    report.AppendLine($"    {bone.name,-20} {Format(bone.localScale)} -> {Format(referenceScale)}");
                    bone.localScale = referenceScale;
                    changed++;
                }

                foreach (var pair in propScales)
                {
                    var prop = pair.Key;
                    var before = pair.Value;
                    var after = AccumulatedScale(prop.parent, working.transform);

                    if (Approximately(before, after))
                    {
                        report.AppendLine($"    {prop.name,-20} sin cambio, no se toca");
                        continue;
                    }

                    var corrected = new Vector3(
                        prop.localScale.x * SafeRatio(before.x, after.x),
                        prop.localScale.y * SafeRatio(before.y, after.y),
                        prop.localScale.z * SafeRatio(before.z, after.z));

                    report.AppendLine($"    {prop.name,-20} {Format(prop.localScale)} -> {Format(corrected)}  " +
                                      "(compensado: mismo tamaño en pantalla)");
                    prop.localScale = corrected;
                    compensated++;
                }

                if (dryRun)
                {
                    UnityEngine.Object.DestroyImmediate(working);
                }

                if (changed == 0 && compensated == 0)
                {
                    continue;
                }

                prefabsChanged++;
                bonesChanged += changed;
                propsCompensated += compensated;

                if (dryRun)
                {
                    continue;
                }

                EditorUtility.SetDirty(prefab);
                PrefabUtility.SavePrefabAsset(prefab);
            }

            if (dryRun)
            {
                Debug.Log($"[Proporciones] VISTA PREVIA — no se escribió nada.\n" +
                          $"Cambiarían {prefabsChanged} prefab(s), {bonesChanged} hueso(s), " +
                          $"{propsCompensated} arma(s)/escudo(s).\n\n{report}");
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Proporciones] {prefabsChanged} prefab(s), {bonesChanged} hueso(s) igualados al " +
                      $"{k_ReferenceClass}, {propsCompensated} arma(s)/escudo(s) compensados.\n" +
                      $"Backup: {backupPath}\n\n{report}");
        }

        // ── Plan ──────────────────────────────────────────────────────────────────────────────

        /// <summary>One prefab to rewrite, with the bone scales it should end up with.</summary>
        class Target
        {
            public string Path;
            public Dictionary<string, Vector3> Reference;
        }

        /// <summary>
        /// Works out what would be written, and reads the reference builds. Returns null (having
        /// said why) if the Tank prefabs are not where they are expected to be.
        /// </summary>
        static List<Target> BuildPlan()
        {
            var plan = new List<Target>();

            foreach (var gender in k_Genders)
            {
                string referencePath = $"{k_CharacterFolder}/PlayerGraphics_{k_ReferenceClass}_{gender}.prefab";
                var reference = AssetDatabase.LoadAssetAtPath<GameObject>(referencePath);

                if (reference == null)
                {
                    EditorUtility.DisplayDialog("Igualar proporciones al Tank",
                        $"No encuentro el prefab de referencia:\n{referencePath}", "Cerrar");
                    return null;
                }

                // Matched per gender, so the Boys follow Tank_Boy and the Girls Tank_Girl. Today
                // the two agree on every bone that is copied; if they ever stop agreeing, this
                // still means what its name says.
                var scales = new Dictionary<string, Vector3>();
                foreach (var bone in Bones(reference.transform))
                {
                    scales[bone.name] = bone.localScale;
                }

                foreach (var className in k_TargetClasses)
                {
                    string path = $"{k_CharacterFolder}/PlayerGraphics_{className}_{gender}.prefab";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    {
                        Debug.LogWarning($"[Proporciones] No encontrado, se omite: {path}");
                        continue;
                    }

                    plan.Add(new Target { Path = path, Reference = scales });
                }
            }

            return plan;
        }

        // ── Backup and restore ────────────────────────────────────────────────────────────────

        [Serializable]
        class BackupEntry
        {
            public string PrefabPath;
            public List<string> ObjectPaths = new List<string>();
            public List<Vector3> Scales = new List<Vector3>();
        }

        [Serializable]
        class BackupFile
        {
            public string Created;
            public List<BackupEntry> Entries = new List<BackupEntry>();
        }

        /// <summary>
        /// Writes every scale this pass could touch — bones and held props — to a JSON file beside
        /// the project. Returns its path, or null if it could not be written, in which case
        /// nothing else should happen.
        /// </summary>
        static string WriteBackup(List<Target> plan)
        {
            var file = new BackupFile { Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };

            foreach (var target in plan)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(target.Path);
                if (prefab == null)
                {
                    continue;
                }

                var entry = new BackupEntry { PrefabPath = target.Path };

                foreach (var bone in Bones(prefab.transform))
                {
                    entry.ObjectPaths.Add(PathTo(bone, prefab.transform));
                    entry.Scales.Add(bone.localScale);
                }

                foreach (var prop in HeldProps(prefab.transform))
                {
                    entry.ObjectPaths.Add(PathTo(prop, prefab.transform));
                    entry.Scales.Add(prop.localScale);
                }

                file.Entries.Add(entry);
            }

            // One directory above the Unity project, which is where this project already keeps
            // its out-of-band backups.
            string directory = Directory.GetParent(Application.dataPath)?.Parent?.FullName
                               ?? Directory.GetParent(Application.dataPath)?.FullName;
            string path = Path.Combine(directory ?? ".",
                $"_proportions_backup_{DateTime.Now:yyyy-MM-dd_HHmm}.json");

            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(file, true));
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Igualar proporciones al Tank",
                    $"No se pudo escribir el backup, así que no se cambió nada:\n\n{exception.Message}", "Cerrar");
                return null;
            }

            return path;
        }

        [MenuItem("Boss Room/Style/Restore Body Proportions From Backup...")]
        public static void RestoreFromBackup()
        {
            string directory = Directory.GetParent(Application.dataPath)?.Parent?.FullName ?? ".";
            string path = EditorUtility.OpenFilePanel("Backup de proporciones", directory, "json");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            BackupFile file;
            try
            {
                file = JsonUtility.FromJson<BackupFile>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Restaurar proporciones", $"No se pudo leer:\n\n{exception.Message}", "Cerrar");
                return;
            }

            if (file?.Entries == null || file.Entries.Count == 0)
            {
                EditorUtility.DisplayDialog("Restaurar proporciones",
                    "El archivo no tiene entradas. No se cambió nada.", "Cerrar");
                return;
            }

            int restored = 0;

            foreach (var entry in file.Entries)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[Proporciones] Ya no existe, se omite: {entry.PrefabPath}");
                    continue;
                }

                bool touched = false;

                for (int i = 0; i < entry.ObjectPaths.Count && i < entry.Scales.Count; i++)
                {
                    var found = FindByPath(prefab.transform, entry.ObjectPaths[i]);
                    if (found == null || Approximately(found.localScale, entry.Scales[i]))
                    {
                        continue;
                    }

                    found.localScale = entry.Scales[i];
                    touched = true;
                }

                if (!touched)
                {
                    continue;
                }

                EditorUtility.SetDirty(prefab);
                PrefabUtility.SavePrefabAsset(prefab);
                restored++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Proporciones] Restaurados {restored} prefab(s) desde {path} (guardado {file.Created}).");
        }

        // ── Hierarchy helpers ─────────────────────────────────────────────────────────────────

        /// <summary>The rig bones this pass is allowed to write, in hierarchy order.</summary>
        static IEnumerable<Transform> Bones(Transform root)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith(k_BonePrefix, StringComparison.Ordinal))
                .Where(t => !k_ExcludedBones.Contains(t.name));
        }

        /// <summary>Everything the character is holding: weapons and the Tank's shield.</summary>
        static List<Transform> HeldProps(Transform root)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .Where(t => k_HeldPropMarkers.Any(marker => t.name.IndexOf(marker, StringComparison.Ordinal) >= 0))
                .ToList();
        }

        /// <summary>
        /// The scale a child inherits at <paramref name="node"/>: every localScale from there up
        /// to <paramref name="root"/>, multiplied componentwise.
        /// </summary>
        /// <remarks>
        /// Componentwise rather than <c>lossyScale</c> on purpose. They agree for this rig, but
        /// this one is explicit about where it stops — the prefab root — so a prop's correction
        /// never depends on what the prefab itself is scaled to when it is instantiated.
        /// </remarks>
        static Vector3 AccumulatedScale(Transform node, Transform root)
        {
            var scale = Vector3.one;

            for (var current = node; current != null && current != root.parent; current = current.parent)
            {
                scale = Vector3.Scale(scale, current.localScale);
            }

            return scale;
        }

        static string PathTo(Transform node, Transform root)
        {
            var parts = new List<string>();

            for (var current = node; current != null && current != root; current = current.parent)
            {
                parts.Add(current.name);
            }

            parts.Reverse();

            return string.Join("/", parts);
        }

        static Transform FindByPath(Transform root, string path)
        {
            // Find() would do, except sibling names in this rig are not unique across branches
            // ("Bone_Toe 1"), and a stored path is only meaningful walked from the root.
            var current = root;

            foreach (var part in path.Split('/'))
            {
                Transform next = null;

                foreach (Transform child in current)
                {
                    if (child.name == part)
                    {
                        next = child;
                        break;
                    }
                }

                if (next == null)
                {
                    return null;
                }

                current = next;
            }

            return current;
        }

        static float SafeRatio(float before, float after)
        {
            return Mathf.Approximately(after, 0f) ? 1f : before / after;
        }

        static bool Approximately(Vector3 a, Vector3 b)
        {
            return (a - b).sqrMagnitude < 1e-10f;
        }

        static string Format(Vector3 v)
        {
            return $"({v.x:0.####}, {v.y:0.####}, {v.z:0.####})";
        }
    }
}
