"""Request/response models.

Field names MUST match the Unity client's [Serializable] DTOs in
Assets/Scripts/MasterServer/MasterServerModels.cs, because the client
deserializes with JsonUtility (no name remapping).
"""

from pydantic import BaseModel, Field


# ── Auth ────────────────────────────────────────────────────────────────────


class AuthRequest(BaseModel):
    username: str = Field(min_length=1, max_length=32)
    password: str = Field(min_length=1, max_length=128)


class TokenResponse(BaseModel):
    access_token: str
    token_type: str = "bearer"
    player_id: str
    username: str


# ── Lobby ───────────────────────────────────────────────────────────────────


class CreateLobbyRequest(BaseModel):
    name: str = Field(min_length=1, max_length=64)
    host_ip: str
    host_port: int
    max_players: int = 8
    is_private: bool = False
    # Extension over the current Unity DTO; optional password for private lobbies.
    password: str | None = None


class CreateDedicatedLobbyRequest(BaseModel):
    name: str = Field(min_length=1, max_length=64)
    max_players: int = 8
    is_private: bool = False
    password: str | None = None


class LobbyResponse(BaseModel):
    session_id: str
    name: str
    host_player_id: str
    host_ip: str
    host_port: int
    max_players: int
    current_players: int
    is_private: bool
    is_dedicated: bool = False


class LobbyListResponse(BaseModel):
    # Wrapped in an object (not a bare array) because the Unity client
    # deserializes with JsonUtility, which can't parse a top-level JSON array.
    lobbies: list[LobbyResponse]


class JoinLobbyRequest(BaseModel):
    # Optional; required only when joining a private lobby.
    password: str | None = None


class JoinResponse(BaseModel):
    session_id: str
    host_ip: str
    host_port: int
    join_token: str


# ── Servers ─────────────────────────────────────────────────────────────────


class RegisterServerRequest(BaseModel):
    ip: str
    port: int
    max_players: int = 8


class ServerResponse(BaseModel):
    server_id: str
    ip: str
    port: int
    current_players: int
    max_players: int


class ServerAllocationResponse(BaseModel):
    allocated: bool
    session_id: str | None = None
    lobby_name: str | None = None
    max_players: int = 8


class ValidateJoinTokenRequest(BaseModel):
    join_token: str
    session_id: str | None = None
    # When False the token is only checked ("peeked"), not consumed, so a client
    # rejected by a later gameplay gate (capacity / build-type) can reconnect with
    # the same token. The game server consumes it (consume=True) once the player
    # actually passes approval and is seated.
    consume: bool = True


class ValidateJoinTokenResponse(BaseModel):
    valid: bool
    player_id: str | None = None
    session_id: str | None = None


# ── Stats ───────────────────────────────────────────────────────────────────


class MatchResultRequest(BaseModel):
    # Every listed player gets games_played += 1; the winner also games_won += 1.
    player_ids: list[str]
    winner_player_id: str | None = None


class StatsResponse(BaseModel):
    player_id: str
    username: str
    games_played: int
    games_won: int
