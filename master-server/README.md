# BossRoom Master Server (FastAPI)

REST backend for the PvP deathmatch conversion of Unity BossRoom. Serves the
contract consumed by the Unity client in
`Assets/Scripts/MasterServer/` (`MasterServerClient` / `MasterServerFacade`):
auth, a lobby browser (public + password-protected private lobbies), dedicated
server registration, join-token validation, and player stats.

## Stack

- FastAPI + Uvicorn
- MongoDB (PyMongo) — set `MONGO_URL` / `DB_NAME`; tests use mongomock
- JWT access tokens (python-jose), bcrypt password hashing (passlib)

## Run (dev)

```bash
cd master-server
python -m venv .venv
# Windows PowerShell: .venv\Scripts\Activate.ps1
source .venv/bin/activate
pip install -r requirements.txt
cp .env.example .env          # then edit SECRET_KEY
uvicorn app.main:app --reload --host 0.0.0.0 --port 8000
```

Requires a running MongoDB reachable at `MONGO_URL` (default
`mongodb://localhost:27017`). For a one-shot local instance:
`docker run -d -p 27017:27017 --name mongo mongo:7`. In production `docker compose
up` starts both the master server and a `mongo` service automatically.

Interactive docs: http://localhost:8000/docs

The Unity client points at this via `MasterServerConfig.baseUrl`
(default `http://localhost:8000`).

## Endpoints

### Auth
| Method | Path             | Body                          | Returns        |
|--------|------------------|-------------------------------|----------------|
| POST   | `/auth/register` | `{username, password}`        | `TokenResponse`|
| POST   | `/auth/login`    | `{username, password}`        | `TokenResponse`|
| POST   | `/auth/guest`    | `{}`                          | `TokenResponse`|

`TokenResponse = {access_token, token_type, player_id, username}`. Send the
token as `Authorization: Bearer <access_token>` on every other call.

### Lobby
| Method | Path                        | Body                                                   | Returns         |
|--------|-----------------------------|--------------------------------------------------------|-----------------|
| GET    | `/lobby`                    | —                                                      | `{lobbies: [LobbyResponse]}` (public only) |
| POST   | `/lobby`                    | `{name, host_ip, host_port, max_players, is_private, password?}` | `LobbyResponse` |
| POST   | `/lobby/{id}/join`          | `{password?}`                                          | `JoinResponse`  |
| DELETE | `/lobby/{id}/leave`         | —                                                      | 204             |
| POST   | `/lobby/{id}/heartbeat`     | —                                                      | 204             |

Private lobbies require a password at create time and validate it on join.
Lobbies with no heartbeat for `LOBBY_TTL_SECONDS` are pruned. `JoinResponse`
returns a single-use `join_token`.

### Servers
| Method | Path                            | Body                          | Returns          |
|--------|---------------------------------|-------------------------------|------------------|
| POST   | `/servers/register`             | `{ip, port, max_players}`     | `ServerResponse` |
| PUT    | `/servers/{id}/heartbeat`       | —                             | 204              |
| POST   | `/servers/validate-join-token`  | `{join_token, session_id?}`   | `{valid, player_id, session_id}` |

`validate-join-token` is called by the game server during the connection
handshake. Tokens are single-use (consumed on a successful validation).

### Stats
| Method | Path                    | Body                                  | Returns        |
|--------|-------------------------|---------------------------------------|----------------|
| POST   | `/stats/match-result`   | `{player_ids: [...], winner_player_id?}` | 204         |
| GET    | `/stats/{player_id}`    | —                                     | `StatsResponse`|

`match-result`: every listed player gets `games_played += 1`; the winner also
`games_won += 1`. `StatsResponse = {player_id, username, games_played, games_won}`.

## Client deserialization note

`GET /lobby` returns `{"lobbies": [...]}` rather than a bare array, because the
Unity client deserializes with `JsonUtility`, which cannot parse a top-level
JSON array. The client unwraps via `LobbyListResponse` and returns
`LobbyResponse[]` from `MasterServerClient.GetLobbiesAsync`.
