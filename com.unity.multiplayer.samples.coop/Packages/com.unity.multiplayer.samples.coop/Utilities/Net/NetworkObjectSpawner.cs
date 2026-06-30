using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Multiplayer.Samples.Utilities
{
    public class NetworkObjectSpawner : MonoBehaviour
    {
        public GameObject prefabReference;

        void Awake()
        {
            if (NetworkServer.active)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
            }
            else
            {
                SafeDestroy(gameObject);
            }
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SpawnNetworkObject();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SafeDestroy(gameObject);
        }

        static void SafeDestroy(GameObject go)
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
#else
            Destroy(go);
#endif
        }

        void SpawnNetworkObject()
        {
            var prefab = ResolvePrefab();
            if (prefab == null)
            {
                Debug.LogError($"[NetworkObjectSpawner] Could not resolve prefab for '{gameObject.name}'. " +
                               "Add the enemy prefab to NetworkManager.spawnPrefabs or assign prefabReference.");
                return;
            }
            var instance = Instantiate(prefab, transform.position, transform.rotation);
            SceneManager.MoveGameObjectToScene(instance, SceneManager.GetSceneByName(gameObject.scene.name));
            instance.transform.localScale = transform.lossyScale;
            NetworkServer.Spawn(instance);
        }

        // Resolve the prefab to spawn. If prefabReference is not assigned in the inspector, extract the
        // enemy type from the spawner's name (e.g. "NetworkObjectSpawner(Imp)") and look it up in
        // NetworkManager.singleton.spawnPrefabs so the prefab list is the single source of truth.
        // In the Editor, falls back to AssetDatabase search and auto-registers the prefab with Mirror.
        GameObject ResolvePrefab()
        {
            if (prefabReference != null)
                return prefabReference;

            var n = gameObject.name;
            int s = n.IndexOf('('), e = n.LastIndexOf(')');
            if (s < 0 || e <= s)
                return null;

            var typeName = n.Substring(s + 1, e - s - 1);

            var found = NetworkManager.singleton?.spawnPrefabs.Find(p => p != null && p.name == typeName);
            if (found != null)
                return found;

#if UNITY_EDITOR
            // Editor fallback: search the AssetDatabase and auto-register so Mirror clients can spawn it.
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets($"t:Prefab {typeName}"))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && prefab.name == typeName && prefab.GetComponent<NetworkIdentity>() != null)
                {
                    NetworkManager.singleton?.spawnPrefabs.Add(prefab);
                    NetworkClient.RegisterPrefab(prefab);
                    return prefab;
                }
            }
#endif
            return null;
        }
    }
}
