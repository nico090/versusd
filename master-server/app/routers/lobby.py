import asyncio
import logging
import uuid
from datetime import datetime, timedelta, timezone

from fastapi import APIRouter, Depends, HTTPException, status
from pymongo.database import Database

from ..config import settings
from ..database import get_db
from ..models import GameServer, JoinToken, Lobby, User, utcnow
from ..ratelimit import lobby_rate_limit
from ..schemas import (
    CreateDedicatedLobbyRequest,
    CreateLobbyRequest,
    JoinLobbyRequest,
    JoinResponse,
    LobbyListResponse,
    LobbyResponse,
)
from ..security import get_current_user, hash_password, verify_password
from ..spawn import find_free_port, spawn_game_server, stop_container

logger = logging.getLogger("versused.lobby")

# Per-IP rate limit on mutating lobby endpoints (create / join). Read-only listing
# is intentionally excluded so the browser can refresh freely.
router = APIRouter(prefix="/lobby", tags=["lobby"])

# Serializes dedicated-server allocation so two concurrent requests can't pick
# the same free port. The slow wait-for-registration happens outside the lock.
_alloc_lock = asyncio.Lock()


def _aware(dt: datetime) -> datetime:
    # Defensive: treat any naive timestamp as UTC (the client is opened tz_aware,
    # but mongomock or older docs may yield naive datetimes).
    return dt if dt.tzinfo else dt.replace(tzinfo=timezone.utc)


def prune_stale_lobbies(db: Database) -> None:
    lobby_cutoff = utcnow() - timedelta(seconds=settings.lobby_ttl_seconds)
    for doc in db.lobbies.find():
        if _aware(doc["last_heartbeat"]) < lobby_cutoff:
            db.join_tokens.delete_many({"session_id": doc["_id"]})
            db.lobbies.delete_one({"_id": doc["_id"]})
    # Crash recovery: drop server docs whose container died without a clean
    # shutdown, so their ports can be reused.
    server_cutoff = utcnow() - timedelta(seconds=settings.server_ttl_seconds)
    for doc in db.game_servers.find():
        if _aware(doc["last_heartbeat"]) < server_cutoff:
            db.game_servers.delete_one({"_id": doc["_id"]})


def _assert_name_available(db: Database, name: str) -> None:
    """Reject creation if an active lobby already uses this name.
    Compares case-insensitively and trimmed. Call *after* pruning stale lobbies
    so names freed by expired rooms can be reused."""
    wanted = (name or "").strip().lower()
    for doc in db.lobbies.find({}, {"name": 1}):
        if (doc.get("name") or "").strip().lower() == wanted:
            raise HTTPException(
                status_code=status.HTTP_409_CONFLICT,
                detail="A room with that name already exists",
            )


def _to_response(lobby: Lobby) -> LobbyResponse:
    return LobbyResponse(
        session_id=lobby.session_id,
        name=lobby.name,
        host_player_id=lobby.host_player_id,
        host_ip=lobby.host_ip,
        host_port=lobby.host_port,
        max_players=lobby.max_players,
        current_players=lobby.current_players,
        is_private=lobby.is_private,
        is_dedicated=lobby.is_dedicated,
    )


@router.get("", response_model=LobbyListResponse)
def list_lobbies(
    db: Database = Depends(get_db),
    _user: User = Depends(get_current_user),
):
    prune_stale_lobbies(db)
    # Show all non-private lobbies (both dedicated and P2P).
    lobbies = [Lobby.from_doc(doc) for doc in db.lobbies.find({"is_private": False})]
    return LobbyListResponse(lobbies=[_to_response(lobby) for lobby in lobbies])


@router.post(
    "",
    response_model=LobbyResponse,
    status_code=status.HTTP_201_CREATED,
    dependencies=[Depends(lobby_rate_limit)],
)
def create_lobby(
    body: CreateLobbyRequest,
    db: Database = Depends(get_db),
    user: User = Depends(get_current_user),
):
    """Create a player-hosted (P2P) lobby. Not shown in public search."""
    if body.is_private and not body.password:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Private lobbies require a password",
        )

    prune_stale_lobbies(db)
    _assert_name_available(db, body.name)

    lobby = Lobby(
        session_id=str(uuid.uuid4()),
        name=body.name,
        host_player_id=user.player_id,
        host_ip=body.host_ip,
        host_port=body.host_port,
        max_players=body.max_players,
        current_players=1,
        is_private=body.is_private,
        is_dedicated=False,
        password_hash=hash_password(body.password) if body.password else None,
        last_heartbeat=utcnow(),
    )
    db.lobbies.insert_one(lobby.to_doc())
    return _to_response(lobby)


