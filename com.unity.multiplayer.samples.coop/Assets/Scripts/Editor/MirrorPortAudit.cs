using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.BossRoom.Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.BossRoom.Editor
{
    /// <summary>
    /// Read-only audit for the NGO→Mirror port. Scans the project's own scenes and prefabs
    /// (Mirror's own package + examples are skipped) and reports, without modifying anything:
    ///
    ///  1. Vestigial NetworkIdentity — a <c>NetworkIdentity</c> on a GameObject that has
    ///     no <c>NetworkBehaviour</c>. The NGO→Mirror conversion left these on bootstrap
    ///     objects; on a dedicated server they get spawned by sceneId and fail with
    ///     "Spawn scene object not found" on clients that unloaded the scene. (Fase 1)
    ///  2. Missing scripts — components whose backing script no longer resolves (a leftover of
    ///     removed NGO components, or a deleted dead stub). (Fase 1)
    ///  3. Dead NGO stubs — empty MonoBehaviours kept only so scene refs don't break.
    ///     Reported so they can be removed in the Editor (component first, then the .cs) without
    ///     leaving missing scripts. NetworkLatencyWarning was removed during the port cleanup.
    ///     NetworkSimulatorUIMediator is intentionally NOT listed: it is not dead — its Awake()
    ///     hides the simulator CanvasGroup, so removing it would expose a broken debug panel. (Fase 1)
    ///  4. spawnPrefabs coverage — every name in
    ///     <see cref="BossRoomMirrorNetworkManager.SpawnablePrefabNames"/> must be present in the
    ///     manager's serialized spawnPrefabs list, since auto-register only runs in the Editor and
    ///     the dedicated-server build relies entirely on the serialized list. (Fase 2)
    ///
    /// The audit only reports — apply fixes yourself in the Editor so every change is undoable.
    /// </summary>
    public static class MirrorPortAudit
    {
        // Type names (no assembly reference needed) of empty NGO stubs from the port that are
        // safe to delete. NetworkLatencyWarning already was; NetworkSimulatorUIMediator is NOT
        // here on purpose — it still hides its CanvasGroup on Awake, so it must stay.
        static readonly HashSet<string> k_DeadStubTypeNames = new()
        {
        };

        // Asset path prefixes to skip — Mirror's package source and its bundled examples carry
        // thousands of their own identities that are not part of this port.
        static readonly string[] k_SkipPathPrefixes =
        {
            "Assets/Mirror/",
        };

        [MenuItem("Boss Room/Mirror Audit/Run Full Audit")]
        public static void RunFullAudit()
        {
            var report = new StringBuilder();
            int findings = 0;

            findings += AuditPrefabs(report);
            findings += AuditScenes(report);

            var header = findings == 0
                ? "Mirror port audit: no findings. Scenes and prefabs look clean."
                : $"Mirror port audit: {findings} finding(s). See Console for details.";

            if (findings == 0)
            {
                Debug.Log(header);
            }
            else
            {
                Debug.LogWarning(header + "\n\n" + report);
            }

            EditorUtility.DisplayDialog("Mirror Port Audit", header, "OK");
        }

        static bool ShouldSkip(string assetPath)
        {
            return k_SkipPathPrefixes.Any(prefix => assetPath.StartsWith(prefix));
        }

        static int AuditPrefabs(StringBuilder report)
        {
            int findings = 0;
            report.AppendLine("=== Prefabs ===");

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (ShouldSkip(path)) continue;

                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;

                findings += ScanGameObjectTree(root, path, report);
            }

            // spawnPrefabs coverage lives on the manager prefab/scene; check any prefab that has it.
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (ShouldSkip(path)) continue;
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;
                var mgr = root.GetComponentInChildren<BossRoomMirrorNetworkManager>(true);
                if (mgr != null) findings += AuditSpawnPrefabCoverage(mgr, path, report);
            }

            return findings;
        }

        static int AuditScenes(StringBuilder report)
        {
            int findings = 0;
            report.AppendLine("=== Scenes ===");

            // Remember which scenes are already open so we don't close the user's working scenes.
            var alreadyOpen = new HashSet<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                alreadyOpen.Add(SceneManager.GetSceneAt(i).path);

            foreach (var guid in AssetDatabase.FindAssets("t:Scene"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (ShouldSkip(path)) continue;

                bool wasOpen = alreadyOpen.Contains(path);
                Scene scene;
                try
                {
                    scene = wasOpen
                        ? SceneManager.GetSceneByPath(path)
                        : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                }
                catch (System.Exception e)
                {
                    report.AppendLine($"[skip] Could not open scene '{path}': {e.Message}");
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    findings += ScanGameObjectTree(root, path, report);
                    var mgr = root.GetComponentInChildren<BossRoomMirrorNetworkManager>(true);
                    if (mgr != null) findings += AuditSpawnPrefabCoverage(mgr, path, report);
                }

                if (!wasOpen)
                    EditorSceneManager.CloseScene(scene, true);
            }

            return findings;
        }

        // Walks a GameObject hierarchy looking for vestigial identities, missing scripts and dead stubs.
        static int ScanGameObjectTree(GameObject root, string assetPath, StringBuilder report)
        {
            int findings = 0;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                var components = go.GetComponents<Component>();

                // (2) Missing scripts surface as null components.
                int missing = components.Count(c => c == null);
                if (missing > 0)
                {
                    findings++;
                    report.AppendLine(
                        $"[missing-script] {assetPath} :: '{GetHierarchyPath(t)}' has {missing} missing script(s).");
                }

                // (1) Vestigial NetworkIdentity: identity present, no NetworkBehaviour anywhere on the GO.
                var identity = go.GetComponent<global::Mirror.NetworkIdentity>();
                if (identity != null && go.GetComponents<global::Mirror.NetworkBehaviour>().Length == 0)
                {
                    findings++;
                    report.AppendLine(
                        $"[vestigial-identity] {assetPath} :: '{GetHierarchyPath(t)}' has a NetworkIdentity " +
                        "but no NetworkBehaviour. Likely a leftover from the NGO→Mirror port; remove it.");
                }

                // (3) Dead NGO stubs.
                foreach (var c in components)
                {
                    if (c == null) continue;
                    var typeName = c.GetType().FullName;
                    if (typeName != null && k_DeadStubTypeNames.Contains(typeName))
                    {
                        findings++;
                        report.AppendLine(
                            $"[dead-stub] {assetPath} :: '{GetHierarchyPath(t)}' has '{typeName}'. " +
                            "Empty post-port stub; remove the component, then delete the .cs.");
                    }
                }
            }

            return findings;
        }

        static int AuditSpawnPrefabCoverage(BossRoomMirrorNetworkManager mgr, string assetPath, StringBuilder report)
        {
            int findings = 0;
            var present = new HashSet<string>(
                mgr.spawnPrefabs.Where(p => p != null).Select(p => p.name));

            foreach (var required in BossRoomMirrorNetworkManager.SpawnablePrefabNames)
            {
                if (!present.Contains(required))
                {
                    findings++;
                    report.AppendLine(
                        $"[spawnprefabs-missing] {assetPath} :: NetworkManager.spawnPrefabs is missing " +
                        $"'{required}'. Add the prefab in the Inspector or it won't spawn in a build.");
                }
            }

            if (mgr.playerPrefab == null)
            {
                findings++;
                report.AppendLine($"[playerprefab-missing] {assetPath} :: NetworkManager.playerPrefab is unset.");
            }

            return findings;
        }

        static string GetHierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var cur = t.parent;
            while (cur != null)
            {
                sb.Insert(0, cur.name + "/");
                cur = cur.parent;
            }
            return sb.ToString();
        }
    }
}
