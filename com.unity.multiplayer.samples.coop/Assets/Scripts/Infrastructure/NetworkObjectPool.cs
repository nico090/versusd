using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Pool;

namespace Unity.BossRoom.Infrastructure
{
    /// <summary>
    /// Object Pool for networked objects. Reuses objects instead of allocating new memory on each spawn.
    /// Boss Room uses this for projectiles.
    /// </summary>
    public class NetworkObjectPool : NetworkBehaviour
    {
        public static NetworkObjectPool Singleton { get; private set; }

        [SerializeField]
        List<PoolConfigObject> PooledPrefabsList;

        HashSet<GameObject> m_Prefabs = new HashSet<GameObject>();

        Dictionary<GameObject, ObjectPool<GameObject>> m_PooledObjects = new Dictionary<GameObject, ObjectPool<GameObject>>();

        public void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
            }
            else
            {
                Singleton = this;
            }
        }

        public override void OnStartServer()
        {
            // Registers all objects in PooledPrefabsList to the cache.
            foreach (var configObject in PooledPrefabsList)
            {
                RegisterPrefabInternal(configObject.Prefab, configObject.PrewarmCount);
            }
        }

        public override void OnStartClient()
        {
            // The pool itself is server-side, but a pure remote client still receives spawn
            // messages for these prefabs (projectiles, etc.) and must have each one registered
            // with Mirror to resolve them by assetId. Without this the client throws
            // "Could not spawn assetId=..." and (with exceptionsDisconnect) drops the connection.
            // Skip on host, where the server already registered them in the shared process.
            if (NetworkServer.active)
                return;
            foreach (var configObject in PooledPrefabsList)
            {
                if (configObject.Prefab != null)
                    NetworkClient.RegisterPrefab(configObject.Prefab);
            }
        }

        public override void OnStopServer()
        {
            // Unregisters all objects in PooledPrefabsList from the cache.
            foreach (var prefab in m_Prefabs)
            {
                m_PooledObjects[prefab].Clear();
            }
            m_PooledObjects.Clear();
            m_Prefabs.Clear();
        }

        public void OnValidate()
        {
            for (var i = 0; i < PooledPrefabsList.Count; i++)
            {
                var prefab = PooledPrefabsList[i].Prefab;
                if (prefab != null)
                {
                    Assert.IsNotNull(prefab.GetComponent<NetworkIdentity>(), $"{nameof(NetworkObjectPool)}: Pooled prefab \"{prefab.name}\" at index {i.ToString()} has no {nameof(NetworkIdentity)} component.");
                }
            }
        }

        /// <summary>
        /// Gets an instance of the given prefab from the pool. The prefab must be registered to the pool.
        /// On the server this should be followed by a NetworkServer.Spawn() call.
        /// </summary>
        public GameObject GetNetworkObject(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            var go = m_PooledObjects[prefab].Get();

            var t = go.transform;
            t.position = position;
            t.rotation = rotation;

            return go;
        }

        /// <summary>
        /// Return an object to the pool (reset objects before returning).
        /// </summary>
        public void ReturnNetworkObject(GameObject go, GameObject prefab)
        {
            m_PooledObjects[prefab].Release(go);
        }

        /// <summary>
        /// Builds up the cache for a prefab.
        /// </summary>
        void RegisterPrefabInternal(GameObject prefab, int prewarmCount)
        {
            GameObject CreateFunc()
            {
                var go = Instantiate(prefab);
                // Mirror's NetworkScenePostProcess re-runs on additive scene loads and reports
                // any NetworkIdentity with sceneId==0 as an error (these are runtime-instantiated
                // pool objects, not scene objects).  Moving them to DontDestroyOnLoad keeps them
                // out of Mirror's scene scan while still being properly destroyed when
                // NetworkObjectPool.OnStopServer clears the pool.
                DontDestroyOnLoad(go);
                return go;
            }

            void ActionOnGet(GameObject go)
            {
                go.SetActive(true);
            }

            void ActionOnRelease(GameObject go)
            {
                go.SetActive(false);
            }

            void ActionOnDestroy(GameObject go)
            {
                Destroy(go);
            }

            m_Prefabs.Add(prefab);

            // Create the pool
            m_PooledObjects[prefab] = new ObjectPool<GameObject>(CreateFunc, ActionOnGet, ActionOnRelease, ActionOnDestroy, defaultCapacity: prewarmCount);

            // Populate the pool
            var prewarmObjects = new List<GameObject>();
            for (var i = 0; i < prewarmCount; i++)
            {
                prewarmObjects.Add(m_PooledObjects[prefab].Get());
            }
            foreach (var go in prewarmObjects)
            {
                m_PooledObjects[prefab].Release(go);
            }
        }
    }

    [Serializable]
    struct PoolConfigObject
    {
        public GameObject Prefab;
        public int PrewarmCount;
    }
}
