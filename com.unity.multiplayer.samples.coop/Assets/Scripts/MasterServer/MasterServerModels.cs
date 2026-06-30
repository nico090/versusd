using System;

namespace Unity.BossRoom.MasterServer
{
    [Serializable]
    public class AuthRequest
    {
        public string username;
        public string password;
    }

    [Serializable]
    public class TokenResponse
    {
        public string access_token;
        public string token_type;
        public string player_id;
        public string username;
    }

    [Serializable]
    public class LobbyResponse
    {
        public string session_id;
        public string name;
        public string host_player_id;
        public string host_ip;
        public int host_port;
        public int max_players;
        public int current_players;
        public bool is_private;
        public bool is_dedicated;
    }

    [Serializable]
    public class LobbyListResponse
    {
        public LobbyResponse[] lobbies;
    }

    [Serializable]
    public class JoinResponse
    {
        public string session_id;
        public string host_ip;
        public int host_port;
        public string join_token;
    }

    [Serializable]
    public class CreateLobbyRequest
    {
        public string name;
        public string host_ip;
        public int host_port;
        public int max_players = 8;
        public bool is_private = false;
        // Optional; required by the server when is_private is true.
        public string password;
    }

    [Serializable]
    public class JoinLobbyRequest
    {
        // Optional; required only when joining a private lobby.
        public string password;
    }

    [Serializable]
    public class RegisterServerRequest
    {
        public string ip;
        public int port;
        public int max_players = 8;
    }

    [Serializable]
    public class RegisterServerResponse
    {
        public string server_id;
        public string ip;
        public int port;
    }

    [Serializable]
    public class CreateDedicatedLobbyRequest
    {
        public string name;
        public int max_players = 8;
        public bool is_private;
        public string password;
    }

    [Serializable]
    public class ValidateJoinTokenRequest
    {
        public string join_token;
        // Optional; when set the server checks the token belongs to this lobby.
        public string session_id;
        // False = peek (validate without consuming) so a client bounced by a later
        // gameplay gate can reconnect. True = consume the single-use token.
        public bool consume;
    }

    [Serializable]
    public class ValidateJoinTokenResponse
    {
        public bool valid;
        public string player_id;
        public string session_id;
    }

    // ── Stats ─────────────────────────────────────────────────────────────────

    [Serializable]
    public class MatchResultRequest
    {
        // Every listed player gets games_played += 1; the winner also games_won += 1.
        public string[] player_ids;
        public string winner_player_id;
    }

    [Serializable]
    public class StatsResponse
    {
        public string player_id;
        public string username;
        public int games_played;
        public int games_won;
    }

    [Serializable]
    public class ServerAllocationResponse
    {
        public bool allocated;
        public string session_id;
        public string lobby_name;
        public int max_players;
    }
}
