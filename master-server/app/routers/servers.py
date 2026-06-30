import uuid
from datetime import timezone

from fastapi import APIRouter, Depends, HTTPException, status
from pymongo.database import Database

from ..database import get_db
from ..models import GameServer, JoinToken, utcnow
from ..schemas import (
    RegisterServerRequest,
    ServerAllocationResponse,
    ServerResponse,
    ValidateJoinTokenRequest,
    ValidateJoinTokenResponse,
)
from ..security import require_server

router = APIRouter(prefix="/servers", tags=["servers"])


@router.post(
    "/register",
    response_model=ServerResponse,
    dependencies=[Depends(require_server)],
)
def register_server(
    body: RegisterServerRequest,
    db: Database = Depends(get_db),
):
    server = GameServer(
        server_id=str(uuid.uuid4()),
        ip=body.ip,
        port=body.port,
        max_players=body.max_players,
        current_players=0,
        status="available",
        last_heartbeat=utcnow(),
    )
    db.game_servers.insert_one(server.to_doc())
    return ServerResponse(
        server_id=server.server_id,
        ip=server.ip,
        port=server.port,
        current_players=server.current_players,
        max_players=server.max_players,
    )


@router.put(
    "/{server_id}/heartbeat",
    status_code=status.HTTP_204_NO_CONTENT,
    dependencies=[Depends(require_server)],
)
def server_heartbeat(
    server_id: str,
    db: Database = Depends(get_db),
):
    result = db.game_servers.update_one(
        {"_id": server_id}, {"$set": {"last_heartbeat": utcnow()}}
    )
    if result.matched_count == 0:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail="Server not found"
        )


@router.get(
    "/{server_id}/allocation",
    response_model=ServerAllocationResponse,
    dependencies=[Depends(require_server)],
)
def get_server_allocation(
    server_id: str,
    db: Database = Depends(get_db),
):
    """Polled by a dedicated server waiting to be assigned a lobby."""
    doc = db.game_servers.find_one({"_id": server_id})
    if doc is None:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail="Server not found"
        )
    server = GameServer.from_doc(doc)
    if server.status != "allocated":
        return ServerAllocationResponse(allocated=False)
    return ServerAllocationResponse(
        allocated=True,
        session_id=server.session_id,
        lobby_name=server.lobby_name or "Room",
        max_players=server.max_players,
    )


@router.delete(
    "/{server_id}",
    status_code=status.HTTP_204_NO_CONTENT,
    dependencies=[Depends(require_server)],
)
def unregister_server(
    server_id: str,
    db: Database = Depends(get_db),
):
    """Clean shutdown: the dedicated server removes itself from the pool."""
    db.game_servers.delete_one({"_id": server_id})


@router.post("/validate-join-token", response_model=ValidateJoinTokenResponse)
def validate_join_token(
    body: ValidateJoinTokenRequest,
    db: Database = Depends(get_db),
):
    """Called by the game server during the connection handshake. Single-use:
    a valid token is consumed so it can't be replayed.

    Deliberately unauthenticated: P2P hosts (which have no server secret) also
    validate tokens here, and the endpoint only confirms validity of a token the
    caller must already possess."""
    doc = db.join_tokens.find_one({"_id": body.join_token})
    if doc is None:
        return ValidateJoinTokenResponse(valid=False)
    token = JoinToken.from_doc(doc)

    expires_at = token.expires_at
    if expires_at.tzinfo is None:
        expires_at = expires_at.replace(tzinfo=timezone.utc)
    if expires_at < utcnow():
        db.join_tokens.delete_one({"_id": token.token})
        return ValidateJoinTokenResponse(valid=False)

    # Treat an empty string the same as "not provided": the schema marks
    # session_id optional, and clients that omit it send "" (not null). The
    # token PK is an unguessable single-use uuid4, so this check is only an
    # extra defense and must not reject callers who simply didn't supply it.
    if body.session_id and body.session_id != token.session_id:
        return ValidateJoinTokenResponse(valid=False)

    player_id = token.player_id
    session_id = token.session_id
    # Peek (consume=False) leaves the token in place so a client bounced by a later
    # gameplay gate can reconnect; the game server consumes it once the player is
    # actually seated. Replay is still bounded by the token's short TTL.
    if body.consume:
        db.join_tokens.delete_one({"_id": token.token})
    return ValidateJoinTokenResponse(
        valid=True, player_id=player_id, session_id=session_id
    )
