using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Unity.BossRoom.MasterServer
{
    /// <summary>
    /// Low-level HTTP client for the Master Server REST API.
    /// Uses UnityWebRequest so it works on all Unity platforms.
    /// </summary>
    public class MasterServerClient
    {
        readonly string m_BaseUrl;
        string m_AccessToken;
        string m_ServerSecret;

        public string PlayerId { get; private set; }
        public string Username { get; private set; }
        public bool IsAuthenticated => !string.IsNullOrEmpty(m_AccessToken);

        public MasterServerClient(string baseUrl)
        {
            m_BaseUrl = baseUrl.TrimEnd('/');
        }


        public void SetToken(string token, string playerId, string username)
        {
            m_AccessToken = token;
            PlayerId = playerId;
            Username = username;
        }

        /// <summary>Set the shared secret a dedicated game server presents on privileged
        /// (/servers/*, /stats/match-result) endpoints. Players never set this.</summary>
        public void SetServerSecret(string secret) => m_ServerSecret = secret;

        // ── Auth ──────────────────────────────────────────────────────────────

        public Task<TokenResponse> RegisterAsync(string username, string password) =>
            PostAsync<TokenResponse>("/auth/register",
                JsonUtility.ToJson(new AuthRequest { username = username, password = password }));

        public Task<TokenResponse> LoginAsync(string username, string password) =>
            PostAsync<TokenResponse>("/auth/login",
                JsonUtility.ToJson(new AuthRequest { username = username, password = password }));

        public Task<TokenResponse> GuestLoginAsync() =>
            PostAsync<TokenResponse>("/auth/guest", "{}");

        // ── Lobby ─────────────────────────────────────────────────────────────

        public async Task<LobbyResponse[]> GetLobbiesAsync()
        {
            // Server wraps the list in an object because JsonUtility can't parse a top-level array.
            var wrapped = await GetAsync<LobbyListResponse>("/lobby");
            return wrapped?.lobbies ?? Array.Empty<LobbyResponse>();
        }

        public Task<LobbyResponse> CreateLobbyAsync(CreateLobbyRequest req) =>
            PostAsync<LobbyResponse>("/lobby", JsonUtility.ToJson(req));

        public Task<JoinResponse> JoinLobbyAsync(string sessionId, string password = null) =>
            PostAsync<JoinResponse>($"/lobby/{sessionId}/join",
                JsonUtility.ToJson(new JoinLobbyRequest { password = password }));

        public Task LeaveLobbyAsync(string sessionId) =>
            DeleteAsync($"/lobby/{sessionId}/leave");

        public Task LobbyHeartbeatAsync(string sessionId) =>
            PostAsync<object>($"/lobby/{sessionId}/heartbeat", "{}");

        // ── Servers ───────────────────────────────────────────────────────────

        public Task<RegisterServerResponse> RegisterServerAsync(RegisterServerRequest req) =>
            PostAsync<RegisterServerResponse>("/servers/register", JsonUtility.ToJson(req));

        public Task ServerHeartbeatAsync(string serverId) =>
            PutAsync($"/servers/{serverId}/heartbeat");

        public Task ServerUnregisterAsync(string serverId) =>
            DeleteAsync($"/servers/{serverId}");

        public Task<ServerAllocationResponse> GetServerAllocationAsync(string serverId) =>
            GetAsync<ServerAllocationResponse>($"/servers/{serverId}/allocation");

        public Task<LobbyResponse> CreateDedicatedLobbyAsync(CreateDedicatedLobbyRequest req) =>
            PostAsync<LobbyResponse>("/lobby/dedicated", JsonUtility.ToJson(req));

        // ── Stats ─────────────────────────────────────────────────────────────

        public Task SubmitMatchResultAsync(MatchResultRequest req) =>
            PostAsync<object>("/stats/match-result", JsonUtility.ToJson(req));

        public Task<StatsResponse> GetStatsAsync(string playerId) =>
            GetAsync<StatsResponse>($"/stats/{playerId}");

        // ── HTTP helpers ──────────────────────────────────────────────────────

        async Task<T> GetAsync<T>(string path)
        {
            using var req = UnityWebRequest.Get(m_BaseUrl + path);
            AddAuthHeader(req);
            await SendAsync(req);
            return JsonUtility.FromJson<T>(req.downloadHandler.text);
        }

        async Task<T> PostAsync<T>(string path, string json)
        {
            using var req = new UnityWebRequest(m_BaseUrl + path, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            AddAuthHeader(req);
            await SendAsync(req);
            if (typeof(T) == typeof(object)) return default;
            return JsonUtility.FromJson<T>(req.downloadHandler.text);
        }

        async Task PutAsync(string path)
        {
            using var req = new UnityWebRequest(m_BaseUrl + path, "PUT");
            req.downloadHandler = new DownloadHandlerBuffer();
            AddAuthHeader(req);
            await SendAsync(req);
        }

        async Task DeleteAsync(string path)
        {
            using var req = UnityWebRequest.Delete(m_BaseUrl + path);
            req.downloadHandler = new DownloadHandlerBuffer();
            AddAuthHeader(req);
            await SendAsync(req);
        }

        void AddAuthHeader(UnityWebRequest req)
        {
            if (!string.IsNullOrEmpty(m_AccessToken))
                req.SetRequestHeader("Authorization", $"Bearer {m_AccessToken}");
            // Dedicated servers also present the shared secret for privileged endpoints.
            if (!string.IsNullOrEmpty(m_ServerSecret))
                req.SetRequestHeader("X-Server-Secret", m_ServerSecret);
        }

        static Task SendAsync(UnityWebRequest req)
        {
            var tcs = new TaskCompletionSource<bool>();
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                if (req.result == UnityWebRequest.Result.Success || req.responseCode is >= 200 and < 300)
                    tcs.SetResult(true);
                else
                    tcs.SetException(new Exception($"HTTP {req.responseCode}: {req.downloadHandler?.text}"));
            };
            return tcs.Task;
        }
    }
}
