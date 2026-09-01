using System.Collections.Generic;
using Mirror;
using Unity.BossRoom.ConnectionManagement;
using Unity.Multiplayer.Samples.BossRoom;
using Unity.Multiplayer.Samples.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;
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
            // Replace the scene's LAN-era network settings with ones sized for the relay. Applied
            // here rather than in Startup.unity because the Editor serves its cached copy of a
            // scene, so a scene edit made outside it does not reliably reach a build.
            // NetworkManager.ApplyConfiguration() re-reads these fields every Update, so setting
            // them once here is enough to make them stick.
            OnlineTuning.Apply(this);
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

        // ── Ready handshake diagnostics ───────────────────────────────────────────────────────

        /// <summary>
        /// Logs every step of the ready handshake. On while the "client stuck not ready" bug is
        /// being chased; turn it off in the Inspector once it is understood.
        /// </summary>
        /// <remarks>
        /// The four hooks below are the whole handshake, in the order it is supposed to happen:
        /// the server announces a scene change (<see cref="OnServerChangeScene"/>) which sends
        /// every client a NotReadyMessage (<see cref="OnClientNotReady"/>); the client is told
        /// which scene to load (<see cref="OnClientChangeScene"/>) and, once it has loaded it,
        /// becomes ready again (<see cref="OnClientSceneChanged"/>). Exactly one of those four is
        /// going missing, and their order is the answer.
        /// </remarks>
        [Header("Depuración")]
        [SerializeField]
        bool m_LogReadyHandshake = true;

        string ReadyState =>
            $"escena='{SceneManager.GetActiveScene().name}' ready={NetworkClient.ready} " +
            $"isLoadingScene={NetworkClient.isLoadingScene} loadPendiente={loadingSceneAsync != null}";

        /// <summary>Fires on the client when the server sends a NotReadyMessage.</summary>
        public override void OnClientNotReady()
        {
            base.OnClientNotReady();

            if (m_LogReadyHandshake)
            {
                // The one message that can clear the flag. If this lands *after* the scene load
                // finished, nothing will ever set the client ready again.
                Debug.Log($"[Ready] 2. NotReady recibido del servidor — {ReadyState}");
            }
        }

        /// <summary>Fires on the client when a SceneMessage arrives, before the load starts.</summary>
        public override void OnClientChangeScene(string newSceneName, SceneOperation sceneOperation, bool customHandling)
        {
            base.OnClientChangeScene(newSceneName, sceneOperation, customHandling);

            // A LoadAdditive for a scene the client already has produces no load at all, and
            // therefore no OnClientSceneChanged — the documented way this handshake stalls.
            // Mirror's ClientChangeScene takes the else branch, logs "Scene is already loaded",
            // resets isLoadingScene and returns WITHOUT assigning loadingSceneAsync; FinishLoadScene
            // is only ever called from that AsyncOperation completing, so nothing re-readies the
            // client. Flagged here (before Mirror's switch runs) and repaired on the next
            // LateUpdate, because at this point isLoadingScene is still mid-flight.
            m_LoadWillBeSkipped = sceneOperation == SceneOperation.LoadAdditive
                                  && SceneManager.GetSceneByName(newSceneName).IsValid();

            if (m_LogReadyHandshake)
            {
                Debug.Log($"[Ready] 3. SceneMessage {sceneOperation} '{newSceneName}' " +
                          $"(customHandling={customHandling}, cargaOmitida={m_LoadWillBeSkipped}) — {ReadyState}");
            }
        }

        /// <summary>
        /// Set when the SceneMessage just received is one Mirror will not actually load, so the
        /// watchdog can repair readiness at once instead of waiting out its grace period.
        /// </summary>
        /// <remarks>
        /// The delay exists to avoid mistaking a scene handover in flight for a stuck client. In
        /// this one case there is no handover to wait for — we already know no load is coming — so
        /// waiting only buys the player a second and a half of dead clicks.
        /// </remarks>
        bool m_LoadWillBeSkipped;

        /// <summary>Fires on the server for every scene change it starts.</summary>
        public override void OnServerChangeScene(string newSceneName)
        {
            base.OnServerChangeScene(newSceneName);

            // The registry exists so OnServerReady can replay the dungeon to a client that
            // becomes ready after it was loaded. Entries are removed when ServerAdditiveSceneLoader
            // unloads a scene itself — but a single-mode scene change destroys every additive
            // scene without going through that path, taking the loader components with it, so the
            // names were left in a static set for the rest of the server's life. On a dedicated
            // server that meant every client becoming ready in CharSelect or PostGame was sent a
            // LoadAdditive for a dungeon the server no longer has. This hook runs before the new
            // scene starts loading, which is exactly when everything in the set stops existing.
            ServerAdditiveSceneLoader.ClearLoadedScenes();

            if (m_LogReadyHandshake)
            {
                // Every client is marked not-ready by this call. The stack trace in the console
                // says who asked for the scene change.
                Debug.Log($"[Ready] 1. ServerChangeScene -> '{newSceneName}': todos los clientes " +
                          "quedan NOT READY.");
            }
        }

        /// <summary>
        /// Re-sends Ready after a server-driven scene change.
        /// </summary>
        /// <remarks>
        /// <c>ServerChangeScene</c> calls <c>SetAllClientsNotReady()</c>, which sends every client
        /// a <c>NotReadyMessage</c>; the client clears <c>NetworkClient.ready</c> and is expected
        /// to become ready again here, once the new scene has finished loading. Mirror's own
        /// version of this only fires when the connection reports as authenticated, so the second
        /// call covers the case where it does not.
        /// </remarks>
        public override void OnClientSceneChanged()
        {
            bool wasReady = NetworkClient.ready;

            base.OnClientSceneChanged(); // calls NetworkClient.Ready() if authenticated

            if (NetworkClient.isConnected && !NetworkClient.ready)
                NetworkClient.Ready();

            if (m_LogReadyHandshake)
            {
                Debug.Log($"[Ready] 4. Escena cargada, fin del handshake — antes ready={wasReady}, " +
                          $"ahora ready={NetworkClient.ready} — {ReadyState}");
            }
        }

        // ── Readiness watchdog ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Scenes where the client is expected to be ready, because it has UI that sends Commands.
        /// The watchdog only repairs readiness in these — everywhere else, "not ready" is a normal
        /// state to pass through.
        /// </summary>
        static readonly string[] k_CommandScenes = { "CharSelect", "BossRoom", "PostGame" };

        /// <summary>
        /// How long the client may sit not-ready, with nothing loading, before it is repaired.
        /// Long enough that a scene handover in flight is never mistaken for a stuck client.
        /// </summary>
        const float k_ReadyRepairDelay = 1.5f;

        /// <summary>When the current not-ready stretch started, or 0 while the client is fine.</summary>
        float m_NotReadySince;

        public override void LateUpdate()
        {
            base.LateUpdate(); // Mirror's UpdateScene(), which is what finishes a scene load

            RepairClientReadiness();
            RepairHostReadinessDisagreement();
        }

        /// <summary>
        /// Puts the client back into the ready state if it is stuck out of it.
        /// </summary>
        /// <remarks>
        /// <para><b>Why this is needed at all.</b> A client that is connected but not ready has
        /// every Command it sends silently dropped by <c>NetworkBehaviour.SendCommandInternal</c>
        /// — it only logs a warning. In CharSelect that means clicking a seat does nothing; in
        /// BossRoom it would mean the player cannot act at all. The handshake that is supposed to
        /// restore readiness (NotReadyMessage on the server's scene change, Ready again once the
        /// client's load finishes) has several ways to be missed in this port: an additive
        /// SceneMessage for a scene the client already has never produces a
        /// <c>loadingSceneAsync</c>, so <c>FinishLoadScene</c> — and with it
        /// <see cref="OnClientSceneChanged"/> — never runs; and the local connection's queued
        /// messages can be delivered on either side of a host's own scene load.</para>
        ///
        /// <para><b>Why it is safe.</b> It only acts once the client is authenticated, has nothing
        /// loading, is standing in a scene where it is meant to be ready, and has been stuck for
        /// <see cref="k_ReadyRepairDelay"/>. Nothing in this project ever sets a client not-ready
        /// on purpose, so in these conditions "not ready" is only ever a fault. Re-sending Ready
        /// is idempotent on the server: <see cref="OnServerReady"/> guards the PersistentPlayer
        /// spawn with <see cref="m_SeatedConnections"/>.</para>
        ///
        /// <para>It logs when it fires. If that line shows up, the handshake above is the thing to
        /// go and fix — this only stops the player being stuck while it is broken.</para>
        /// </remarks>
        void RepairClientReadiness()
        {
            if (!NetworkClient.active || !NetworkClient.isConnected || NetworkClient.ready)
            {
                m_NotReadySince = 0f;
                return;
            }

            // A scene change legitimately clears ready, and the client re-readies when the load
            // completes. Nothing is wrong while that is still in flight.
            if (NetworkClient.isLoadingScene || loadingSceneAsync != null)
            {
                m_NotReadySince = 0f;
                return;
            }

            if (NetworkClient.connection == null || !NetworkClient.connection.isAuthenticated)
            {
                m_NotReadySince = 0f;
                return;
            }

            var scene = SceneManager.GetActiveScene().name;
            if (System.Array.IndexOf(k_CommandScenes, scene) < 0)
            {
                m_NotReadySince = 0f;
                return;
            }

            if (m_NotReadySince == 0f)
            {
                m_NotReadySince = Time.unscaledTime;

                // Nothing to wait for when the last SceneMessage was one Mirror skipped: fall
                // through and repair on this very frame.
                if (!m_LoadWillBeSkipped)
                {
                    return;
                }
            }

            if (!m_LoadWillBeSkipped && Time.unscaledTime - m_NotReadySince < k_ReadyRepairDelay)
            {
                return;
            }

            bool skipped = m_LoadWillBeSkipped;
            m_LoadWillBeSkipped = false;

            m_NotReadySince = 0f;

            if (skipped)
            {
                Debug.Log($"[Mirror] SceneMessage sin carga real en '{scene}' (la escena ya estaba " +
                          "cargada), así que no hubo OnClientSceneChanged que reenviara Ready. " +
                          "Reparado en el acto, sin clics perdidos.");
            }
            else
            {
                Debug.LogWarning($"[Mirror] El cliente lleva {k_ReadyRepairDelay:0.0}s conectado y NOT READY " +
                                 $"en '{scene}', sin ninguna carga de escena en curso. Sus Commands se estaban " +
                                 "descartando (clic en asiento, acciones). Se reenvía Ready.");
            }

            NetworkClient.Ready();
        }

        /// <summary>
        /// The other half of the same failure: the client believes it is ready and the server does
        /// not. <see cref="RepairClientReadiness"/> cannot see this one — <c>NetworkClient.ready</c>
        /// is true — and it is the more damaging of the two, because Commands are sent and then
        /// thrown away at the far end instead of never leaving.
        /// </summary>
        /// <remarks>
        /// Only checkable while hosting, where both halves live in this process.
        /// <see cref="OnServerSceneChanged"/> repairs the known cause at the moment it happens;
        /// this is the net for whatever else can produce the same disagreement.
        /// </remarks>
        void RepairHostReadinessDisagreement()
        {
            var local = NetworkServer.localConnection;

            if (local == null || local.isReady || !NetworkClient.active || !NetworkClient.ready
                || NetworkServer.isLoadingScene || NetworkClient.isLoadingScene)
            {
                m_DisagreementSince = 0f;
                return;
            }

            if (m_DisagreementSince == 0f)
            {
                m_DisagreementSince = Time.unscaledTime;
                return;
            }

            if (Time.unscaledTime - m_DisagreementSince < k_ReadyRepairDelay)
            {
                return;
            }

            m_DisagreementSince = 0f;

            Debug.LogWarning($"[Mirror] El cliente local se cree listo pero el servidor lo tiene como NOT " +
                             $"READY desde hace {k_ReadyRepairDelay:0.0}s en '{SceneManager.GetActiveScene().name}'. " +
                             "Sus Commands llegaban y se rechazaban. Se corre el handshake del lado del servidor.");

            OnServerReady(local);
        }

        /// <summary>When the current client/server disagreement started, or 0 while they agree.</summary>
        float m_DisagreementSince;

        /// <summary>
        /// Fires on the server once its own scene load has finished. Used to repair the host's
        /// ready handshake, which this project's startup order can lose.
        /// </summary>
        /// <remarks>
        /// <para><b>How it gets lost.</b> <c>StartHost()</c> calls <c>SetupServer()</c>, whose
        /// <c>OnStartServer</c> reaches <c>HostingState.Enter()</c> — and that asks for CharSelect
        /// straight away, so <c>NetworkServer.isLoadingScene</c> is already true while
        /// <c>StartHost</c> is still running. Mirror has a mechanism for exactly this
        /// (<c>finishStartHostPending</c>) but it only arms it for its own <c>onlineScene</c>
        /// feature, which this project does not use: it drives scenes itself. So
        /// <c>FinishStartHost()</c> runs immediately, the host client connects and sends its
        /// ReadyMessage — and <c>NetworkServer.OnTransportData</c> parks it, because its drain loop
        /// is <c>while (!isLoadingScene …)</c>.</para>
        ///
        /// <para>Nothing wakes that message up afterwards. A remote connection keeps receiving
        /// packets, so its unbatcher drains on the next one; the host's
        /// <c>LocalConnectionToClient.Update</c> only calls <c>OnTransportData</c> while its queue
        /// has something in it, and the queue is empty by then. Meanwhile the client already set
        /// its own <c>NetworkClient.ready</c> before sending, so <see cref="OnClientSceneChanged"/>
        /// sees nothing to fix.</para>
        ///
        /// <para><b>What that looks like.</b> The client believes it is ready and the server does
        /// not, so Commands leave the client and are rejected on arrival ("received … when client
        /// not ready") — clicking a seat in CharSelect does nothing. It unsticks itself only when
        /// the client next sends something, because that finally drains the parked message.</para>
        /// </remarks>
        public override void OnServerSceneChanged(string sceneName)
        {
            base.OnServerSceneChanged(sceneName);

            RepairHostReadiness(sceneName);
        }

        /// <summary>
        /// Runs the ready handshake for the host's own connection when the two sides disagree
        /// about it. Host only — on a dedicated server there is no local connection.
        /// </summary>
        void RepairHostReadiness(string sceneName)
        {
            var local = NetworkServer.localConnection;

            // Only when the client half says ready and the server half does not. Any other
            // combination is a state the normal handshake still owns.
            if (local == null || local.isReady || !NetworkClient.active || !NetworkClient.ready)
            {
                return;
            }

            if (m_LogReadyHandshake)
            {
                Debug.Log($"[Ready] Reparación de host: el cliente local se cree listo pero el servidor " +
                          $"lo tenía como NOT READY tras cargar '{sceneName}'. Se corre el " +
                          "handshake del lado del servidor.");
            }

            // OnServerReady rather than SetClientReady: it is the whole handshake — the additive
            // scene replay, the base call that marks the connection ready and spawns its
            // observers, and the PersistentPlayer. Running it twice is safe; m_SeatedConnections
            // makes the second pass a no-op, which is what happens when the parked ReadyMessage
            // finally arrives.
            OnServerReady(local);
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

            // Nothing was calling this. Its own summary says it exists to reset the runtime state
            // "so it is ready to be reinitialized when starting a new server" — without it every
            // player's SessionPlayerData survives a host stopping and hosting again in the same
            // process, and the second session starts by treating everyone as a returning player.
            SessionManager<SessionPlayerData>.Instance.OnServerEnded();
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
