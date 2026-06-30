using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity.BossRoom.Editor
{
    /// <summary>
    /// Opens every scene in the project and saves it, forcing Mirror's
    /// NetworkIdentity.OnValidate() to assign stable sceneIds. Run this once
    /// after the first Mirror integration (or any time you see the
    /// "needs to be opened and resaved" build error).
    /// </summary>
    public static class ResaveAllScenes
    {
        [MenuItem("BossRoom/Utilities/Resave All Scenes (fix Mirror sceneIds)")]
        public static void ResaveAll()
        {
            // Prompt the user to save any unsaved work first.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[ResaveAllScenes] Cancelled by user.");
                return;
            }

            var scenePaths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" });
            int saved = 0;

            foreach (var guid in scenePaths)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

                // Force OnValidate on every NetworkIdentity in the scene so Mirror
                // calls AssignSceneID() and writes the sceneId into the asset.
                var identities = Object.FindObjectsByType<global::Mirror.NetworkIdentity>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var ni in identities)
                {
                    EditorUtility.SetDirty(ni);
                    // Explicitly invoke OnValidate so AssignSceneID runs now.
                    var method = ni.GetType().GetMethod("OnValidate",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);
                    method?.Invoke(ni, null);
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, path);
                saved++;
                Debug.Log($"[ResaveAllScenes] Saved: {path} ({identities.Length} NetworkIdentity objects)");
            }

            Debug.Log($"[ResaveAllScenes] Done — {saved} scene(s) resaved. Mirror sceneIds are now assigned.");
        }
    }
}
