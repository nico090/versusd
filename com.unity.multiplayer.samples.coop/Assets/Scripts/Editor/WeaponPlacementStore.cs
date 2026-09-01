using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.BossRoom.Editor
{
    /// <summary>
    /// Hand-authored weapon placement, captured from the Editor and treated as authoritative.
    /// </summary>
    /// <remarks>
    /// <para>The auto-fit in <see cref="CyberMedievalModelPass"/> exists to get a swapped weapon
    /// roughly into the hand without anyone having to eyeball it. It is not better than a person
    /// who can actually see the result — so once someone has placed the weapons properly, those
    /// values win, permanently, and the computed ones are never applied again.</para>
    ///
    /// <para>This is stored as an asset rather than as constants in code for a practical reason:
    /// the values only exist inside the character prefabs as nested-instance overrides, and
    /// transcribing forty numbers (ten weapons across position, rotation and scale) by hand is a
    /// transcription error waiting to happen. One menu item reads them out exactly.</para>
    /// </remarks>
    public class WeaponPlacement : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public GameObject CharacterPrefab;
            public string WeaponName;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        public List<Entry> Entries = new();

        public bool TryGet(GameObject characterPrefab, string weaponName, out Entry result)
        {
            foreach (var entry in Entries)
            {
                if (entry.CharacterPrefab == characterPrefab && entry.WeaponName == weaponName)
                {
                    result = entry;
                    return true;
                }
            }

            result = null;
            return false;
        }
    }

    public static class WeaponPlacementStore
    {
        public const string AssetPath = "Assets/Prefabs/CharGFX/WeaponPlacement.asset";

        const string k_CharacterFolder = "Assets/Prefabs/CharGFX/CharacterGraphics";

        static readonly string[] k_Classes = { "Tank", "Archer", "Mage", "Rogue" };
        static readonly string[] k_Genders = { "Boy", "Girl" };

        /// <summary>
        /// Reads every weapon's current transform out of the character prefabs and stores it.
        /// Run this after placing the weapons by hand; from then on the swap reproduces exactly
        /// these values instead of its own estimate.
        /// </summary>
        [MenuItem("Boss Room/Style/Capture Current Weapon Placement")]
        public static void Capture()
        {
            var placement = LoadOrCreate();

            // A re-capture replaces everything: the point is to record the current state of the
            // Editor, so keeping stale rows around would mean silently reapplying an older
            // placement for any weapon that had since been renamed or removed.
            placement.Entries.Clear();

            int captured = 0;

            foreach (var className in k_Classes)
            {
                foreach (var gender in k_Genders)
                {
                    string path = $"{k_CharacterFolder}/PlayerGraphics_{className}_{gender}.prefab";
                    var characterAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (characterAsset == null)
                    {
                        continue;
                    }

                    // The values live as nested-instance overrides, so the prefab has to be opened
                    // with its instances resolved — reading the asset directly gives the weapon
                    // prefab's own (overridden, therefore meaningless) transform.
                    var root = PrefabUtility.LoadPrefabContents(path);

                    foreach (var weaponName in CyberMedievalModelPass.WeaponPrefabNames)
                    {
                        var weapon = FindChildByName(root.transform, weaponName);
                        if (weapon == null)
                        {
                            continue;
                        }

                        placement.Entries.Add(new WeaponPlacement.Entry
                        {
                            CharacterPrefab = characterAsset,
                            WeaponName = weaponName,
                            Position = weapon.localPosition,
                            Rotation = weapon.localRotation,
                            Scale = weapon.localScale,
                        });
                        captured++;
                    }

                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            EditorUtility.SetDirty(placement);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Placement] Captured {captured} weapon transform(s) into {AssetPath}. " +
                      "The weapon swap will now reproduce these exactly and will no longer " +
                      "recompute placement. Delete that asset (or re-capture) to change that.");
        }

        /// <summary>Prints what is currently stored, without changing anything.</summary>
        [MenuItem("Boss Room/Style/Show Captured Weapon Placement")]
        public static void Show()
        {
            var placement = AssetDatabase.LoadAssetAtPath<WeaponPlacement>(AssetPath);
            if (placement == null || placement.Entries.Count == 0)
            {
                Debug.Log("[Placement] Nothing captured yet — the swap is using its computed fit.");
                return;
            }

            var report = new System.Text.StringBuilder($"[Placement] {placement.Entries.Count} captured transform(s)\n");
            foreach (var entry in placement.Entries)
            {
                string character = entry.CharacterPrefab != null ? entry.CharacterPrefab.name : "<missing>";
                report.AppendLine($"  {character} / {entry.WeaponName}");
                report.AppendLine($"    pos {entry.Position}  rot {entry.Rotation.eulerAngles}  scale {entry.Scale}");
            }

            Debug.Log(report.ToString());
        }

        public static WeaponPlacement Load() => AssetDatabase.LoadAssetAtPath<WeaponPlacement>(AssetPath);

        static WeaponPlacement LoadOrCreate()
        {
            var placement = Load();
            if (placement != null)
            {
                return placement;
            }

            placement = ScriptableObject.CreateInstance<WeaponPlacement>();
            AssetDatabase.CreateAsset(placement, AssetPath);
            return placement;
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
    }
}
