using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Unity.BossRoom.MasterServer
{
    /// <summary>
    /// High-level facade over the Master Server REST API.
    /// Replaces both AuthenticationServiceFacade and MultiplayerServicesFacade.
    /// Register as a singleton in your DI container (VContainer LifetimeScope).
    /// </summary>
    public class MasterServerFacade
    {
        readonly MasterServerClient m_Client;

        public bool IsAuthenticated => m_Client.IsAuthenticated;
        public string PlayerId => m_Client.PlayerId;
        public string Username => m_Client.Username;
        public string CurrentSessionId { get; private set; }
        public LobbyResponse CurrentLobby { get; private set; }
        public bool LastErrorWasServerUnavailable { get; private set; }

        public event Action<string> OnError;

        public MasterServerFacade(MasterServerConfig config)
        {
            m_Client = new MasterServerClient(config.baseUrl);
        }

        /// <summary>Set the dedicated-server shared secret used for privileged endpoints
        /// (/servers/*, /stats/match-result). Called only by the dedicated server process.</summary>
        public void SetServerSecret(string secret) => m_Client.SetServerSecret(secret);

        // ── Auth ──────────────────────────────────────────────────────────────

        public async Task<bool> LoginAnonymouslyAsync()
        {
            try
            {
                var response = await m_Client.GuestLoginAsync();
                m_Client.SetToken(response.access_token, response.player_id, response.username);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MasterServer] Guest login failed: {e.Message}");
                OnError?.Invoke(e.Message);
                return false;
            }
        }

        public async Task<bool> LoginAsync(string username, string password)
        {
            try
            {
                var response = await m_Client.LoginAsync(username, password);
                m_Client.SetToken(response.access_token, response.player_id, response.username);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MasterServer] Login failed: {e.Message}");
                OnError?.Invoke(e.Message);
                return false;
            }
        }

        public async Task<bool> RegisterAsync(string username, string password)
        {
            try
            {
                var response = await m_Client.RegisterAsync(username, password);
                m_Client.SetToken(response.access_token, response.player_id, response.username);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MasterServer] Register failed: {e.Message}");
                OnError?.Invoke(e.Message);
                return false;
            }
        }

        // ── Lobby ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the lobby list on success, an empty array when there are no rooms,
        /// or null when the server could not be reached (to distinguish from "genuinely empty").
        /// </summary>
        public async Task<LobbyResponse[]> QueryLobbiesAsync()
        {
            try
            {
                return await m_Client.GetLobbiesAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"[MasterServer] Query lobbies failed: {e.Message}");
                OnError?.Invoke(e.Message);
                return null; // null = server unreachable; [] = connected but no rooms
            }
        }

        /// <summary>Creates a lobby and registers the host's KCP endpoint with the Master Server.</summary>
        public async Task<LobbyResponse> CreateLobbyAsync(string name, string hostIp, int hostPort, int maxPlayers = 8, bool isPrivate = false, string password = null)
        {
            try
            {
                var lobby = await m_Client.CreateLobbyAsync(new CreateLobbyRequest
                {
                    name = name,
                    host_ip = hostIp,
                    host_port = hostPort,
                    max_players = maxPlayers,
                    is_private = isPrivate,
                    password = password,
                });
                CurrentLobby = lobby;
                CurrentSessionId = lobby.session_id;
                _ = HeartbeatLoopAsync(lobby.session_id);
                return lobby;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MasterServer] Create lobby failed: {e.Message}");
                OnError?.Invoke(e.Message);
                return null;
            }
        }

        /// <summary>Joins a lobby. Returns connection info including the join_token for the game server auth.
        /// Pass the password for private lobbies.</summary>
        public async Task<JoinResponse> JoinLobbyAsync(string sessionId, string password = null)
        {
            try
            {
                var join = await m_Client.JoinLobbyAsync(sessionId, password);
                CurrentSessionId = sessionId;
                return join;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MasterServer] Join lobby failed: {e.Message}");
                OnError?.Invoke(e.Message);
                return null;
            }
        }

        public async Task LeaveLobbyAsync()
        {
            if (string.IsNullOrEmpty(CurrentSessionId)) return;
            try
            {
                await m_Client.LeaveLobbyAsync(CurrentSessionId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MasterServer] Leave lobby failed: {e.Message}");
            }
            finally
            {
                CurrentSessionId = null;
                CurrentLobby = null;
            }
        }

        // ── Host lobby heartbeat ──────────────────────────────────────────────

        bool m_StopHeartbeat;

        async Task HeartbeatLoopAsync(string sessionId)
        {
            m_StopHeartbeat = false;
            while (!m_StopHeartbeat)
            {
                await Task.Delay(TimeSpan.FromSeconds(15));
                if (m_StopHeartbeat) break;
                try
                {
                    await m_Client.LobbyHeartbeatAsync(sessionId);
                }
                catch
                {
                    // heartbeat failure is non-fatal; lobby TTL will clean it up
                }
            }
        }

        public void StopHeartbeat() => m_StopHeartbeat = true;

        // ── Dedicated server registration ─────────────────────────────────────

        public async Task<RegisterServerResponse> RegisterDedicatedServerAsync(string ip, int port, int maxPlayers = 8)
        {
            try
            {
                return await m_Client.RegisterServerAsync(new RegisterServerRequest
                {
                    ip = ip,
                    port = port,
                    max_players = maxPlayers,
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"[MasterServer] Server registration failed: {e.Message}");
                return null;
            }
        }

        public async Task ServerHeartbeatAsync(string serverId)
        {
            try
            {
                await m_Client.ServerHeartbeatAsync(serverId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MasterServer] Server heartbeat failed: {e.Message}");
            }
        }

        public async Task ServerUnregisterAsync(string serverId)
        {
            try
            {
                await m_Client.ServerUnregisterAsync(serverId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MasterServer] Server unregister failed: {e.Message}");
            }
        }

        public async Task<ServerAllocationResponse> GetServerAllocationAsync(string serverId)
        {
            try
            {
                return await m_Client.GetServerAllocationAsync(serverId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MasterServer] Allocation poll failed: {e.Message}");
                return null;
            }
        }

        public async Task LobbyHeartbeatAsync(string sessionId)
        {
            try { await m_Client.LobbyHeartbeatAsync(sessionId); }
            catch { /* non-fatal */ }
        }

        /// <summary>Creates a lobby on an available dedicated VPS server.
        /// Returns null and sets LastErrorWasServerUnavailable=true when the VPS is at capacity.</summary>
        public async Task<LobbyResponse> CreateDedicatedLobbyAsync(string name, int maxPlayers = 8, bool isPrivate = false, string password = null)
        {
            LastErrorWasServerUnavailable = false;
            try
            {
                var lobby = await m_Client.CreateDedicatedLobbyAsync(new CreateDedicatedLobbyRequest
                {
                    name = string.IsNullOrEmpty(name) ? "Room" : name,
                    max_players = maxPlayers,
                    is_private = isPrivate,
                    password = password,
                });
                CurrentLobby = lobby;
                CurrentSessionId = lobby?.session_id;
                if (lobby != null) _ = HeartbeatLoopAsync(lobby.session_id);
                return lobby;
            }
            catch (Exception e)
            {
                LastErrorWasServerUnavailable = e.Message.Contains("SERVER_UNAVAILABLE") || e.Message.Contains("503");
                if (!LastErrorWasServerUnavailable)
                {
                    Debug.LogError($"[MasterServer] Create dedicated lobby failed: {e.Message}");
                    OnError?.Invoke(e.Message);
                }
                return null;
            }
        }

        // ── Stats ─────────────────────────────────────────────────────────────

        /// <summary>Reports a finished match: every player gets +1 played, the winner +1 won.</summary>
        public async Task SubmitMatchResultAsync(string[] playerIds, string winnerPlayerId)
        {
            try
            {
                await m_Client.SubmitMatchResultAsync(new MatchResultRequest
                {
                    player_ids = playerIds,
                    winner_player_id = winnerPlayerId,
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"[MasterServer] Submit match result failed: {e.Message}");
                OnError?.Invoke(e.Message);
            }
        }

        /// <summary>Fetches a player's lifetime stats. Returns null on failure.</summary>
        public async Task<StatsResponse> GetStatsAsync(string playerId)
        {
            try
            {
                return await m_Client.GetStatsAsync(playerId);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MasterServer] Get stats failed: {e.Message}");
                OnError?.Invoke(e.Message);
                return null;
            }
        }
    }
}
