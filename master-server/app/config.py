from pydantic_settings import BaseSettings, SettingsConfigDict

# Placeholder values that must never be used in a real deployment. Startup aborts
# (see app.security.assert_secrets_configured) if any secret still equals these.
INSECURE_SECRET_PLACEHOLDERS = {
    "dev-insecure-change-me",
    "change-me-in-prod",
    "",
}


class Settings(BaseSettings):
    """Runtime configuration, read from environment / .env file."""

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    secret_key: str = "dev-insecure-change-me"
    algorithm: str = "HS256"
    access_token_expire_minutes: int = 1440

    # Shared secret presented by game servers (X-Server-Secret header) to call the
    # privileged /servers/* and /stats/match-result endpoints. Must be set in prod.
    server_shared_secret: str = "dev-insecure-change-me"

    # MongoDB connection string and database name.
    mongo_url: str = "mongodb://localhost:27017"
    db_name: str = "versused"

    # Seconds without a heartbeat before a lobby/server is pruned.
    lobby_ttl_seconds: int = 45
    # Seconds without a heartbeat before a *server* doc is considered dead (crash
    # recovery; longer than lobby TTL so a brief stall doesn't free a live port).
    server_ttl_seconds: int = 60
    # Minutes a join_token stays valid after being issued.
    join_token_expire_minutes: int = 10

    # Set false (or DEV_ALLOW_INSECURE=1) only for local development without auth.
    require_secure_secrets: bool = True

    # ── Abuse / resource-exhaustion limits ──────────────────────────────────────
    # Auth endpoints: attempts per client IP within the window.
    auth_rate_limit_max: int = 10
    auth_rate_limit_window_seconds: float = 60.0
    # Lobby mutating endpoints (create / join): requests per client IP within the window.
    lobby_rate_limit_max: int = 20
    lobby_rate_limit_window_seconds: float = 60.0
    # Max dedicated lobbies a single player may own at once (caps container spawns
    # driven by one account — see POST /lobby/dedicated).
    max_dedicated_lobbies_per_player: int = 1
    # Hard cap on concurrent dedicated game-server containers across the whole fleet.
    max_concurrent_game_servers: int = 50
    # Guest accounts are pruned this many hours after creation (0 disables pruning).
    guest_ttl_hours: int = 48

    # ── On-demand dedicated game-server spawning ────────────────────────────────
    vps_public_ip: str = "127.0.0.1"
    game_server_image: str = "versused-game-server"
    game_server_port_start: int = 9000
    game_server_port_end: int = 9999
    docker_network: str = "master-server_versused"
    master_server_internal_url: str = "http://master-server:8000"
    # How long (seconds) to wait for a freshly spawned container to register.
    container_spawn_timeout_seconds: float = 20.0


settings = Settings()
