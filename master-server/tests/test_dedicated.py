"""Dedicated on-demand spawn flow, with Docker mocked out."""

import uuid

from app import database
from app.models import GameServer, utcnow
from app.routers import lobby as lobby_router

from .conftest import auth_header


def _fake_spawn_factory(port):
    """Simulate a container that registers itself the moment it's spawned."""

    def fake_spawn(p):
        server = GameServer(
            server_id=str(uuid.uuid4()),
            ip="1.2.3.4",
            port=p,
            status="available",
            last_heartbeat=utcnow(),
        )
        database.get_db().game_servers.insert_one(server.to_doc())
        return f"gs-{p}"

    return fake_spawn


def test_create_dedicated_lobby_allocates_server(client, monkeypatch):
    monkeypatch.setattr(lobby_router, "spawn_game_server", _fake_spawn_factory(9000))
    monkeypatch.setattr(lobby_router, "stop_container", lambda name: None)
    # Speed up the registration wait.
    monkeypatch.setattr(
        lobby_router.settings, "container_spawn_timeout_seconds", 3.0
    )

    headers = auth_header(client)
    r = client.post(
        "/lobby/dedicated", json={"name": "Arena", "max_players": 8}, headers=headers
    )
    assert r.status_code == 201
    data = r.json()
    assert data["is_dedicated"] is True
    assert data["host_ip"] == lobby_router.settings.vps_public_ip

    # It now appears in the public search (unlike P2P lobbies).
    listed = client.get("/lobby", headers=headers).json()["lobbies"]
    assert any(lo["session_id"] == data["session_id"] for lo in listed)


def test_dedicated_lobby_quota_per_player(client, monkeypatch):
    """A single account can't own more than max_dedicated_lobbies_per_player."""
    monkeypatch.setattr(lobby_router, "spawn_game_server", _fake_spawn_factory(9010))
    monkeypatch.setattr(lobby_router, "stop_container", lambda name: None)
    monkeypatch.setattr(lobby_router.settings, "container_spawn_timeout_seconds", 3.0)
    monkeypatch.setattr(lobby_router.settings, "max_dedicated_lobbies_per_player", 1)

    headers = auth_header(client)
    r1 = client.post("/lobby/dedicated", json={"name": "Arena1"}, headers=headers)
    assert r1.status_code == 201

    r2 = client.post("/lobby/dedicated", json={"name": "Arena2"}, headers=headers)
    assert r2.status_code == 429


def test_dedicated_global_container_cap(client, monkeypatch):
    """Fleet-wide cap returns 503 once max_concurrent_game_servers is reached."""
    monkeypatch.setattr(lobby_router, "spawn_game_server", _fake_spawn_factory(9020))
    monkeypatch.setattr(lobby_router, "stop_container", lambda name: None)
    monkeypatch.setattr(lobby_router.settings, "container_spawn_timeout_seconds", 3.0)
    monkeypatch.setattr(lobby_router.settings, "max_concurrent_game_servers", 0)

    headers = auth_header(client)
    r = client.post("/lobby/dedicated", json={"name": "Arena"}, headers=headers)
    assert r.status_code == 503
    assert r.json()["detail"] == "SERVER_UNAVAILABLE"


def test_dedicated_falls_back_when_no_container_registers(client, monkeypatch):
    # Spawn that never registers → 503 SERVER_UNAVAILABLE.
    monkeypatch.setattr(lobby_router, "spawn_game_server", lambda p: f"gs-{p}")
    monkeypatch.setattr(lobby_router, "stop_container", lambda name: None)
    monkeypatch.setattr(
        lobby_router.settings, "container_spawn_timeout_seconds", 1.0
    )

    headers = auth_header(client)
    r = client.post(
        "/lobby/dedicated", json={"name": "Arena"}, headers=headers
    )
    assert r.status_code == 503
    assert r.json()["detail"] == "SERVER_UNAVAILABLE"
