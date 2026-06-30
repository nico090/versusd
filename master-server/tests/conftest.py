"""Test fixtures.

Each test gets a fresh in-memory MongoDB (mongomock) wired into the app by
setting the module-global handle in app.database, so init_db's lifespan call
no-ops and never touches a real server.
"""

import os

# Must be set before importing app modules: config.settings is built at import
# time and the secret guard runs in the app lifespan.
os.environ.setdefault("REQUIRE_SECURE_SECRETS", "false")
os.environ.setdefault("SECRET_KEY", "test-secret-key")
os.environ.setdefault("SERVER_SHARED_SECRET", "test-server-secret")

import mongomock
import pytest
from fastapi.testclient import TestClient

from app import database
from app.config import settings
from app.main import app
from app.ratelimit import auth_rate_limit

SERVER_SECRET = settings.server_shared_secret
SERVER_HEADERS = {"X-Server-Secret": SERVER_SECRET}


@pytest.fixture(name="client")
def client_fixture():
    mongo_client = mongomock.MongoClient(tz_aware=True)
    test_db = mongo_client[settings.db_name]
    database.create_indexes(test_db)

    # Point the app at the test db. init_db (run in the lifespan) sees a handle is
    # already set and returns without connecting to a real Mongo.
    database._client = mongo_client
    database._db = test_db

    # Disable per-IP auth throttling in tests (shared singleton state would
    # otherwise leak across tests and trip the limiter).
    app.dependency_overrides[auth_rate_limit] = lambda: None
    with TestClient(app) as test_client:
        yield test_client
    app.dependency_overrides.clear()
    database._client = None
    database._db = None


def auth_header(client, username="player1", password="pw"):
    client.post("/auth/register", json={"username": username, "password": password})
    r = client.post("/auth/login", json={"username": username, "password": password})
    return {"Authorization": f"Bearer {r.json()['access_token']}"}
