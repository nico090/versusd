using System.Collections.Generic;
using Mirror;
using Unity.BossRoom.ConnectionManagement;
using Unity.Multiplayer.Samples.Utilities;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.BossRoom.Mirror
{
    /// <summary>
    /// Bridges Mirror's NetworkManager lifecycle into ConnectionManager's state machine.
    ///
    /// Connection approval is handled by <see cref="MirrorNetworkAuthenticator"/> (assign it
    /// to NetworkManager.authenticator). By the time a connection reaches OnServerReady it has
    /// already passed token validation, and its credentials are available on
    /// conn.authenticationData (<see cref="PlayerAuthData"/>). This component then runs the
    /// gameplay-level approval (capacity / duplicate / build-type) and spawns the
    /// PersistentPlayer.
    ///
    /// Place this as the NetworkManager in the Bootstrap/Main scene instead of Mirror's
    /// default NetworkManager. VContainer injects ConnectionManager after Awake, so all
    /// lifecycle methods lazy-resolve it on first use.
    /// </summary>
    [AddComponentMenu("BossRoom/BossRoom Mirror Network Manager")]
    public class BossRoomMirrorNetworkManager : NetworkManager
    {
        ConnectionManager m_ConnectionManager;

        ConnectionManager ConnectionMgr =>
            m_ConnectionManager ??= FindObjectOfType<ConnectionManager>();

        // Connections whose PersistentPlayer has already been set up, to guard against
        // OnServerReady firing more than once for the same connection.
        readonly HashSet<int> m_SeatedConnections = new();

        // Prefab names that must be spawnable over the network.
        // In a build these should be added to NetworkManager.spawnPrefabs in the Inspector.
        static readonly string[] k_SpawnablePrefabNames =
        {
            "Imp", "VandalImp", "ImpBoss", "Enemy",
            "Arrow", "ChargedArrow1", "ChargedArrow2", "ChargedArrow3",
            "ImpTossedItem"
        };

        /// <summary>
        /// Prefab names that must appear in <see cref="NetworkManager.spawnPrefabs"/> for a
        /// build to spawn them over the network. Single source of truth, also consumed by the
        /// editor audit tool (Boss Room/Mirror Audit) so the check can't drift.
        /// </summary>
        public static IReadOnlyList<string> SpawnablePrefabNames => k_SpawnablePrefabNames;

        public override void Awake()
        {
            base.Awake();
            // The MirrorNetworkAuthenticator validates join tokens (and gates dedicated
            // servers). Mirror wires it up in SetupServer/SetupClient (called from
            // StartServer/StartClient), so it only needs to be assigned before then —
            // Awake is safe. Auto-attach so it works without manual scene wiring;
            // without it Mirror skips auth and connections arrive with no auth data.
            if (authenticator == null)
            {
                authenticator = GetComponent<MirrorNetworkAuthenticator>()
                    ?? gameObject.AddComponent<MirrorNetworkAuthenticator>();
            }
            AutoRegisterSpawnablePrefabs();
        }

        void AutoRegisterSpawnablePrefabs()
        {
#if UNITY_EDITOR
            foreach (var prefabName in k_SpawnablePrefabNames)
            {
                foreach (var guid in AssetDatabase.FindAssets($"t:Prefab {prefabName}"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null || prefab.name != prefabName) continue;
                    if (prefab.GetComponent<NetworkIdentity>() == null) continue;
                    if (spawnPrefabs.Contains(prefab)) continue;
                    spawnPrefabs.Add(prefab);
                }
            }
#endif
        }

        // Mirror resets NetworkClient.ready during scene changes for remote clients
        // (ClientChangeScene sets isLoadingScene=true which clears ready). After the
        // scene loads, OnClientSceneChanged re-sends Ready — but only if
        // isAuthenticated is true. Override to guarantee it always fires.
        public override void OnClientSceneChanged()
        {
            base.OnClientSceneChanged(); // calls NetworkClient.Ready() if authenticated
            // Ensure ready is set even if base guard (isAuthenticated check) didn't fire.
            if (NetworkClient.isConnected && !NetworkClient.ready)
                NetworkClient.Ready();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_SeatedConnections.Clear();
            ValidateSpawnPrefabs();
            ConnectionMgr?.OnMirrorServerStarted();
        }

        // Runtime guard for builds. AutoRegisterSpawnablePrefabs only runs in the Editor,
        // so a dedicated-server build depends entirely on the serialized spawnPrefabs list
        // being complete in the Inspector. If a required prefab is missing the server would
        // otherwise fail later with an opaque "Could not resolve prefab" on first spawn.
        // Surface it loudly at server start instead. (Fase 2 del plan Mirror.)
        void ValidateSpawnPrefabs()
        {
            foreach (var prefabName in k_SpawnablePrefabNames)
            {
                var found = false;
                foreach (var prefab in spawnPrefabs)
                {
                    if (prefab != null && prefab.name == prefabName)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    Debug.LogError(
                        $"[BossRoomMirrorNetworkManager] Spawnable prefab '{prefabName}' is not " +
                        "registered in NetworkManager.spawnPrefabs. Add it in the Inspector — " +
                        "spawning it over the network will fail in this build.");
                }
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            m_SeatedConnections.Clear();
            // The additive-scene registry is static; clear it so it doesn't leak into a future
            // server start within the same process.
            ServerAdditiveSceneLoader.ClearLoadedScenes();
            ConnectionMgr?.OnMirrorServerStopped();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ConnectionMgr?.OnMirrorClientStarted();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            ConnectionMgr?.OnMirrorClientStopped();
        }

        /// <summary>
        /// Fires on the server once the client signals it is ready (NetworkClient.Ready()),
        /// which is the earliest safe moment to call AddPlayerForConnection. The connection
        /// has already been authenticated, so we read its validated credentials, run the
        /// gameplay approval and spawn the PersistentPlayer. Used for host and remote clients.
        /// </summary>
        public override void OnServerReady(NetworkConnectionToClient conn)
        {
            // Mirror doesn't auto-replicate server additive scene loads the way NGO's
            // NetworkSceneManager did, and a load broadcast only reaches clients that were
            // already ready. Replay every additive scene the server currently has loaded to this
            // just-ready client so it gets the geometry — covers loadOnNetworkSpawn scenes (e.g.
            // the entrance, loaded before this client was ready) and late join / reconnect into an
            // already-loaded dungeon. The host is a no-op here: its ClientChangeScene early-outs
            // while NetworkServer.active is true.
            //
            // This MUST run BEFORE base.OnServerReady: that call sets the client ready and
            // immediately spawns every observed object — including the entrance scene's ~19
            // networked objects. The client's message pump halts on isLoadingScene
            // (NetworkClient.cs) the instant it sees a LoadAdditive, finishes the load, and
            // registers the scene's objects via PrepareToSpawnSceneObjects before processing the
            // spawn batch. Send the spawns first and the client drops all of them with
            // "Spawn scene object not found" (the entrance's networked objects never appear).
            foreach (var sceneName in ServerAdditiveSceneLoader.LoadedScenes)
            {
                conn.Send(new SceneMessage
                {
                    sceneName = sceneName,
                    sceneOperation = SceneOperation.LoadAdditive
                });
            }

            base.OnServerReady(conn);

            if (!m_SeatedConnections.Add(conn.connectionId))
                return; // already seated

            PlayerAuthData auth;
            if (conn.authenticationData is PlayerAuthData existing)
            {
                auth = existing;
            }
            else if (conn is LocalConnectionToClient)
            {
                // Defensive fallback: authenticator already ran AcceptConnection for the
                // local host, but authenticationData can still be null if Unity ran the
                // previous DLL before recompile or the message queue drained unexpectedly.
                var p = ClientAuthPayload.Current;
                auth = new PlayerAuthData
                {
                    PlayerId   = p?.PlayerId   ?? SystemInfo.deviceUniqueIdentifier,
                    PlayerName = p?.PlayerName ?? "Host",
                    IsDebug    = p?.IsDebug    ?? Debug.isDebugBuild,
                };
                conn.authenticationData = auth;
            }
            else
            {
                Debug.LogError($"[BossRoom] Connection {conn.connectionId} is ready without auth data — disconnecting.");
                m_SeatedConnections.Remove(conn.connectionId);
                conn.Disconnect();
                return;
            }

            // Token is already validated by the authenticator; rebuild the payload for the
            // gameplay-level approval (capacity / duplicate / build-type compatibility).
            var json = JsonUtility.ToJson(new ConnectionPayload
            {
                playerId = auth.PlayerId,
                playerName = auth.PlayerName,
                isDebug = auth.IsDebug,
                joinToken = string.Empty,
            });

            bool approved = ConnectionMgr?.ProcessConnectionApproval(conn.connectionId, json) ?? false;
            if (!approved)
            {
                m_SeatedConnections.Remove(conn.connectionId);
                conn.Disconnect();
                return;
            }

            // Player cleared the approval gate — now burn the single-use join token.
            // It was only peeked during authentication so a bounced client could retry.
            (authenticator as MirrorNetworkAuthenticator)?.ConsumeJoinToken(auth.JoinToken, auth.SessionId);

            SpawnPersistentPlayer(conn);
            ConnectionManager.InvokeClientApproved((ulong)(uint)conn.connectionId);

            // Spawn this client's networked loading-progress tracker (owned by the connection) so
            // every player can see everyone's loading bars during scene transitions. Done here —
            // once the connection is ready — so the spawn actually reaches the owner.
            Unity.Multiplayer.Samples.Utilities.LoadingProgressManager.Instance?.ServerSpawnTrackerFor(conn);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            m_SeatedConnections.Remove(conn.connectionId);
            Unity.Multiplayer.Samples.Utilities.LoadingProgressManager.Instance?.ServerDespawnTrackerFor(conn);
            base.OnServerDisconnect(conn);
        }

        void SpawnPersistentPlayer(NetworkConnectionToClient conn)
        {
            if (playerPrefab == null)
            {
                Debug.LogError("[BossRoom] NetworkManager.playerPrefab is not set — cannot spawn PersistentPlayer.");
                return;
            }
            var player = Instantiate(playerPrefab);
            // Keep the PersistentPlayer alive across scene changes (Mirror uses Single-mode loads).
            DontDestroyOnLoad(player);
            NetworkServer.AddPlayerForConnection(conn, player);
        }

        public override void OnServerError(NetworkConnectionToClient conn, TransportError error, string reason)
        {
            base.OnServerError(conn, error, reason);
            Debug.LogWarning($"[Mirror] Server transport error on conn {conn?.connectionId}: {error} – {reason}");
            if (error is TransportError.Unexpected or TransportError.Refused)
                ConnectionMgr?.OnMirrorTransportFailure();
        }

        public override void OnClientError(TransportError error, string reason)
        {
            base.OnClientError(error, reason);
            Debug.LogWarning($"[Mirror] Client transport error: {error} – {reason}");
            ConnectionMgr?.OnMirrorTransportFailure();
        }
    }
}
