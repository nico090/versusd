using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Unity.BossRoom.Editor
{
    /// <summary>
    /// Replaces the stock heads — heroes and enemies alike — with KayKit heads (CC0).
    /// </summary>
    /// <remarks>
    /// <para>This is cheap for one reason: the characters are already modular. A graphics prefab
    /// owns no renderer of its own — it is a skeleton plus a set of nested prefabs, and of those
    /// only the torso is a <see cref="SkinnedMeshRenderer"/>. Head, ears, hair, eyes and mouth are
    /// rigid meshes hanging off a <c>Head_Parent_*</c> node, which itself hangs off
    /// <c>Bone_Head</c>. Swapping them cannot break an animation clip, because the clips drive
    /// bones and never touch these meshes. The imps and the boss are built exactly the same way as
    /// the heroes, which is why one pass covers both.</para>
    ///
    /// <para>The KayKit heads arrive the same way. They ship as skinned meshes, but every vertex is
    /// weighted 1.0 to a single <c>head</c> joint — so they are rigid in all but name. The
    /// extraction step (a Python pass over the CC0 <c>.glb</c>) already multiplied the vertices by
    /// that joint's inverse bind matrix, which leaves them in bone-local space. That is why this
    /// pass only has to parent, align and scale, never re-skin.</para>
    ///
    /// <para>The stock pieces are deactivated rather than deleted, so the revert is a re-enable
    /// plus a deletion of the added child — no backup asset to be emptied by a broken assembly.</para>
    /// </remarks>
    public static class KayKitHeadPass
    {
        const string k_HeroFolder = "Assets/Prefabs/CharGFX/CharacterGraphics";
        const string k_SelectFolder = "Assets/Prefabs/CharGFX/CharacterGraphics/CharacterSelect";
        const string k_EnemyFolder = "Assets/Prefabs/CharGFX";
        const string k_HeadDataFolder = "Assets/ThirdParty/KayKit/Heads";
        const string k_TextureFolder = "Assets/ThirdParty/KayKit/Textures";
        const string k_GeneratedFolder = "Assets/Prefabs/CharGFX/Generated";

        /// <summary>Name of the child this pass adds, so a re-run replaces instead of stacking.</summary>
        const string k_HeadName = "KayKitHead";

        /// <summary>
        /// Stock pieces the new head stands in for.
        /// </summary>
        /// <remarks>
        /// All of them, not just the head. Ears and hair share the head's material and would float
        /// beside a head that brings its own. Eyes and mouth are the less obvious ones: they are
        /// flat quads carrying the <c>*_Eyes_sheet</c> / <c>*_Mouth_sheet</c> textures, positioned
        /// for the stock face — and the KayKit heads model and paint their own features, so leaving
        /// the sheets on hangs a second pair of eyes in front of the new face.
        /// </remarks>
        static readonly string[] k_ReplacedParts = { "Head", "Ears", "Hair", "Eyes", "Mouth" };

        /// <summary>
        /// One prefab to operate on, and everything needed to build its head inside it.
        /// </summary>
        /// <remarks>
        /// <para><see cref="Pieces"/> is a list because a KayKit head is rarely one mesh. The
        /// skeletons carry a detached jaw as its own object — leave it out and the skull renders
        /// with no lower jaw — and the helmets, hats and hoods are separate again. The first entry
        /// is always the head proper: it is what the fit measures against, so a tall hat scales the
        /// head down if it is allowed into that calculation.</para>
        ///
        /// <para>Enemies need the head parent named explicitly rather than derived.
        /// <c>BossGraphics</c> carries a complete second head rig — <c>Head_Parent_Imp</c> beside
        /// its own <c>Head_Parent_Boss</c> — so a pass that took the first <c>Head_Parent_*</c> it
        /// found would swap the wrong one.</para>
        /// </remarks>
        readonly struct HeadTarget
        {
            public readonly string PrefabPath;
            public readonly string HeadParent;
            public readonly string PartPrefix;
            public readonly string PartSuffix;
            public readonly string[] Pieces;
            public readonly string Atlas;
            public readonly string Label;

            public HeadTarget(string prefabPath, string headParent, string partPrefix,
                string partSuffix, string[] pieces, string atlas, string label)
            {
                PrefabPath = prefabPath;
                HeadParent = headParent;
                PartPrefix = partPrefix;
                PartSuffix = partSuffix;
                Pieces = pieces;
                Atlas = atlas;
                Label = label;
            }

            public string PartName(string part) => $"{PartPrefix}_{part}{PartSuffix}";
        }

        /// <summary>
        /// Every hero variant gets its own face. KayKit only ships nine distinct heads, which is
        /// two short of the eleven characters here, so the Boy/Girl pairs are split by headwear
        /// rather than by a different head: same donor, one bare and one covered. That keeps the
        /// class readable at silhouette size — the Tank is still a knight, the Archer still a
        /// barbarian — while making the two variants unmistakably different characters. The Rogue
        /// is the exception and gets it for free, because the pack ships a hooded second head.
        /// </summary>
        static readonly (string Class, string Gender, string[] Pieces, string Atlas)[] k_HeroHeads =
        {
            ("Tank", "Boy", new[] { "Knight_Head", "Knight_Helmet" }, "kaykit_knight"),
            ("Tank", "Girl", new[] { "Knight_Head" }, "kaykit_knight"),
            ("Archer", "Boy", new[] { "Barbarian_Head", "Barbarian_Hat" }, "kaykit_barbarian"),
            ("Archer", "Girl", new[] { "Barbarian_Head" }, "kaykit_barbarian"),
            ("Mage", "Boy", new[] { "Mage_Head", "Mage_Hat" }, "kaykit_mage"),
            ("Mage", "Girl", new[] { "Mage_Head" }, "kaykit_mage"),
            ("Rogue", "Boy", new[] { "Rogue_Head" }, "kaykit_rogue"),
            ("Rogue", "Girl", new[] { "Rogue_Head_Hooded" }, "kaykit_rogue"),
        };

        /// <summary>
        /// The enemies. Skulls across the board, distinguished by rank: the Minion's bare skull for
        /// the common imp, a hood for the vandal, a horned helmet for the boss.
        /// </summary>
        static readonly HeadTarget[] k_EnemyTargets =
        {
            new($"{k_EnemyFolder}/ImpGraphics.prefab", "Head_Parent_Imp", "Imp", "",
                new[] { "Skeleton_Minion_Head", "Skeleton_Minion_Jaw" }, "kaykit_skeleton", "Imp"),
            new($"{k_EnemyFolder}/VandalImpGraphics.prefab", "Head_Parent_Imp", "Imp", "",
                new[] { "Skeleton_Rogue_Head", "Skeleton_Rogue_Jaw", "Skeleton_Rogue_Hood" },
                "kaykit_skeleton", "VandalImp"),
            new($"{k_EnemyFolder}/BossGraphics.prefab", "Head_Parent_Boss", "Boss", "",
                new[] { "Skeleton_Warrior_Head", "Skeleton_Warrior_Jaw", "Skeleton_Warrior_Helmet" },
                "kaykit_skeleton", "Boss"),
        };

        // ── Menu ──────────────────────────────────────────────────────────────────────────────

        [MenuItem("Boss Room/Style/8. Swap Heads To KayKit Models")]
        public static void Apply()
        {
            int changed = ForEachTarget(AllTargets(), ApplyToPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[KayKitHeads] Swapped heads on {changed} prefab(s).");
        }

        [MenuItem("Boss Room/Style/Revert KayKit Heads")]
        public static void Revert()
        {
            int changed = ForEachTarget(AllTargets(), (root, target) =>
            {
                bool touched = false;

                var added = FindChildByName(root.transform, k_HeadName);
                if (added != null)
                {
                    UnityEngine.Object.DestroyImmediate(added.gameObject);
                    touched = true;
                }

                foreach (var part in k_ReplacedParts)
                {
                    var stock = FindPart(root, target, part);
                    if (stock != null && !stock.gameObject.activeSelf)
                    {
                        stock.gameObject.SetActive(true);
                        touched = true;
                    }
                }

                return touched;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[KayKitHeads] Reverted {changed} prefab(s).");
        }

        // ── Targets ───────────────────────────────────────────────────────────────────────────

        static IEnumerable<HeadTarget> AllTargets()
        {
            foreach (var hero in k_HeroHeads)
            {
                foreach (var folder in new[] { k_HeroFolder, k_SelectFolder })
                {
                    string suffix = folder == k_SelectFolder ? "_CharacterSelect" : string.Empty;
                    yield return new HeadTarget(
                        $"{folder}/PlayerGraphics_{hero.Class}_{hero.Gender}{suffix}.prefab",
                        $"Head_Parent_{hero.Class}_{hero.Gender}",
                        hero.Class,
                        $"_{hero.Gender}",
                        hero.Pieces,
                        hero.Atlas,
                        $"{hero.Class}_{hero.Gender}");
                }
            }

            foreach (var target in k_EnemyTargets)
            {
                yield return target;
            }
        }

        /// <summary>
        /// Runs <paramref name="action"/> over every target prefab that exists, saving only the
        /// ones the action reports as changed.
        /// </summary>
        static int ForEachTarget(IEnumerable<HeadTarget> targets, Func<GameObject, HeadTarget, bool> action)
        {
            int changed = 0;

            foreach (var target in targets)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(target.PrefabPath) == null)
                {
                    continue;
                }

                var root = PrefabUtility.LoadPrefabContents(target.PrefabPath);
                try
                {
                    if (action(root, target))
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, target.PrefabPath);
                        changed++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return changed;
        }

        /// <summary>
        /// One stock piece, looked up under the target's own head parent rather than anywhere in
        /// the prefab — <c>BossGraphics</c> holds two full head rigs and a global search would
        /// happily return the imp's mouth while working on the boss.
        /// </summary>
        static Transform FindPart(GameObject root, HeadTarget target, string part)
        {
            var parent = FindChildByName(root.transform, target.HeadParent);
            return parent != null ? FindChildByName(parent, target.PartName(part)) : null;
        }

        // ── Pass ──────────────────────────────────────────────────────────────────────────────

        static bool ApplyToPrefab(GameObject root, HeadTarget target)
        {
            var parent = FindChildByName(root.transform, target.HeadParent);
            if (parent == null)
            {
                Debug.LogWarning($"[KayKitHeads] {target.Label}: no {target.HeadParent} — skipped.");
                return false;
            }

            // A previous run left the stock pieces disabled, and a disabled renderer reports no
            // world bounds — which is exactly what the fit measures against. So everything goes
            // back on before measuring and off again afterwards, making a re-run measure the same
            // thing the first run did instead of fitting against an empty box.
            SetStockPartsActive(root, target, true);

            var stockHead = FindPart(root, target, "Head");
            var stockRenderer = stockHead != null ? stockHead.GetComponent<MeshRenderer>() : null;
            var stockFilter = stockHead != null ? stockHead.GetComponent<MeshFilter>() : null;
            if (stockRenderer == null || stockFilter == null || stockFilter.sharedMesh == null)
            {
                Debug.LogWarning($"[KayKitHeads] {target.Label}: no stock head mesh to fit against — skipped.");
                return false;
            }

            var material = BuildHeadMaterial(target, stockRenderer);
            if (material == null)
            {
                return false;
            }

            var meshes = new List<Mesh>(target.Pieces.Length);
            foreach (var piece in target.Pieces)
            {
                var pieceMesh = BuildHeadMesh(piece);
                if (pieceMesh == null)
                {
                    return false;
                }

                meshes.Add(pieceMesh);
            }

            // Rebuilt from scratch on every run so a re-run after a proportion change re-fits.
            var existing = FindChildByName(parent, k_HeadName);
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

            // A holder, with one child per piece. The pieces were extracted into a single shared
            // space, so they only stay aligned to each other if one transform moves all of them —
            // hence fitting the holder rather than each piece.
            var head = new GameObject(k_HeadName);
            head.transform.SetParent(parent, false);

            for (int i = 0; i < meshes.Count; i++)
            {
                var piece = new GameObject(target.Pieces[i]);
                piece.transform.SetParent(head.transform, false);
                piece.AddComponent<MeshFilter>().sharedMesh = meshes[i];

                var pieceRenderer = piece.AddComponent<MeshRenderer>();
                pieceRenderer.sharedMaterial = material;
                // The stock pieces cast shadows onto the body; matching keeps the silhouette read.
                pieceRenderer.shadowCastingMode = stockRenderer.shadowCastingMode;
            }

            // Measured against the head proper, never the hat: a tall hat in the bounds would
            // scale the whole face down to make room for it.
            FitToStockHead(head.transform, meshes[0], stockRenderer, root.transform);

            SetStockPartsActive(root, target, false);

            return true;
        }

        /// <summary>Turns the stock head pieces on or off together.</summary>
        static void SetStockPartsActive(GameObject root, HeadTarget target, bool active)
        {
            foreach (var part in k_ReplacedParts)
            {
                var stock = FindPart(root, target, part);
                if (stock != null)
                {
                    stock.gameObject.SetActive(active);
                }
            }
        }

        /// <summary>
        /// Aligns, sizes and centres the donor head onto the volume the stock head occupied.
        /// </summary>
        /// <remarks>
        /// <para>All of this happens in world space, which is the only reason it is reliable. The
        /// obvious approach — matching local positions and mesh-space bounds — fails on this rig,
        /// because the bone chain is not axis-aligned: <c>Bone_Head</c> sits at roughly
        /// <c>(5, -34, 89)</c> degrees, Maya-style, with its local +X running up the skeleton. The
        /// stock head meshes are authored with that orientation already baked into their vertices,
        /// so they sit at identity under <c>Head_Parent_*</c> and look correct. The KayKit meshes
        /// carry no such bake — they come out of the extraction cleanly Y-up — so parenting them
        /// and leaving the local rotation at identity hands them the bone's twist and lays the head
        /// on its side.</para>
        ///
        /// <para>Hence: re-align to the character rather than to the bone, then match the stock
        /// renderer's <b>world</b> bounds, which are by definition where the old head actually
        /// appeared. Nothing here has to know what convention the rig uses.</para>
        ///
        /// <para>Height is the axis that matters — a head matching the old one's height reads as
        /// the same character size even when proportioned differently — so one uniform factor is
        /// taken from Y rather than fitting each axis, which would squash the donor back into the
        /// stock head's shape and undo the point of swapping it.</para>
        /// </remarks>
        static void FitToStockHead(Transform head, Mesh donor, Renderer stockRenderer, Transform root)
        {
            // Cancel whatever the bone chain accumulated; the donor is authored in character space.
            head.rotation = root.rotation;

            Bounds fresh = donor.bounds;
            Bounds stock = stockRenderer.bounds;

            // LoadPrefabContents builds its hierarchy in a preview scene, and a renderer that never
            // gets culled there can report an empty box. Falling back to the mesh bounds pushed
            // through the stock transform keeps the fit honest instead of scaling the head to zero.
            if (stock.size.y < 0.0001f)
            {
                var stockFilter = stockRenderer.GetComponent<MeshFilter>();
                if (stockFilter != null && stockFilter.sharedMesh != null)
                {
                    Bounds local = stockFilter.sharedMesh.bounds;
                    stock = new Bounds(
                        stockRenderer.transform.TransformPoint(local.center),
                        Vector3.Scale(local.size, stockRenderer.transform.lossyScale));
                }
            }

            float worldScale = fresh.size.y > 0.0001f && stock.size.y > 0.0001f
                ? stock.size.y / fresh.size.y
                : 1f;

            Vector3 parentScale = head.parent != null ? head.parent.lossyScale : Vector3.one;
            head.localScale = new Vector3(
                worldScale / SafeScale(parentScale.x),
                worldScale / SafeScale(parentScale.y),
                worldScale / SafeScale(parentScale.z));

            // Centres last: the offset is only meaningful once rotation and scale are settled.
            head.position += stock.center - head.TransformPoint(fresh.center);
        }

        /// <summary>A scale factor guarded against a degenerate axis.</summary>
        static float SafeScale(float value)
        {
            return Mathf.Abs(value) < 0.0001f ? 1f : value;
        }

        // ── Assets ────────────────────────────────────────────────────────────────────────────

        [Serializable]
        class HeadData
        {
            public string source;
            public string joint;
            public float[] vertices;
            public float[] normals;
            public float[] uvs;
            public int[] triangles;
        }

        /// <summary>
        /// Turns the extracted KayKit head into a Mesh asset, reusing the asset if it already
        /// exists so prefabs referencing it survive a re-run.
        /// </summary>
        static Mesh BuildHeadMesh(string piece)
        {
            EnsureFolder(k_GeneratedFolder);
            string assetPath = $"{k_GeneratedFolder}/KayKit_{piece}.asset";
            string jsonPath = $"{k_HeadDataFolder}/KayKit_{piece}.json";

            if (!File.Exists(jsonPath))
            {
                Debug.LogError($"[KayKitHeads] Missing head data at {jsonPath}.");
                return null;
            }

            var data = JsonUtility.FromJson<HeadData>(File.ReadAllText(jsonPath));

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            var mesh = existing != null ? existing : new Mesh { name = $"KayKit_{piece}" };

            mesh.Clear();
            mesh.SetVertices(ToVector3(data.vertices));
            mesh.SetNormals(ToVector3(data.normals));
            mesh.SetUVs(0, ToVector2(data.uvs));
            mesh.SetTriangles(data.triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, assetPath);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }

            return mesh;
        }

        /// <summary>
        /// A copy of the material the stock head already wears, with the KayKit atlas dropped into
        /// its <c>_MainTex</c> slot.
        /// </summary>
        /// <remarks>
        /// Copying rather than building fresh is deliberate: <c>SG_Toon</c> carries the whole
        /// cyberpunk look in per-material values — ambient, rim colour and threshold, specular —
        /// and those were hand-set per character. Reconstructing them here would fork that palette
        /// and let the head drift away from the body the first time either is retuned.
        /// </remarks>
        static Material BuildHeadMaterial(HeadTarget target, Renderer stockRenderer)
        {
            EnsureFolder(k_GeneratedFolder);
            string path = $"{k_GeneratedFolder}/KayKit_Head_{target.Label}.mat";

            if (stockRenderer.sharedMaterial == null)
            {
                Debug.LogWarning($"[KayKitHeads] {target.Label}: stock head has no material to copy.");
                return null;
            }

            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>($"{k_TextureFolder}/{target.Atlas}.png");
            if (atlas == null)
            {
                Debug.LogError($"[KayKitHeads] Missing atlas at {k_TextureFolder}/{target.Atlas}.png.");
                return null;
            }

            // Copying a Material Variant with the copy constructor yields another variant, and a
            // variant refuses shader assignment — which is what Head_Rogue_Girl_Cyber, the one
            // stock head material that is a variant, tripped on. Building from the shader and
            // copying properties across sidesteps that: property reads on a variant resolve
            // through to the parent, so the values still arrive intact.
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null && existing.parent != null)
            {
                AssetDatabase.DeleteAsset(path);
                existing = null;
            }

            var material = existing != null ? existing : new Material(stockRenderer.sharedMaterial.shader);

            // Re-copied on every run so a retune of the stock head reaches the swapped one too.
            material.CopyPropertiesFromMaterial(stockRenderer.sharedMaterial);

            // KayKit paints the face into the atlas, so the tint has to go neutral or it multiplies
            // the painted colours down.
            foreach (var tint in new[] { "_Color", "_BaseColor" })
            {
                if (material.HasProperty(tint))
                {
                    material.SetColor(tint, Color.white);
                }
            }

            bool assigned = false;
            foreach (var slot in new[] { "_MainTex", "_BaseMap" })
            {
                if (material.HasProperty(slot))
                {
                    material.SetTexture(slot, atlas);
                    assigned = true;
                    break;
                }
            }

            if (!assigned)
            {
                Debug.LogWarning($"[KayKitHeads] {target.Label}: no _MainTex/_BaseMap slot on "
                    + $"{material.shader.name} — the head will render untextured.");
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

        static List<Vector3> ToVector3(float[] flat)
        {
            var list = new List<Vector3>(flat.Length / 3);
            for (int i = 0; i + 2 < flat.Length; i += 3)
            {
                list.Add(new Vector3(flat[i], flat[i + 1], flat[i + 2]));
            }

            return list;
        }

        static List<Vector2> ToVector2(float[] flat)
        {
            var list = new List<Vector2>(flat.Length / 2);
            for (int i = 0; i + 1 < flat.Length; i += 2)
            {
                list.Add(new Vector2(flat[i], flat[i + 1]));
            }

            return list;
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
            EnsureFolder(folder.Substring(0, lastSlash));
            AssetDatabase.CreateFolder(folder.Substring(0, lastSlash), folder.Substring(lastSlash + 1));
        }
    }
}
