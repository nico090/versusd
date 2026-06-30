using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Multiplayer.Samples.Utilities
{
    /// <summary>
    /// Trigger-based additive scene loader. Loads a scene additively when any tagged player object enters
    /// the collider, unloads it after a delay once all players have left.
    /// NGO's NetworkSceneManager events replaced with plain SceneManager calls.
    /// </summary>
    public class ServerAdditiveSceneLoader : NetworkBehaviour
    {
        [SerializeField]
        float m_DelayBeforeUnload = 5.0f;

        [SerializeField]
        string m_SceneName;

        [SerializeField]
        string m_PlayerTag;

        // Restored from the original NGO loader (the Mirror port dropped it). Some loaders —
        // notably the dungeon entrance, the area where players actually spawn — must load their
        // scene the moment the server spawns the loader, NOT when a player walks into the trigger:
        // the player needs the entrance floor / spawn points to exist before it can be standing in
        // any trigger, so a trigger-only load is circular and never fires on a dedicated server.
        // The scene's serialized override (loadOnNetworkSpawn: 1 on the DungeonEntrance instance)
        // binds back to this field.
        [SerializeField]
        bool loadOnNetworkSpawn;

        readonly List<uint> m_PlayersInTrigger = new List<uint>();

        bool m_SceneLoaded;
        bool m_Loading;
        Coroutine m_UnloadCoroutine;

        // Server-side set of additive scenes currently loaded. Mirror (unlike NGO's
        // NetworkSceneManager) does not auto-replicate server additive loads, and a load
        // broadcast only reaches clients that are already ready. Newly-ready clients (late join,
        // reconnect, or — for loadOnNetworkSpawn scenes — clients that become ready AFTER the
        // server loaded the scene) replay this set from BossRoomMirrorNetworkManager.OnServerReady.
        static readonly HashSet<string> s_LoadedScenes = new HashSet<string>();
        public static IReadOnlyCollection<string> LoadedScenes => s_LoadedScenes;

        public static void ClearLoadedScenes() => s_LoadedScenes.Clear();

        bool IsActive => isServer && netId != 0;

        public override void OnStartServer()
        {
            m_PlayersInTrigger.Clear();

            // Trigger-independent load for scenes flagged loadOnNetworkSpawn (e.g. the entrance).
            if (loadOnNetworkSpawn && !m_SceneLoaded && !m_Loading)
            {
                m_Loading = true;
                StartCoroutine(LoadSceneAsync());
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (!IsActive) return;
            if (other.CompareTag(m_PlayerTag) && other.TryGetComponent(out NetworkIdentity identity))
            {
                m_PlayersInTrigger.Add(identity.netId);
                if (m_UnloadCoroutine != null)
                {
                    StopCoroutine(m_UnloadCoroutine);
                    m_UnloadCoroutine = null;
                }
            }
        }

        void OnTriggerExit(Collider other)
        {
            if (!IsActive) return;
            if (other.CompareTag(m_PlayerTag) && other.TryGetComponent(out NetworkIdentity identity))
            {
                m_PlayersInTrigger.Remove(identity.netId);
            }
        }

        void FixedUpdate()
        {
            if (!IsActive) return;

            if (!m_SceneLoaded && !m_Loading && m_PlayersInTrigger.Count > 0)
            {
                m_Loading = true;
                StartCoroutine(LoadSceneAsync());
            }
            // loadOnNetworkSpawn scenes (e.g. the entrance) are permanent areas — never auto-unload
            // them, otherwise the entrance would unload 5s after spawn while nobody is registered
            // in its trigger yet (the exact case loadOnNetworkSpawn exists to handle).
            else if (!loadOnNetworkSpawn && m_SceneLoaded && m_PlayersInTrigger.Count == 0 && m_UnloadCoroutine == null)
            {
                m_UnloadCoroutine = StartCoroutine(WaitToUnloadCoroutine());
            }
        }

        IEnumerator LoadSceneAsync()
        {
            // Load the additive scene on the server's own instance.
            var op = SceneManager.LoadSceneAsync(m_SceneName, LoadSceneMode.Additive);
            yield return op;

            // Unlike NGO's NetworkSceneManager, Mirror does NOT replicate a plain additive
            // SceneManager load to clients. Without this message remote/dedicated clients
            // never load the dungeon geometry and see an empty map (the P2P host happens to
            // work because it is also the local client). Tell every ready client to load it.
            // The host client is skipped automatically: Mirror's ClientChangeScene early-outs
            // while NetworkServer.active is true.
            NetworkServer.SendToReady(new SceneMessage
            {
                sceneName = m_SceneName,
                sceneOperation = SceneOperation.LoadAdditive
            });

            // Record it so clients that become ready later get a replay (see LoadedScenes).
            s_LoadedScenes.Add(m_SceneName);

            m_SceneLoaded = true;
            m_Loading = false;
        }

        IEnumerator WaitToUnloadCoroutine()
        {
            yield return new WaitForSeconds(m_DelayBeforeUnload);
            var scene = SceneManager.GetSceneByName(m_SceneName);
            if (scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(scene);
            }

            // Mirror the unload to clients too (same reason as the load above).
            if (NetworkServer.active)
            {
                NetworkServer.SendToReady(new SceneMessage
                {
                    sceneName = m_SceneName,
                    sceneOperation = SceneOperation.UnloadAdditive
                });
            }

            s_LoadedScenes.Remove(m_SceneName);

            m_SceneLoaded = false;
            m_UnloadCoroutine = null;
        }
    }
}
