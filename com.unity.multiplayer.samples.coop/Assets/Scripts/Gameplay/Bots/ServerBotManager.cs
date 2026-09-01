using System;
using System.Collections.Generic;
using Mirror;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.BossRoom.Gameplay.GameState;
using Unity.BossRoom.Infrastructure;
using Unity.Multiplayer.Samples.BossRoom;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Unity.BossRoom.Gameplay.Bots
{
    /// <summary>
    /// Server-only. Fills a match with bots that join, pick a character and play like any other
    /// player, and gets out of the way as soon as real players want the slots.
    /// </summary>
    /// <remarks>
    /// <para>Bots are not a parallel game mode: each one owns a <see cref="BotConnection"/>, which
    /// means the ordinary CharSelect, spawn, scoring and kill-feed code treats it as a player
    /// without knowing bots exist. This class only does the three things a human's client would
    /// have done for itself — connect, choose a seat, and press Ready — plus the bookkeeping a
    /// human's absence makes necessary (yielding a slot when the room fills up).</para>
    ///
    /// <para>Created from code rather than placed in a scene, so it reaches a dedicated-server
    /// build without an Editor session — the same reason
    /// <see cref="Unity.BossRoom.Gameplay.Configuration.DeathmatchRules"/> lives in code.</para>
    /// </remarks>
    public class ServerBotManager : MonoBehaviour
    {
        public static ServerBotManager Instance { get; private set; }

        // ── Configuration ─────────────────────────────────────────────────────────────────────
        // Env vars so the master server can tune a dedicated container at spawn time without a
        // rebuild; the defaults are what a P2P host gets.

        /// <summary>Master switch. <c>BOTS_ENABLED=0</c> turns the whole feature off.</summary>
        static bool BotsEnabled => ReadEnvBool("BOTS_ENABLED", true);

        /// <summary>How many participants the match is topped up to, bots included.</summary>
        static int TargetPlayerCount => ReadEnvInt("BOTS_TARGET_PLAYERS", 4);

        /// <summary>
        /// How long CharSelect waits before filling. Long enough that a group joining together
        /// takes their own slots, short enough that a lone player isn't left staring at an empty
        /// lobby.
        /// </summary>
        static float FillDelaySeconds => ReadEnvFloat("BOTS_FILL_DELAY", 12f);

        /// <summary>
        /// Backstop for the whole CharSelect stage: however indecisive a bot is meant to look, it
        /// must be locked in by now, because the session cannot close until everyone is ready and
        /// a bot that never presses Ready would hang the match forever.
        /// </summary>
        const float k_ForceLockInSeconds = 25f;

        /// <summary>
        /// Slots bots are never allowed to occupy, so a human arriving at a "full" lobby always has
        /// somewhere to go. This is the ceiling for both filling and eviction — if the two used
        /// different limits, the manager would add a bot and evict it again on the next frame,
        /// forever.
        /// </summary>
        const int k_SlotsReservedForHumans = 1;

        /// <summary>The highest headcount bots are allowed to take the lobby to.</summary>
        int BotFillCeiling(int maxPlayers, int seatCount) =>
            Mathf.Min(TargetPlayerCount, maxPlayers - k_SlotsReservedForHumans, seatCount);

        // ── State ─────────────────────────────────────────────────────────────────────────────

        readonly List<BotSeatAgent> m_Bots = new();

        ServerCharSelectState m_CharSelectState;
        ConnectionManager m_ConnectionManager;
        float m_CharSelectStartTime;
        bool m_FillDone;
        int m_BotsSpawnedThisSession;

        /// <summary>Bots currently in the match.</summary>
        public int BotCount => m_Bots.Count;

        /// <summary>Connections that belong to real people.</summary>
        /// <summary>
        /// True if <paramref name="clientId"/> belongs to a bot rather than a person.
        /// </summary>
        /// <remarks>
        /// Asked of the connection's type, the same test <see cref="RealPlayerCount"/> uses, rather
        /// than of a name or a list this class would have to keep in step. A bot IS a
        /// <c>BotConnection</c>; nothing else can be one, and nothing can stop being one halfway
        /// through a match.
        /// </remarks>
        public static bool IsBot(ulong clientId)
        {
            return NetworkServer.connections.TryGetValue((int)clientId, out var connection)
                   && connection is BotConnection;
        }

        public static int RealPlayerCount
        {
            get
            {
                int count = 0;
                foreach (var conn in NetworkServer.connections.Values)
                {
                    if (conn is not BotConnection)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// Returns the running manager, creating it if needed. Returns null off the server — bots
        /// are a purely server-side construct and a client must never instantiate one.
        /// </summary>
        public static ServerBotManager EnsureInstance()
        {
            if (!NetworkServer.active)
            {
                return null;
            }

            if (Instance == null)
            {
                var go = new GameObject(nameof(ServerBotManager));
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<ServerBotManager>();
            }

            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ── CharSelect ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called by <see cref="ServerCharSelectState"/> when the lobby opens on the server. Resets
        /// per-match state, so a P2P host that returns to the lobby for a second match gets a fresh
        /// roster rather than the leftovers of the first.
        /// </summary>
        public void BeginCharSelect(ServerCharSelectState charSelectState)
        {
            m_CharSelectState = charSelectState;
            m_CharSelectStartTime = Time.time;
            m_FillDone = false;
            m_BotsSpawnedThisSession = 0;
            m_ConnectionManager = FindAnyObjectByType<ConnectionManager>();

            // Anything left over from a previous match in this process is stale: its connections
            // were dropped with the old session.
            m_Bots.Clear();
        }

        /// <summary>Called when CharSelect goes away, so we stop driving seat choices.</summary>
        public void EndCharSelect()
        {
            m_CharSelectState = null;
        }

        void Update()
        {
            if (!NetworkServer.active)
            {
                // The server stopped underneath us; the connections are already gone.
                m_Bots.Clear();
                Destroy(gameObject);
                return;
            }

            // Bots receive nothing, so without this their lastMessageTime would go stale and the
            // inactivity reaper would drop them mid-match if it's ever switched on.
            for (int i = 0; i < m_Bots.Count; i++)
            {
                m_Bots[i].Connection.KeepAlive();
            }

            PruneDeadBots();
            EnforceCapacityForRealPlayers();

            if (m_CharSelectState != null)
            {
                UpdateCharSelect();
            }
        }

        void UpdateCharSelect()
        {
            var networkCharSelection = m_CharSelectState.networkCharSelection;
            if (networkCharSelection == null)
            {
                return;
            }

            // A lobby with nobody in it should not run a match against itself: hold off until a
            // real player is present, and clear out if they all leave.
            if (RealPlayerCount == 0)
            {
                if (m_Bots.Count > 0)
                {
                    RemoveAllBots();
                }

                return;
            }

            if (!m_FillDone && Time.time - m_CharSelectStartTime >= FillDelaySeconds)
            {
                FillWithBots();
                m_FillDone = true;
            }

            float elapsed = Time.time - m_CharSelectStartTime;
            for (int i = 0; i < m_Bots.Count; i++)
            {
                m_Bots[i].Tick(networkCharSelection, elapsed >= k_ForceLockInSeconds);
            }
        }

        /// <summary>
        /// Tops the lobby up to the target headcount. Deliberately counts everyone already
        /// present — a full lobby of humans gets no bots at all.
        /// </summary>
        void FillWithBots()
        {
            if (!BotsEnabled)
            {
                return;
            }

            int seatCount = m_CharSelectState.networkCharSelection.AvatarConfiguration?.Length ?? 0;
            int maxPlayers = m_ConnectionManager != null ? m_ConnectionManager.MaxConnectedPlayers : 8;
            int ceiling = BotFillCeiling(maxPlayers, seatCount);

            int missing = ceiling - NetworkServer.connections.Count;
            if (missing <= 0)
            {
                return;
            }

            var takenNames = CollectPlayerNames();
            var roster = BotProfileLibrary.CreateRoster(missing, takenNames);

            foreach (var profile in roster)
            {
                if (AddBot(profile) == null)
                {
                    break;
                }
            }

            Debug.Log($"[Bots] Filled lobby with {m_Bots.Count} bot(s) — {RealPlayerCount} real player(s), " +
                      $"target {ceiling}, difficulty {BotDifficulty.Level:0.00}.");
        }

        /// <summary>Names already on show in this lobby, so a bot never reuses one.</summary>
        HashSet<string> CollectPlayerNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var players = m_CharSelectState.networkCharSelection.sessionPlayers;
            for (int i = 0; i < players.Count; i++)
            {
                if (!string.IsNullOrEmpty(players[i].PlayerName))
                {
                    names.Add(players[i].PlayerName);
                }
            }

            return names;
        }

        // ── Joining and leaving ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Brings one bot into the match along the same path a real client takes: a connection, a
        /// session record, a PersistentPlayer, and the approval notification that seats it in
        /// CharSelect. Returns null if the connection could not be registered.
        /// </summary>
        BotConnection AddBot(BotProfile profile)
        {
            int connectionId = BotConnection.AllocateConnectionId();
            string playerId = $"bot:{profile.Personality}:{connectionId}:{m_BotsSpawnedThisSession++}";

            var connection = new BotConnection(connectionId, profile, playerId);

            // Bots skip the authenticator (there is no handshake to run), so they are marked
            // authenticated up front — the same end state a real client reaches.
            connection.isAuthenticated = true;

            if (!NetworkServer.AddConnection(connection))
            {
                Debug.LogError($"[Bots] Could not register connection {connectionId} for bot '{profile.AssignedName}'.");
                return null;
            }

            // Session data must exist before the PersistentPlayer spawns: PersistentPlayer.OnStartServer
            // reads it to set the display name and initial avatar. This is the same call
            // HostingState.HandleApproval makes for a human.
            SessionManager<SessionPlayerData>.Instance.SetupConnectingPlayerSessionData(
                connection.ClientId,
                playerId,
                new SessionPlayerData(connection.ClientId, profile.AssignedName, new NetworkGuid(), 0, true));

            if (!SpawnPersistentPlayer(connection))
            {
                RemoveBot(connection);
                return null;
            }

            // The two notifications a real client's arrival produces. NetworkServer.OnConnectedEvent
            // is deliberately NOT raised: Mirror routes it into the authenticator, which would sit
            // waiting for a handshake that can never arrive from a connection with no transport.
            m_ConnectionManager?.NotifyVirtualClientConnected(connection.ClientId);
            ConnectionManager.InvokeClientApproved(connection.ClientId);

            m_Bots.Add(new BotSeatAgent(connection));
            return connection;
        }

        /// <summary>Mirrors BossRoomMirrorNetworkManager.SpawnPersistentPlayer for a bot.</summary>
        static bool SpawnPersistentPlayer(BotConnection connection)
        {
            var playerPrefab = NetworkManager.singleton != null ? NetworkManager.singleton.playerPrefab : null;
            if (playerPrefab == null)
            {
                Debug.LogError("[Bots] NetworkManager.playerPrefab is not set — cannot spawn a bot's PersistentPlayer.");
                return false;
            }

            var player = Instantiate(playerPrefab);
            // Mirror loads scenes in Single mode; the PersistentPlayer has to survive
            // CharSelect -> BossRoom exactly as a human player's does.
            DontDestroyOnLoad(player);
            return NetworkServer.AddPlayerForConnection(connection, player);
        }

        /// <summary>
        /// Removes a bot, running the same cleanup Mirror runs when a real client drops. Raising
        /// <c>OnDisconnectedEvent</c> is what makes the removal complete rather than half-done: it
        /// destroys the owned objects, frees the CharSelect seat, drops the scoreboard row and
        /// clears the session record — all through the existing disconnect handlers.
        /// </summary>
        public void RemoveBot(BotConnection connection)
        {
            if (connection == null)
            {
                return;
            }

            m_Bots.RemoveAll(agent => agent.Connection == connection);

            if (!NetworkServer.connections.ContainsKey(connection.connectionId))
            {
                return;
            }

            connection.Cleanup();
            NetworkServer.RemoveConnection(connection.connectionId);

            if (NetworkServer.OnDisconnectedEvent != null)
            {
                NetworkServer.OnDisconnectedEvent.Invoke(connection);
            }
            else
            {
                NetworkServer.DestroyPlayerForConnection(connection);
            }
        }

        /// <summary>Removes every bot — e.g. when the last human leaves.</summary>
        public void RemoveAllBots()
        {
            for (int i = m_Bots.Count - 1; i >= 0; i--)
            {
                RemoveBot(m_Bots[i].Connection);
            }

            m_Bots.Clear();
        }

        /// <summary>Drops any bot whose connection has already gone (belt and braces).</summary>
        void PruneDeadBots()
        {
            for (int i = m_Bots.Count - 1; i >= 0; i--)
            {
                if (!NetworkServer.connections.ContainsKey(m_Bots[i].Connection.connectionId))
                {
                    m_Bots.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// A real player must never be turned away because bots filled the room. When the lobby is
        /// at capacity and bots are in it, free a slot pre-emptively so the next human to knock is
        /// let in. The most recently added bot goes first — it has invested the least in the match.
        /// </summary>
        void EnforceCapacityForRealPlayers()
        {
            if (m_Bots.Count == 0 || m_ConnectionManager == null)
            {
                return;
            }

            // Same ceiling the fill uses, so adding and evicting can never fight each other.
            int limit = m_ConnectionManager.MaxConnectedPlayers - k_SlotsReservedForHumans;
            while (m_Bots.Count > 0 && NetworkServer.connections.Count > limit)
            {
                RemoveBot(m_Bots[m_Bots.Count - 1].Connection);
            }
        }

        // ── Gameplay ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Gives a freshly spawned avatar a brain if it belongs to a bot. Called from
        /// <see cref="ServerBossRoomState"/> right after the avatar spawns; a no-op for humans, so
        /// the call site needs no bot-specific branch.
        /// </summary>
        public static void TryAttachBrain(ulong clientId, ServerCharacter character)
        {
            if (character == null)
            {
                return;
            }

            var connection = BotConnection.Find(clientId);
            if (connection == null)
            {
                return;
            }

            var brain = character.gameObject.AddComponent<BotBrain>();
            brain.Initialize(character, connection.Profile);
        }

        // ── Env var helpers ───────────────────────────────────────────────────────────────────

        static bool ReadEnvBool(string key, bool fallback)
        {
            var raw = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrEmpty(raw))
            {
                return fallback;
            }

            return raw != "0" && !raw.Equals("false", StringComparison.OrdinalIgnoreCase);
        }

        static int ReadEnvInt(string key, int fallback)
        {
            var raw = Environment.GetEnvironmentVariable(key);
            return !string.IsNullOrEmpty(raw) && int.TryParse(raw, out var parsed) ? parsed : fallback;
        }

        static float ReadEnvFloat(string key, float fallback)
        {
            var raw = Environment.GetEnvironmentVariable(key);
            return !string.IsNullOrEmpty(raw) &&
                   float.TryParse(raw, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }

        /// <summary>
        /// Drives one bot through character select: look at the roster, take a seat, maybe change
        /// its mind, press Ready. Every choice goes through
        /// <see cref="NetworkCharSelection.ServerRequestSeatChange"/>, i.e. the same event a
        /// player's Command raises — so a bot can have its seat sniped by a human who locks in
        /// first, and has to pick again, exactly as a player would.
        /// </summary>
        class BotSeatAgent
        {
            enum Stage
            {
                Thinking,
                Seated,
                LockedIn,
            }

            public BotConnection Connection { get; }

            BotProfile Profile => Connection.Profile;

            Stage m_Stage = Stage.Thinking;
            float m_NextActionTime;
            int m_ChosenSeat = -1;
            bool m_MayStillChangeMind = true;

            public BotSeatAgent(BotConnection connection)
            {
                Connection = connection;
                // Bots don't all lurch at the lobby the instant they appear.
                m_NextActionTime = Time.time + Profile.SeatDecisionSeconds * Random.Range(0.7f, 1.3f);
                m_MayStillChangeMind = Random.value < Profile.SeatIndecisionChance;
            }

            public void Tick(NetworkCharSelection charSelection, bool forceLockIn)
            {
                if (m_Stage == Stage.LockedIn)
                {
                    return;
                }

                // Not in the roster yet (or no longer in it). Asking for a seat now would throw
                // inside ServerCharSelectState.OnClientChangedSeat, which on a headless server
                // means an exception every frame — so wait until the row exists.
                if (!TryFindSeatState(charSelection, out var seatState, out int currentSeat))
                {
                    return;
                }

                // The server sets a bot back to Inactive when someone else locks in the seat it
                // was sitting on. That is the signal to go and find another one.
                if (m_Stage == Stage.Seated && seatState == NetworkCharSelection.SeatState.Inactive)
                {
                    m_Stage = Stage.Thinking;
                    m_ChosenSeat = -1;
                    m_NextActionTime = Time.time + Profile.EffectiveReactionSeconds;
                }

                if (!forceLockIn && Time.time < m_NextActionTime)
                {
                    return;
                }

                switch (m_Stage)
                {
                    case Stage.Thinking:
                        TakeSeat(charSelection, forceLockIn);
                        break;

                    case Stage.Seated:
                        if (!forceLockIn && m_MayStillChangeMind)
                        {
                            // One change of heart, then commit — the lobby's most human tic.
                            m_MayStillChangeMind = false;
                            m_Stage = Stage.Thinking;
                            m_ChosenSeat = currentSeat;
                            m_NextActionTime = Time.time + Profile.SeatDecisionSeconds * 0.5f;
                            break;
                        }

                        charSelection.ServerRequestSeatChange(Connection.ClientId, m_ChosenSeat, true);
                        m_Stage = Stage.LockedIn;
                        break;
                }
            }

            void TakeSeat(NetworkCharSelection charSelection, bool forceLockIn)
            {
                int seat = ChooseSeat(charSelection, avoidSeat: m_MayStillChangeMind ? -1 : m_ChosenSeat);
                if (seat < 0)
                {
                    // Every seat is locked by somebody else. Try again shortly; a human leaving
                    // frees one up.
                    m_NextActionTime = Time.time + 1f;
                    return;
                }

                m_ChosenSeat = seat;
                charSelection.ServerRequestSeatChange(Connection.ClientId, seat, false);
                m_Stage = Stage.Seated;
                m_NextActionTime = Time.time + Profile.SeatLockInSeconds * Random.Range(0.7f, 1.3f);

                if (forceLockIn)
                {
                    charSelection.ServerRequestSeatChange(Connection.ClientId, seat, true);
                    m_Stage = Stage.LockedIn;
                }
            }

            /// <summary>
            /// Picks a seat: the bot's preferred class if it is free, otherwise any free seat. A
            /// seat someone else has locked in is not free; a seat someone is merely hovering on
            /// is, which is what makes seat contests happen at all.
            /// </summary>
            int ChooseSeat(NetworkCharSelection charSelection, int avoidSeat)
            {
                var avatars = charSelection.AvatarConfiguration;
                if (avatars == null || avatars.Length == 0)
                {
                    return -1;
                }

                var lockedSeats = new HashSet<int>();
                var players = charSelection.sessionPlayers;
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i].ClientId != Connection.ClientId &&
                        players[i].SeatState == NetworkCharSelection.SeatState.LockedIn &&
                        players[i].SeatIdx >= 0)
                    {
                        lockedSeats.Add(players[i].SeatIdx);
                    }
                }

                foreach (var wantedClass in Profile.PreferredClasses)
                {
                    int seat = FindFreeSeatForClass(avatars, lockedSeats, wantedClass, avoidSeat);
                    if (seat >= 0)
                    {
                        return seat;
                    }
                }

                // Its mains are gone: take whatever is left, starting from a random offset so the
                // fallback isn't always the same seat.
                int offset = Random.Range(0, avatars.Length);
                for (int i = 0; i < avatars.Length; i++)
                {
                    int seat = (offset + i) % avatars.Length;
                    if (seat != avoidSeat && !lockedSeats.Contains(seat))
                    {
                        return seat;
                    }
                }

                return -1;
            }

            static int FindFreeSeatForClass(Configuration.Avatar[] avatars, HashSet<int> lockedSeats,
                CharacterTypeEnum wantedClass, int avoidSeat)
            {
                // Each class has two seats (a boy and a girl avatar); start from a random one so
                // two bots wanting the same class don't always collide on the same seat.
                int offset = Random.Range(0, avatars.Length);
                for (int i = 0; i < avatars.Length; i++)
                {
                    int seat = (offset + i) % avatars.Length;
                    if (seat == avoidSeat || lockedSeats.Contains(seat))
                    {
                        continue;
                    }

                    var characterClass = avatars[seat] != null ? avatars[seat].CharacterClass : null;
                    if (characterClass != null && characterClass.CharacterType == wantedClass)
                    {
                        return seat;
                    }
                }

                return -1;
            }

            /// <summary>
            /// Finds this bot's row in the lobby roster. Returns false when it has no row — which
            /// is genuinely different from "its row says Inactive", and conflating the two is what
            /// would let the agent request a seat for a participant the CharSelect state has never
            /// heard of.
            /// </summary>
            bool TryFindSeatState(NetworkCharSelection charSelection,
                out NetworkCharSelection.SeatState seatState, out int seatIdx)
            {
                var players = charSelection.sessionPlayers;
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i].ClientId == Connection.ClientId)
                    {
                        seatIdx = players[i].SeatIdx;
                        seatState = players[i].SeatState;
                        return true;
                    }
                }

                seatIdx = -1;
                seatState = NetworkCharSelection.SeatState.Inactive;
                return false;
            }
        }
    }
}