@router.post(
    "/dedicated",
    response_model=LobbyResponse,
    status_code=status.HTTP_201_CREATED,
    dependencies=[Depends(lobby_rate_limit)],
)
async def create_dedicated_lobby(
    body: CreateDedicatedLobbyRequest,
    db: Database = Depends(get_db),
    user: User = Depends(get_current_user),
):
    """Spawn a fresh game-server container on-demand and create a lobby on it.
    On failure raises 503 with detail SERVER_UNAVAILABLE so the client can fall
    back to P2P."""
    if body.is_private and not body.password:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Private lobbies require a password",
        )

    async with _alloc_lock:
        prune_stale_lobbies(db)
        _assert_name_available(db, body.name)

        # Anti-abuse: a single account can't spin up unlimited containers.
        owned = db.lobbies.count_documents(
            {"host_player_id": user.player_id, "is_dedicated": True}
        )
        if owned >= settings.max_dedicated_lobbies_per_player:
            logger.warning(
                "dedicated lobby quota hit: player_id=%s owns=%d", user.player_id, owned
            )
            raise HTTPException(
                status_code=status.HTTP_429_TOO_MANY_REQUESTS,
                detail="You already have an active dedicated lobby",
            )

        # Fleet-wide cap so the whole VPS can't be exhausted by concurrent spawns.
        live_servers = db.game_servers.count_documents({})
        if live_servers >= settings.max_concurrent_game_servers:
            logger.warning(
                "fleet container cap hit: live=%d cap=%d",
                live_servers, settings.max_concurrent_game_servers,
            )
            raise HTTPException(
                status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                detail="SERVER_UNAVAILABLE",
            )

        port = find_free_port(db)
        if port is None:
            raise HTTPException(
                status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                detail="SERVER_UNAVAILABLE",
            )
        # A failure to launch the container (Docker CLI missing, image not built,
        # daemon unreachable, …) must degrade to 503 so the client falls back to
        # P2P — never bubble up as a 500.
        try:
            container_name = spawn_game_server(port)
            logger.info(
                "spawned game server: port=%d owner=%s live=%d",
                port, user.player_id, live_servers + 1,
            )
        except Exception as exc:
            logger.error("spawn_game_server failed on port %d: %s", port, exc)
            raise HTTPException(
                status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                detail="SERVER_UNAVAILABLE",
            ) from exc

    # Wait (outside the lock) for the container to register itself.
    server: GameServer | None = None
    deadline = (
        asyncio.get_event_loop().time() + settings.container_spawn_timeout_seconds
    )
    while asyncio.get_event_loop().time() < deadline:
        await asyncio.sleep(0.5)
        doc = db.game_servers.find_one({"port": port, "status": "available"})
        if doc is not None:
            server = GameServer.from_doc(doc)
            break

    if server is None:
        stop_container(container_name)
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="SERVER_UNAVAILABLE",
        )

    session_id = str(uuid.uuid4())
    lobby = Lobby(
        session_id=session_id,
        name=body.name,
        host_player_id=user.player_id,
        host_ip=settings.vps_public_ip,
        host_port=port,
        max_players=body.max_players,
        current_players=0,
        is_private=body.is_private,
        is_dedicated=True,
        server_id=server.server_id,
        password_hash=hash_password(body.password) if body.password else None,
        last_heartbeat=utcnow(),
    )
    db.lobbies.insert_one(lobby.to_doc())

    db.game_servers.update_one(
        {"_id": server.server_id},
        {
            "$set": {
                "status": "allocated",
                "session_id": session_id,
                "lobby_name": body.name,
                "max_players": body.max_players,
            }
        },
    )
    return _to_response(lobby)


@router.post(
    "/{session_id}/join",
    response_model=JoinResponse,
    dependencies=[Depends(lobby_rate_limit)],
)
def join_lobby(
    session_id: str,
    body: JoinLobbyRequest,
    db: Database = Depends(get_db),
    user: User = Depends(get_current_user),
):
    prune_stale_lobbies(db)
    doc = db.lobbies.find_one({"_id": session_id})
    if doc is None:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail="Lobby not found"
        )
    lobby = Lobby.from_doc(doc)

    if lobby.is_private:
        if not body.password or lobby.password_hash is None or not verify_password(
            body.password, lobby.password_hash
        ):
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN, detail="Invalid lobby password"
            )

    if lobby.current_players >= lobby.max_players:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT, detail="Lobby is full"
        )

    db.lobbies.update_one({"_id": session_id}, {"$inc": {"current_players": 1}})

    token = JoinToken(
        token=str(uuid.uuid4()),
        session_id=lobby.session_id,
        player_id=user.player_id,
        expires_at=utcnow() + timedelta(minutes=settings.join_token_expire_minutes),
    )
    db.join_tokens.insert_one(token.to_doc())

    return JoinResponse(
        session_id=lobby.session_id,
        host_ip=lobby.host_ip,
        host_port=lobby.host_port,
        join_token=token.token,
    )


@router.delete("/{session_id}/leave", status_code=status.HTTP_204_NO_CONTENT)
def leave_lobby(
    session_id: str,
    db: Database = Depends(get_db),
    user: User = Depends(get_current_user),
):
    doc = db.lobbies.find_one({"_id": session_id})
    if doc is None:
        return  # idempotent: already gone
    lobby = Lobby.from_doc(doc)

    # Host leaving tears down the whole lobby; otherwise just decrement.
    if lobby.host_player_id == user.player_id:
        db.join_tokens.delete_many({"session_id": session_id})
        # Free the backing dedicated server (the container self-removes on match end).
        if lobby.server_id:
            db.game_servers.update_one(
                {"_id": lobby.server_id},
                {"$set": {"status": "available", "session_id": None, "lobby_name": None}},
            )
        db.lobbies.delete_one({"_id": session_id})
    else:
        new_count = max(0, lobby.current_players - 1)
        db.lobbies.update_one(
            {"_id": session_id}, {"$set": {"current_players": new_count}}
        )


@router.post("/{session_id}/heartbeat", status_code=status.HTTP_204_NO_CONTENT)
def heartbeat(
    session_id: str,
    db: Database = Depends(get_db),
):
    """Keep a lobby alive. Called by the game server (no player auth) every ~15s."""
    result = db.lobbies.update_one(
        {"_id": session_id}, {"$set": {"last_heartbeat": utcnow()}}
    )
    if result.matched_count == 0:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail="Lobby not found"
        )
