using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.BossRoom.Editor
{
    /// <summary>
    /// Adds shoulder pads, helmets and wings to the heroes.
    /// </summary>
    /// <remarks>
    /// <para>Three different mechanisms, because the rig already provides two of them and not the
    /// third:</para>
    ///
    /// <para><b>Shoulder pads</b> ride slots the rig already has. <c>CharacterSwap.CharacterModelSet</c>
    /// exposes <c>shoulderLeft</c>/<c>shoulderRight</c>, and only the Tank fills them — the other
    /// three classes point at empty placeholder objects (<c>Empty_Gear_LS_Slot_Mage_Boy</c> and
    /// friends, which have no MeshFilter at all). So giving the Mage shoulder pads is filling in a
    /// slot the character was built to have, not bolting something on: the placement, the parenting
    /// and the show/hide-on-swap behaviour already work.</para>
    ///
    /// <para><b>Helmets</b> go on <c>Bone_Helmet</c>, a bone the skeleton carries specifically for
    /// one and which nothing currently uses.</para>
    ///
    /// <para><b>Wings</b> have no equivalent — there is no wing mesh anywhere in the project — so
    /// they are generated: angular hard-surface panels stepped outwards from the spine, which is a
    /// shape that reads at silhouette size and doesn't need to survive close inspection.</para>
    ///
    /// <para>Everything is added as a named child so it can be found and removed again; the revert
    /// is a deletion rather than a restore, which is why no backup asset is needed here.</para>
    /// </remarks>
    public static class HeroGearPass
    {
        const string k_CharacterFolder = "Assets/Prefabs/CharGFX/CharacterGraphics";
        const string k_GearFolder = "Assets/Prefabs/CharGFX";
        const string k_GeneratedFolder = "Assets/Prefabs/CharGFX/Generated";
        const string k_VikingPrefabFolder = "Assets/PolyRonin/Viking Warrior Weapons Pack/Prefabs";

        // Names used for everything this pass creates, so 'Remove' can find them and re-running
        // replaces rather than stacks.
        const string k_WingsName = "CyberWings";
        const string k_HelmetName = "CyberHelmet";
        const string k_ShoulderMeshHolder = "CyberShoulderPad";

        static readonly string[] k_Classes = { "Tank", "Archer", "Mage", "Rogue" };
        static readonly string[] k_Genders = { "Boy", "Girl" };

        /// <summary>
        /// Per-class wing size, as a multiple of the character's own height. The Mage gets the
        /// biggest because a caster's silhouette can carry them; the Rogue gets small swept ones
        /// because a stealth class with a huge wingspan is a contradiction.
        /// </summary>
        static readonly Dictionary<string, float> k_WingScale = new()
        {
            ["Tank"] = 0.55f,
            ["Archer"] = 0.5f,
            ["Mage"] = 0.75f,
            ["Rogue"] = 0.4f,
        };

        [MenuItem("Boss Room/Style/6. Add Shoulder Pads, Helmets And Wings")]
        public static void AddGear()
        {
            EnsureFolder(k_GeneratedFolder);

            int shoulders = 0, helmets = 0, wings = 0;

            foreach (var className in k_Classes)
            {
                foreach (var gender in k_Genders)
                {
                    string path = $"{k_CharacterFolder}/PlayerGraphics_{className}_{gender}.prefab";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    {
                        continue;
                    }

                    var root = PrefabUtility.LoadPrefabContents(path);
                    bool dirty = false;

                    if (AddShoulderPads(root, className, gender))
                    {
                        shoulders++;
                        dirty = true;
                    }

                    if (AddHelmet(root, className))
                    {
                        helmets++;
                        dirty = true;
                    }

                    if (AddWings(root, className))
                    {
                        wings++;
                        dirty = true;
                    }

                    if (dirty)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }

                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Gear] Shoulder pads on {shoulders} character(s), helmets on {helmets}, wings on {wings}. " +
                      "Use 'Remove Added Gear' to take it all off again.");
        }

        [MenuItem("Boss Room/Style/Remove Added Gear")]
        public static void RemoveGear()
        {
            int cleaned = 0;

            foreach (var className in k_Classes)
            {
                foreach (var gender in k_Genders)
                {
                    string path = $"{k_CharacterFolder}/PlayerGraphics_{className}_{gender}.prefab";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    {
                        continue;
                    }

                    var root = PrefabUtility.LoadPrefabContents(path);
                    bool dirty = false;

                    foreach (var name in new[] { k_WingsName, k_HelmetName, k_ShoulderMeshHolder })
                    {
                        // A name can occur more than once (two shoulders), so keep going until
                        // there are none left.
                        Transform found;
                        while ((found = FindChildByName(root.transform, name)) != null)
                        {
                            Object.DestroyImmediate(found.gameObject);
                            dirty = true;
                        }
                    }

                    if (dirty)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        cleaned++;
                    }

                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Gear] Removed added gear from {cleaned} character(s).");
        }

        // ── Shoulder pads ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fills the empty shoulder slots. The Tank's own pad meshes are reused rather than
        /// generated: they are already the right shape for this rig and already match the art
        /// style, which a procedural box would not.
        /// </summary>
        static bool AddShoulderPads(GameObject root, string className, string gender)
        {
            var padMaterial = BuildGearMaterial(className);
            bool added = false;

            // The empty placeholders are named per class and side; the Tank's are real gear
            // objects instead. Handle both by looking for either name.
            foreach (var side in new[] { "LS", "RS" })
            {
                var slot = FindChildByName(root.transform, $"Empty_Gear_{side}_Slot_{className}_{gender}");

                if (slot == null)
                {
                    continue; // this class already has real gear on that shoulder (the Tank)
                }

                // Left and right are separate meshes in the source FBX, so each side wears its
                // own. The previous version took the left mesh for both and mirrored the right one
                // with a negative X scale, which inverts the triangle winding: under a
                // single-sided shader that pad renders inside-out.
                var padMesh = LoadTankShoulderMesh(side, gender);
                if (padMesh == null)
                {
                    continue;
                }

                // A pad left behind by an earlier run is updated, not skipped. Skipping is what
                // made the size unfixable: the pads sitting in the prefabs were written before
                // this pass solved for scale at all, and every later run walked straight past them
                // because the object already existed.
                var pad = FindChildByName(slot, k_ShoulderMeshHolder);
                if (pad == null)
                {
                    pad = new GameObject(k_ShoulderMeshHolder).transform;
                    pad.SetParent(slot, false);
                }

                pad.gameObject.layer = slot.gameObject.layer;
                pad.localPosition = Vector3.zero;
                pad.localRotation = Quaternion.identity;
                pad.localScale = ShoulderPadScale(slot);

                GetOrAddComponent<MeshFilter>(pad.gameObject).sharedMesh = padMesh;
                GetOrAddComponent<MeshRenderer>(pad.gameObject).sharedMaterial = padMaterial;

                added = true;
            }

            return added;
        }

        /// <summary>
        /// The localScale that makes a pad hung in <paramref name="slot"/> come out the size the
        /// Tank's authored pads are.
        /// </summary>
        /// <remarks>
        /// <para>The Tank is the reference because its pads are the only ones an artist placed:
        /// they hang straight off the arm bone at localScale 1. The empty slots the other three
        /// classes expose are not neutral anchors — they are prefab instances carrying a scale of
        /// ~100, which is the rig-unit compensation the slot needs for itself and not for whatever
        /// is hung inside it. A mesh parented there at localScale 1 therefore came out about a
        /// hundred times too big, which is exactly what the pads in the prefabs were.</para>
        ///
        /// <para>Dividing that scale straight back out puts the pad's world scale where the Tank's
        /// is — the arm bone's — with no target size to guess and nothing to re-tune when the
        /// silhouette pass rescales the bones. It is componentwise on purpose: the slots do not
        /// all carry a uniform scale, and correcting on one axis stretches the pad on the
        /// others.</para>
        /// </remarks>
        static Vector3 ShoulderPadScale(Transform slot)
        {
            Vector3 slotScale = slot.localScale;

            return new Vector3(InvertScale(slotScale.x), InvertScale(slotScale.y), InvertScale(slotScale.z));
        }

        /// <summary>
        /// Target real-world size of a helmet, in rig units. This is the dial to turn if the
        /// helmets come out the wrong size; the shoulder pads no longer have one, because they are
        /// solved against the Tank's own pads instead of against a constant.
        /// </summary>
        const float k_HelmetWorldSize = 26f;

        /// <summary>
        /// The localScale that makes <paramref name="mesh"/> come out <paramref name="targetWorldSize"/>
        /// units across when parented under <paramref name="parent"/>.
        /// </summary>
        /// <remarks>
        /// Divides out the parent's lossyScale, which is the whole point: attaching art to a rig
        /// means landing on bones whose accumulated scale you do not control and cannot assume.
        /// Reading it is only possible because these passes open the prefab with
        /// PrefabUtility.LoadPrefabContents, so the hierarchy is real and lossyScale is meaningful.
        /// </remarks>
        static float FitScaleForWorldSize(Mesh mesh, Transform parent, float targetWorldSize)
        {
            Vector3 size = mesh.bounds.size;
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (longest < 0.0001f)
            {
                return 1f;
            }

            float inherited = Mathf.Abs(parent.lossyScale.x);
            if (inherited < 0.0001f)
            {
                inherited = 1f;
            }

            return targetWorldSize / (longest * inherited);
        }

        static Mesh LoadTankShoulderMesh(string side, string gender)
        {
            var donor = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{k_GearFolder}/Gear_{side}_Tank_ShoulderPad_{gender}.prefab");
            return donor != null ? donor.GetComponent<MeshFilter>()?.sharedMesh : null;
        }

        // ── Helmets ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Puts the Viking helmet on <c>Bone_Helmet</c>, sized against the head bone rather than
        /// against a guessed constant — the classes are being re-proportioned by the silhouette
        /// pass, so a fixed helmet scale would fit exactly one of them.
        /// </summary>
        static bool AddHelmet(GameObject root, string className)
        {
            var helmetBone = FindChildByName(root.transform, "Bone_Helmet");
            if (helmetBone == null)
            {
                return false;
            }

            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{k_VikingPrefabFolder}/Helmet.prefab");
            var helmetMesh = source != null ? source.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (helmetMesh == null)
            {
                return false;
            }

            // Refreshed rather than left alone when it is already there, for the same reason the
            // shoulder pads are: a helmet written by an older version of this pass has to be
            // reachable by re-running the menu item, or its scale can never be corrected.
            var helmet = FindChildByName(helmetBone, k_HelmetName);
            if (helmet == null)
            {
                helmet = new GameObject(k_HelmetName).transform;
                helmet.SetParent(helmetBone, false);
                helmet.localPosition = Vector3.zero;
                helmet.localRotation = Quaternion.identity;
            }

            helmet.gameObject.layer = helmetBone.gameObject.layer;

            GetOrAddComponent<MeshFilter>(helmet.gameObject).sharedMesh = helmetMesh;
            GetOrAddComponent<MeshRenderer>(helmet.gameObject).sharedMaterial = BuildGearMaterial(className);

            // Same inherited-scale problem as the shoulder pads: the Viking pack is authored at
            // ~1 unit and the rig works at ~100, and Bone_Helmet carries whatever scale the head
            // chain has accumulated (including anything the silhouette pass did to Bone_Head).
            // Solving against lossyScale is what makes one number work for all eight characters.
            helmet.localScale = Vector3.one *
                FitScaleForWorldSize(helmetMesh, helmetBone, k_HelmetWorldSize);

            return true;
        }

        // ── Wings ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Generates a pair of angular wings on the spine.
        /// </summary>
        /// <remarks>
        /// Built as three stepped, tapering panels per side rather than as a feathered shape:
        /// straight-edged plates are what a low-poly cyberpunk silhouette wants, they cost a few
        /// dozen triangles, and — the practical reason — a hard-surface fan is a shape that still
        /// reads correctly when it turns out to be the wrong size, which a detailed wing would not.
        /// </remarks>
        static bool AddWings(GameObject root, string className)
        {
            var spine = FindChildByName(root.transform, "Bone_Spine");
            if (spine == null)
            {
                return false;
            }

            float span = 100f * (k_WingScale.TryGetValue(className, out var s) ? s : 0.5f);
            var mesh = BuildWingMesh(span, className);

            // Rebuilt in place when it already exists, so editing k_WingScale and re-running
            // actually changes the wings instead of silently doing nothing.
            var wings = FindChildByName(spine, k_WingsName);
            if (wings == null)
            {
                wings = new GameObject(k_WingsName).transform;
                wings.SetParent(spine, false);
            }

            wings.gameObject.layer = spine.gameObject.layer;
            wings.localPosition = Vector3.zero;
            wings.localRotation = Quaternion.identity;
            wings.localScale = Vector3.one;

            GetOrAddComponent<MeshFilter>(wings.gameObject).sharedMesh = mesh;
            GetOrAddComponent<MeshRenderer>(wings.gameObject).sharedMaterial = BuildNeonMaterial(className);

            return true;
        }

        static Mesh BuildWingMesh(float span, string className)
        {
            string path = $"{k_GeneratedFolder}/Wings_{className}.asset";

            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            // Three panels per side, each shorter and steeper than the last, swept backwards.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int panel = 0; panel < 3; panel++)
                {
                    float t = panel / 3f;
                    float inner = span * (0.15f + t * 0.5f);
                    float outer = span * (0.45f + t * 0.55f);
                    float rise = span * (0.35f - t * 0.22f);
                    float drop = span * (t * 0.3f);
                    float sweep = -span * (0.1f + t * 0.25f);   // trailing edge pulled backwards

                    int baseIndex = vertices.Count;

                    vertices.Add(new Vector3(side * inner * 0.35f, rise * 0.2f, 0f));
                    vertices.Add(new Vector3(side * inner, rise, sweep * 0.4f));
                    vertices.Add(new Vector3(side * outer, rise - drop, sweep));
                    vertices.Add(new Vector3(side * inner * 0.5f, -drop * 0.5f, sweep * 0.6f));

                    // Wound so the visible face points away from the body on each side.
                    if (side > 0)
                    {
                        triangles.AddRange(new[]
                        {
                            baseIndex, baseIndex + 1, baseIndex + 2,
                            baseIndex, baseIndex + 2, baseIndex + 3,
                        });
                    }
                    else
                    {
                        triangles.AddRange(new[]
                        {
                            baseIndex, baseIndex + 2, baseIndex + 1,
                            baseIndex, baseIndex + 3, baseIndex + 2,
                        });
                    }
                }
            }

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var mesh = existing != null ? existing : new Mesh { name = $"Wings_{className}" };

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }

            return mesh;
        }

        // ── Materials ─────────────────────────────────────────────────────────────────────────

        /// <summary>Dark metal with a class-tinted glow, for helmets and pads.</summary>
        static Material BuildGearMaterial(string className)
        {
            EnsureFolder(k_GeneratedFolder);
            string path = $"{k_GeneratedFolder}/Gear_{className}.mat";

            // Re-tinted rather than returned as-is, so changing HeroAccentPalette actually reaches
            // gear that was already generated.
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            var material = existing != null ? existing : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Color accent = HeroAccentPalette.For(className);

            foreach (var colorProperty in new[] { "_BaseColor", "_Color" })
            {
                if (material.HasProperty(colorProperty))
                {
                    material.SetColor(colorProperty, new Color(0.09f, 0.09f, 0.11f));
                    break;
                }
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0.85f);
            }

            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetColor("_EmissionColor", accent * 1.2f);
            }

            SaveMaterial(material, existing, path);
            return material;
        }

        /// <summary>Creates the asset on first use, marks it dirty on every later re-tint.</summary>
        static void SaveMaterial(Material material, Material existing, string path)
        {
            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }
        }

        /// <summary>Unlit glow for the wings — they are meant to be light, not lit surfaces.</summary>
        static Material BuildNeonMaterial(string className)
        {
            EnsureFolder(k_GeneratedFolder);
            string path = $"{k_GeneratedFolder}/Wings_{className}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = existing != null ? existing : new Material(shader);
            Color accent = HeroAccentPalette.For(className) * 3.5f;

            foreach (var colorProperty in new[] { "_BaseColor", "_Color" })
            {
                if (material.HasProperty(colorProperty))
                {
                    material.SetColor(colorProperty, accent);
                    break;
                }
            }

            // Wings are flat panels, so they'd vanish edge-on when viewed from behind without
            // double-sided rendering.
            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", 0f);
            }

            SaveMaterial(material, existing, path);
            return material;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The component of type <typeparamref name="T"/> on <paramref name="target"/>, added if it
        /// isn't there yet. Every piece of gear this pass writes is re-applied on top of whatever a
        /// previous run left, so nothing here may assume it is building on a bare GameObject.
        /// </summary>
        static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        /// <summary>Reciprocal of a scale factor, guarded against a degenerate axis.</summary>
        static float InvertScale(float value)
        {
            return Mathf.Abs(value) < 0.0001f ? 1f : 1f / value;
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

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            int lastSlash = folder.LastIndexOf('/');
            AssetDatabase.CreateFolder(folder.Substring(0, lastSlash), folder.Substring(lastSlash + 1));
        }
    }
}
