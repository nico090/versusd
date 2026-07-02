# Security & hardening notes

Companion to `PLAN_1.0.md` (repo root). Summarises the controls in place and how
to deploy the master server safely. Items map to the SEG-* findings in the plan.

## Production checklist

- [ ] **Set real secrets** (`SECRET_KEY`, `SERVER_SHARED_SECRET`). Generate with
      `python -c "import secrets; print(secrets.token_hex(32))"`. The app refuses to
      start with placeholder values while `REQUIRE_SECURE_SECRETS=true` (the default).
      **Never** copy the local `.env` (which sets `REQUIRE_SECURE_SECRETS=false`) to a
      server. *(SEG-9)*
- [ ] **Terminate TLS** in front of the server. Use the prod overlay:
      `docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d`.
      Caddy auto-provisions a Let's Encrypt cert for `MASTER_DOMAIN` and the base
      `8000` port stops being published. Point the Unity client at `https://…`
      (`MasterServerConfig.baseUrl`). *(SEG-4)*
- [ ] **Run a single worker** (`uvicorn … ` with no `--workers`, or `--workers 1`).
      The rate limiter is in-process; multiple workers multiply the effective limit.
      To scale out, move the limiter to a shared store (Redis). *(SEG-7)*
- [ ] **Don't mount the raw Docker socket.** The prod overlay routes container
      spawns through `docker-socket-proxy` (least-privilege API gateway) and sets
      `DOCKER_HOST` accordingly, so an RCE in the master server can't drive the full
      host daemon. *(SEG-8)*
- [ ] **Open only the game-server UDP range** (`GAME_SERVER_PORT_START..END`) on the
      VPS firewall, plus 443 for Caddy.

## Controls already in code

| Control | Where | Finding |
|---|---|---|
| Auth throttle (per IP) | `ratelimit.auth_rate_limit` on `/auth/*` | — |
| Lobby create/join throttle (per IP) | `ratelimit.lobby_rate_limit` | SEG-5 |
| Per-account dedicated-lobby quota | `routers/lobby.create_dedicated_lobby` | SEG-5 |
| Fleet-wide container cap | `routers/lobby.create_dedicated_lobby` | SEG-5 |
| Guest auto-expiry (TTL index) | `models.User.guest_expires_at`, `database.create_indexes` | SEG-6 |
| Constant-time server-secret compare | `security.require_server` | — |
| Startup guard vs placeholder secrets | `security.assert_secrets_configured` | SEG-9 |
| Connect-payload size cap (anti-DoS) | `HostingState.k_MaxConnectPayload` (Unity) | — |
| Single-use, short-TTL join tokens | `routers/servers.validate_join_token` | SEG-10 |

All limits are tunable via env (see `.env.example`).

## Accepted risks

- **`POST /servers/validate-join-token` is unauthenticated** *(SEG-10)*. P2P hosts
  have no server secret yet must validate tokens, so the endpoint can't require one.
  Mitigated by tokens being unguessable `uuid4`, single-use, and short-lived
  (`JOIN_TOKEN_EXPIRE_MINUTES`). Requiring the secret for `consume=true` was
  considered but would break P2P consumption; revisit if dedicated-only.
- **`isDebug` is client-declared** *(SEG-3)*. Only affects P2P build-type
  compatibility today; god-mode is `#if UNITY_EDITOR || DEVELOPMENT_BUILD`-gated and
  compiled out of the release dedicated-server build. Don't gate new privileged
  behaviour on `isDebug`.

## Game-server (Unity) hardening

- `CmdPlayAction` validates the client-supplied `ActionID` via
  `TryGetActionPrototypeByID` before use — an out-of-range id can no longer crash the
  headless dedicated server. *(SEG-1)*
- Movement commands reject non-finite (`NaN`/`Infinity`) vectors before they reach
  the NavMesh/physics layer. *(SEG-2)*
- Master-server HTTP calls from the client and the authenticator carry a 15s timeout
  so a stalled request can't wedge the connection/heartbeat loops. *(MEJ-3)*
