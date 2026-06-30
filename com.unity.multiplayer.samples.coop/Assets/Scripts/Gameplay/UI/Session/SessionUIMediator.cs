using System.Net;
using System.Net.Sockets;
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
            string name = string.IsNullOrEmpty(sessionName) ? "Room" : sessionName;
            var lobby = await m_MasterServerFacade.CreateDedicatedLobbyAsync(name, 8, isPrivate, password);
            SetSpinner(false);

            if (lobby != null)
            {
                // Dedicated server allocated — join the lobby to get a token, then connect.
                var join = await m_MasterServerFacade.JoinLobbyAsync(lobby.session_id);
                if (join == null) return;
                m_ConnectionManager.StartClientIp(m_MasterServerFacade.Username, join.host_ip, join.host_port, join.join_token, join.session_id);
                return;
            }

            if (!m_MasterServerFacade.LastErrorWasServerUnavailable)
            {
                // Not a capacity problem (e.g. duplicate room name, 409) — don't
                // fall back to P2P; tell the player so they can pick a new name.
                ShowToast("Could not create room.\nThat name may already be taken — try another.", 5f);
                return;
            }

            // Fallback: host the room ourselves (P2P)
            Debug.Log("[SessionUI] No dedicated servers available — falling back to P2P.");
            ShowFallbackNotice();
            SetSpinner(true);
            string localIp = GetLocalIpAddress();
            const int port = 9998;
            var p2pLobby = await m_MasterServerFacade.CreateLobbyAsync(name, localIp, port, 8, isPrivate, password);
            SetSpinner(false);
            if (p2pLobby == null) return;
            m_ConnectionManager.StartHostIp(m_MasterServerFacade.Username, localIp, port);
        }

        void ShowFallbackNotice()
        {
            Debug.Log("[SessionUI] No dedicated servers available — falling back to P2P host.");
            ShowToast("No dedicated servers available.\nCreating a P2P room instead.", 5f);
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

        /// <summary>Creates a P2P room hosted on the player's own machine (not visible in public search).</summary>
        public async void CreateSessionRequest(string sessionName, bool isPrivate, string password = null)
        {
            if (m_MasterServerFacade == null)
            {
                Debug.LogWarning("[SessionUI] MasterServerFacade not available.");
                return;
            }
            SetSpinner(true);

            string localIp = GetLocalIpAddress();
            const int port = 9998;

            var lobby = await m_MasterServerFacade.CreateLobbyAsync(
                string.IsNullOrEmpty(sessionName) ? "Room" : sessionName,
                localIp, port, 8, isPrivate, password);

            SetSpinner(false);
            if (lobby == null)
            {
                ShowToast("Could not create room.\nThat name may already be taken — try another.", 5f);
                return;
            }

            m_ConnectionManager.StartHostIp(m_MasterServerFacade.Username, localIp, port);
        }

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

            m_ConnectionManager.StartClientIp(m_MasterServerFacade.Username, join.host_ip, join.host_port, join.join_token, join.session_id);
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

            m_ConnectionManager.StartClientIp(m_MasterServerFacade.Username, join.host_ip, join.host_port, join.join_token, join.session_id);
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
                        m_ConnectionManager.StartClientIp(m_MasterServerFacade.Username, join.host_ip, join.host_port, join.join_token, join.session_id);
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

        // Uses a UDP socket trick to find the LAN IP that would route to the internet.
        static string GetLocalIpAddress()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                socket.Connect("8.8.8.8", 65530);
                var ep = socket.LocalEndPoint as IPEndPoint;
                return ep?.Address.ToString() ?? "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }
    }
}
