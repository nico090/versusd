using System.Threading.Tasks;
using TMPro;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.MasterServer;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Unity.BossRoom.Gameplay.UI
{
    public class SessionUIMediator : MonoBehaviour
    {
        [SerializeField] CanvasGroup m_CanvasGroup;
        [SerializeField] SessionJoiningUI m_SessionJoiningUI;
        [SerializeField] SessionCreationUI m_SessionCreationUI;
        [SerializeField] UITinter m_JoinToggleHighlight;
        [SerializeField] UITinter m_JoinToggleTabBlocker;
        [SerializeField] UITinter m_CreateToggleHighlight;
        [SerializeField] UITinter m_CreateToggleTabBlocker;
        [SerializeField] TextMeshProUGUI m_PlayerNameLabel;
        [SerializeField] GameObject m_LoadingSpinner;

        [Inject] MasterServerFacade m_MasterServerFacade;
        [Inject] ConnectionManager m_ConnectionManager;

        // Shown briefly when something notable happens (P2P fallback, errors).
        Text m_ToastLabel;

        void Start()
        {
            m_SessionJoiningUI?.Initialize(this);
            m_SessionCreationUI?.Initialize(this);

            // Relabel the tab buttons using the UITinter sibling text, so the
            // player understands "Create Room" vs "Find & Join" at a glance.
            RelabelTab(m_CreateToggleHighlight, "Create Room", "You will host the match");
            RelabelTab(m_JoinToggleHighlight, "Find & Join", "Browse public rooms");

            // Build the toast overlay (hidden by default).
            BuildToast();

            // Open the "Find & Join" tab by default so public rooms are visible immediately.
            ToggleJoinSessionUI();
        }

        public void Show()
        {
            if (m_CanvasGroup) { m_CanvasGroup.alpha = 1f; m_CanvasGroup.blocksRaycasts = true; }
            if (m_PlayerNameLabel && m_MasterServerFacade != null)
                m_PlayerNameLabel.text = m_MasterServerFacade.Username;
        }

        public void Hide()
        {
            if (m_CanvasGroup) { m_CanvasGroup.alpha = 0f; m_CanvasGroup.blocksRaycasts = false; }
            m_SessionCreationUI?.Hide();
            m_SessionJoiningUI?.Hide();
        }

        public void ToggleJoinSessionUI()
        {
            m_SessionJoiningUI?.Show();
            m_SessionCreationUI?.Hide();
        }

        public void ToggleCreateSessionUI()
        {
            m_SessionJoiningUI?.Hide();
            m_SessionCreationUI?.Show();
        }

        public void RegenerateName()
        {
            if (m_PlayerNameLabel && m_MasterServerFacade != null)
                m_PlayerNameLabel.text = m_MasterServerFacade.Username;
        }

        /// <summary>
        /// Creates a room on a VPS dedicated server. If no server is available,
        /// falls back to P2P automatically and notifies the player.
        /// </summary>
        public async void CreateDedicatedSessionRequest(string sessionName, bool isPrivate, string password = null)
        {
            if (m_MasterServerFacade == null)
            {
                Debug.LogWarning("[SessionUI] MasterServerFacade not available.");
                return;
            }
            SetSpinner(true);
            string name = ResolveSessionName(sessionName);
            var lobby = await m_MasterServerFacade.CreateDedicatedLobbyAsync(name, 8, isPrivate, password);
            SetSpinner(false);

            if (lobby != null)
            {
                // Dedicated server allocated — join the lobby to get a token, then connect.
                var join = await m_MasterServerFacade.JoinLobbyAsync(lobby.session_id);
                if (join == null) return;
                await ConnectFromJoin(join);
                return;
            }

            if (!m_MasterServerFacade.LastErrorWasServerUnavailable)
            {
                // Not a capacity problem (e.g. duplicate room name, 409) — don't
                // fall back to P2P; tell the player so they can pick a new name.
                ShowToast("Could not create room.\nThat name may already be taken — try another.", 5f);
                return;
            }

            // Fallback: host via the relay (player hosts, the VPS just forwards packets) instead
            // of allocating a dedicated container.
            Debug.Log("[SessionUI] No dedicated servers available — falling back to relay.");
            ShowFallbackNotice();
            CreateSessionRequest(name, isPrivate, password);
        }

        /// <summary>
        /// Normalizes the room name the player typed. The master server rejects duplicate
        /// names with a 409 (comparison is trimmed + case-insensitive), so a fixed "Room"
        /// fallback failed as soon as a second player created a room without naming it.
        /// A blank (or whitespace-only) name becomes "Room-XXXX" with a random suffix instead.
        /// Idempotent for non-empty names, so it's safe to call again on the P2P fallback path.
        /// </summary>
        static string ResolveSessionName(string sessionName)
        {
            string trimmed = sessionName?.Trim();
            return string.IsNullOrEmpty(trimmed) ? $"Room-{RandomCode(4)}" : trimmed;
        }

        // Excludes look-alike glyphs (I/1, O/0) so a generated name stays easy to read out loud.
        static string RandomCode(int length)
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var chars = new char[length];
            for (int i = 0; i < length; i++)
                chars[i] = alphabet[UnityEngine.Random.Range(0, alphabet.Length)];
            return new string(chars);
        }

        void ShowFallbackNotice()
        {
            Debug.Log("[SessionUI] No dedicated servers available — falling back to relay host.");
            ShowToast("No dedicated servers available.\nHosting via the relay instead.", 5f);
        }

        // ── Tab relabelling ───────────────────────────────────────────────────

        // Replaces the first TMP or uGUI Text found on the tab button's GameObject
        // (or its direct children) with a clearer label + optional tooltip subtitle.
        static void RelabelTab(UITinter tinter, string newLabel, string tooltip)
        {
            if (tinter == null) return;
            // TMP first
            var tmp = tinter.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) { tmp.text = newLabel; return; }
            // Legacy uGUI Text
            var txt = tinter.GetComponentInChildren<Text>(true);
            if (txt != null) txt.text = newLabel;
            // Tooltip as hover title — just sets the GO name as a hint for now.
            tinter.gameObject.name = $"{newLabel} [{tooltip}]";
        }

        // ── Toast / banner ────────────────────────────────────────────────────

        void BuildToast()
        {
            if (m_CanvasGroup == null) return;
            var go = new GameObject("__Toast");
            go.transform.SetParent(m_CanvasGroup.transform, false);
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.7f, 0.45f, 0.05f, 0.92f);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -2f);
            r.sizeDelta = new Vector2(0f, 48f);

            var tGO = new GameObject("ToastText");
            tGO.transform.SetParent(go.transform, false);
            m_ToastLabel = tGO.AddComponent<Text>();
            m_ToastLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_ToastLabel.fontSize = 13;
            m_ToastLabel.color = Color.white;
            m_ToastLabel.alignment = TextAnchor.MiddleCenter;
            var tr = tGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(12f, 2f); tr.offsetMax = new Vector2(-12f, -2f);

            go.SetActive(false);
        }

        void ShowToast(string message, float duration)
        {
            if (m_ToastLabel == null) return;
            m_ToastLabel.text = message;
            m_ToastLabel.gameObject.transform.parent.gameObject.SetActive(true);
            CancelInvoke(nameof(HideToast));
            Invoke(nameof(HideToast), duration);
        }

        void HideToast()
        {
            if (m_ToastLabel != null)
                m_ToastLabel.gameObject.transform.parent.gameObject.SetActive(false);
        }

        /// <summary>Creates a relay-hosted room: the player hosts on their own machine, but the
        /// self-hosted LRM relay on the VPS forwards traffic so joiners need no port-forwarding.
        /// The relay assigns the room's serverId only after StartHost, so we host first, wait for
        /// the id, then register the lobby with it. Shown in public search unless private.</summary>
        public async void CreateSessionRequest(string sessionName, bool isPrivate, string password = null)
        {
            if (m_MasterServerFacade == null)
            {
                Debug.LogWarning("[SessionUI] MasterServerFacade not available.");
                return;
            }
            SetSpinner(true);
            string name = ResolveSessionName(sessionName);

            // The LRM transport connects to the relay on Awake; make sure it's up before hosting.
            m_ConnectionManager.EnsureRelayConnected();
            if (!await WaitForRelayReadyAsync(8f))
            {
                SetSpinner(false);
                ShowToast("Could not reach the relay server.\nCheck your connection and try again.", 5f);
                return;
            }

            // Host via relay. serverId is assigned asynchronously once the relay creates our room.
            m_ConnectionManager.StartHostRelay(m_MasterServerFacade.Username);

            string serverId = await WaitForRelayServerIdAsync(8f);
            if (string.IsNullOrEmpty(serverId))
            {
                SetSpinner(false);
                m_ConnectionManager.RequestShutdown();
                ShowToast("The relay didn't assign a room id.\nPlease try again.", 5f);
                return;
            }

            var lobby = await m_MasterServerFacade.CreateRelayLobbyAsync(name, serverId, 8, isPrivate, password);
            SetSpinner(false);
            if (lobby == null)
            {
                // Name clash or master unreachable — tear down the half-started host.
                m_ConnectionManager.RequestShutdown();
                ShowToast("Could not create room.\nThat name may already be taken — try another.", 5f);
            }
        }

        // Polls until the LRM transport is authenticated by the relay (or times out).
        async Task<bool> WaitForRelayReadyAsync(float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (m_ConnectionManager.IsRelayReady) return true;
                await Task.Delay(50);
            }
            return m_ConnectionManager.IsRelayReady;
        }

        // Polls until the relay assigns this host a room serverId (or times out).
        async Task<string> WaitForRelayServerIdAsync(float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                string id = m_ConnectionManager.RelayServerId;
                if (!string.IsNullOrEmpty(id)) return id;
                await Task.Delay(50);
            }
            return m_ConnectionManager.RelayServerId;
        }

        // Connects a joiner using the right transport for the lobby type: relay lobbies use the
        // LRM serverId, dedicated/IP lobbies use host_ip/host_port.
        //
        // Relay joins wait for IsRelayReady first (same as the host path in CreateSessionRequest)
        // because LightReflectiveMirrorTransport.ClientConnect only checks Available() (raw KCP
        // connect), not authentication. If StartClientRelay fires before the relay auth handshake
        // (AuthenticationRequest/Response/Authenticated) finishes, the relay is still tracking this
        // client as pending-authentication and silently drops the JoinServer opcode (HandleMessage
        // ignores every opcode but AuthenticationResponse while pending) — the joiner then just sits
        // connected-but-stuck until its own client-side timeout fires, which reads as an endless
        // "retrying to join" loop that eventually fills the room with abandoned slots (see VersusD
        // project memory "relay-lrm-migration").
        async Task ConnectFromJoin(JoinResponse join)
        {
            if (join.is_relay)
            {
                m_ConnectionManager.EnsureRelayConnected();
                if (!await WaitForRelayReadyAsync(8f))
                {
                    ShowToast("Could not reach the relay server.\nCheck your connection and try again.", 5f);
                    return;
                }
                m_ConnectionManager.StartClientRelay(m_MasterServerFacade.Username, join.relay_server_id, join.join_token, join.session_id);
            }
            else
            {
                m_ConnectionManager.StartClientIp(m_MasterServerFacade.Username, join.host_ip, join.host_port, join.join_token, join.session_id);
            }
        }

        /// <summary>Whether the dedicated-server option should be offered (mirrors
        /// MasterServerConfig.enableDedicatedServers). Read by SessionCreationUI.</summary>
        public bool DedicatedServersEnabled =>
            m_MasterServerFacade != null && m_MasterServerFacade.EnableDedicatedServers;

        // Called by SessionJoiningUI after resolving the selected lobby + optional password.
        public async void JoinLobbyRequest(LobbyResponse lobby, string password)
        {
            if (m_MasterServerFacade == null)
            {
                Debug.LogWarning("[SessionUI] MasterServerFacade not available.");
                return;
            }
            SetSpinner(true);

            var join = await m_MasterServerFacade.JoinLobbyAsync(lobby.session_id, password);

            SetSpinner(false);
            if (join == null) return;

            await ConnectFromJoin(join);
        }

        // Direct join by session ID (typed into the join-code field).
        public async void JoinSessionWithCodeRequest(string sessionCode)
        {
            if (m_MasterServerFacade == null)
            {
                Debug.LogWarning("[SessionUI] MasterServerFacade not available.");
                return;
            }
            SetSpinner(true);

            var join = await m_MasterServerFacade.JoinLobbyAsync(sessionCode);

            SetSpinner(false);
            if (join == null) return;

            await ConnectFromJoin(join);
        }

        public async void QuerySessionRequest(bool blockUI)
        {
            if (m_MasterServerFacade == null) return;
            if (blockUI) SetSpinner(true);

            var lobbies = await m_MasterServerFacade.QueryLobbiesAsync();

            SetSpinner(false);
            m_SessionJoiningUI?.PopulateLobbies(lobbies);
        }

        public async void QuickJoinRequest()
        {
            if (m_MasterServerFacade == null) return;
            SetSpinner(true);

            var lobbies = await m_MasterServerFacade.QueryLobbiesAsync();

            foreach (var lobby in lobbies)
            {
                if (!lobby.is_private && lobby.current_players < lobby.max_players)
                {
                    var join = await m_MasterServerFacade.JoinLobbyAsync(lobby.session_id);
                    SetSpinner(false);
                    if (join != null)
                        await ConnectFromJoin(join);
                    return;
                }
            }

            SetSpinner(false);
            Debug.Log("[SessionUI] No available public lobbies for quick join.");
        }

        void SetSpinner(bool active)
        {
            if (m_LoadingSpinner) m_LoadingSpinner.SetActive(active);
        }
    }
}
