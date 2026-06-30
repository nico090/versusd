"""Document models for the MongoDB collections.

Each model maps its primary-key field to Mongo's ``_id`` via ``to_doc`` /
``from_doc``, so routers can build a model, insert ``model.to_doc()``, and later
rebuild a model from a fetched document for attribute-style access. Mutations are
done with explicit collection update operators, not by writing whole documents
back.
"""

from datetime import datetime, timezone
from typing import ClassVar

from pydantic import BaseModel, ConfigDict, Field


def utcnow() -> datetime:
    return datetime.now(timezone.utc)


class _Doc(BaseModel):
    # Ignore unexpected keys so a document gaining a field doesn't break reads.
    model_config = ConfigDict(extra="ignore")

    # Subclasses set this to the model field that backs Mongo's _id.
    pk_field: ClassVar[str] = ""

    def to_doc(self) -> dict:
        data = self.model_dump()
        data["_id"] = data.pop(self.pk_field)
        return data

    @classmethod
    def from_doc(cls, doc: dict):
        data = dict(doc)
        data[cls.pk_field] = data.pop("_id")
        return cls(**data)


class User(_Doc):
    pk_field = "player_id"

    player_id: str
    username: str
    # Null for guest accounts (no password login).
    password_hash: str | None = None
    is_guest: bool = False
    games_played: int = 0
    games_won: int = 0
    created_at: datetime = Field(default_factory=utcnow)


class Lobby(_Doc):
    pk_field = "session_id"

    session_id: str
    name: str
    host_player_id: str
    host_ip: str
    host_port: int
    max_players: int = 8
    current_players: int = 1
    is_private: bool = False
    # True for lobbies backed by a VPS dedicated server (shown in public search).
    # False for player-hosted P2P lobbies (join by code only).
    is_dedicated: bool = False
    # Set for dedicated lobbies: the GameServer.server_id backing this lobby.
    server_id: str | None = None
    # Null for public lobbies; bcrypt hash for private ones.
    password_hash: str | None = None
    last_heartbeat: datetime = Field(default_factory=utcnow)


class JoinToken(_Doc):
    pk_field = "token"

    token: str
    session_id: str
    player_id: str
    expires_at: datetime


class GameServer(_Doc):
    pk_field = "server_id"

    server_id: str
    ip: str
    port: int
    current_players: int = 0
    max_players: int = 8
    # "available" once registered, "allocated" once a lobby is created on it.
    status: str = "available"
    # Set when allocated: the lobby the dedicated server should host.
    session_id: str | None = None
    lobby_name: str | None = None
    last_heartbeat: datetime = Field(default_factory=utcnow)
