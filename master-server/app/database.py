"""MongoDB access layer (PyMongo, synchronous).

A single process-wide client/database handle is created lazily by ``init_db``
(called from the app lifespan) and handed to routers via the ``get_db`` FastAPI
dependency. Tests swap in a mongomock database by setting ``_db`` directly, so
``init_db`` is a no-op once a handle already exists.
"""

from pymongo import ASCENDING, MongoClient
from pymongo.database import Database

from .config import settings

_client: MongoClient | None = None
_db: Database | None = None


def create_indexes(db: Database) -> None:
    # Usernames are unique (the register route also checks, but the index is the
    # real guarantee under concurrency). Other indexes back hot lookups/prunes.
    db.users.create_index("username", unique=True)
    db.game_servers.create_index([("port", ASCENDING)])
    db.game_servers.create_index([("status", ASCENDING)])
    db.join_tokens.create_index([("session_id", ASCENDING)])
    db.lobbies.create_index([("host_player_id", ASCENDING)])
    db.lobbies.create_index([("is_private", ASCENDING)])


def init_db() -> None:
    """Open the Mongo connection and ensure indexes. Idempotent: if a handle is
    already set (e.g. a mongomock db injected by tests) this returns early and
    never touches a real server."""
    global _client, _db
    if _db is not None:
        return
    # tz_aware so datetimes round-trip as timezone-aware UTC.
    _client = MongoClient(settings.mongo_url, tz_aware=True)
    _db = _client[settings.db_name]
    create_indexes(_db)


def get_db() -> Database:
    """FastAPI dependency / direct accessor for the active database handle."""
    if _db is None:
        init_db()
    assert _db is not None
    return _db
