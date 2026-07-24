from datetime import timedelta

from app import database
from app.models import GameServer, Lobby, utcnow
from app.routers import lobby as lobby_router

from .conftest import auth_header


def _create_p2p(client, headers, **overrides):
    body = {"name": "Room", "host_ip": "127.0.0.1", "host_port": 7777}
    body.update(overrides)
    return client.post("/lobby", json=body, headers=headers)


def test_create_p2p_lobby_appears_in_public_search(client):
    headers = auth_header(client)
    r = _create_p2p(client, headers, name="My Room", max_players=4)
    assert r.status_code == 201
    assert r.json()["current_players"] == 1
    assert r.json()["is_dedicated"] is False

    # Public P2P rooms now appear in the lobby list alongside dedicated rooms.
    r = client.get("/lobby", headers=headers)
    assert r.status_code == 200
    names = [lobby["name"] for lobby in r.json()["lobbies"]]
    assert "My Room" in names


def test_private_p2p_lobby_not_in_public_search(client):
    headers = auth_header(client)
    r = _create_p2p(client, headers, name="Secret Room", is_private=True, password="pw")
    assert r.status_code == 201

    r = client.get("/lobby", headers=headers)
    assert r.status_code == 200
    names = [lobby["name"] for lobby in r.json()["lobbies"]]
    assert "Secret Room" not in names


def test_join_lobby_returns_token(client):
    host = auth_header(client, "host", "pw")
    session_id = _create_p2p(client, host, host_ip="192.168.1.1").json()["session_id"]

    joiner = auth_header(client, "joiner", "pw")
    r = client.post(f"/lobby/{session_id}/join", json={}, headers=joiner)
    assert r.status_code == 200
    data = r.json()
    assert data["host_ip"] == "192.168.1.1"
    assert len(data["join_token"]) > 10


def test_join_full_lobby_409(client):
    host = auth_header(client, "host", "pw")
    session_id = _create_p2p(client, host, max_players=1).json()["session_id"]

    joiner = auth_header(client, "late", "pw")
    r = client.post(f"/lobby/{session_id}/join", json={}, headers=joiner)
    assert r.status_code == 409


def test_private_lobby_requires_correct_password(client):
    host = auth_header(client, "host", "pw")
    session_id = _create_p2p(
        client, host, is_private=True, password="hunter2"
    ).json()["session_id"]

    joiner = auth_header(client, "joiner", "pw")
    r = client.post(f"/lobby/{session_id}/join", json={"password": "wrong"}, headers=joiner)
    assert r.status_code == 403
    r = client.post(f"/lobby/{session_id}/join", json={"password": "hunter2"}, headers=joiner)
    assert r.status_code == 200


def _create_relay(client, headers, **overrides):
    body = {"name": "Relay Room", "relay_server_id": "relay-123"}
    body.update(overrides)
    return client.post("/lobby/relay", json=body, headers=headers)


def test_create_relay_lobby_appears_in_public_search(client):
    headers = auth_header(client)
    r = _create_relay(client, headers, name="Relayed", relay_server_id="abc-999")
    assert r.status_code == 201
    data = r.json()
    assert data["is_relay"] is True
    assert data["is_dedicated"] is False
    assert data["relay_server_id"] == "abc-999"
    assert data["current_players"] == 1

    r = client.get("/lobby", headers=headers)
    assert r.status_code == 200
    names = [lobby["name"] for lobby in r.json()["lobbies"]]
    assert "Relayed" in names


def test_join_relay_lobby_returns_server_id(client):
    host = auth_header(client, "relayhost", "pw")
    session_id = _create_relay(
        client, host, relay_server_id="srv-777"
    ).json()["session_id"]

    joiner = auth_header(client, "relayjoiner", "pw")
    r = client.post(f"/lobby/{session_id}/join", json={}, headers=joiner)
    assert r.status_code == 200
    data = r.json()
    assert data["is_relay"] is True
    assert data["relay_server_id"] == "srv-777"
    assert len(data["join_token"]) > 10


def test_create_relay_lobby_requires_server_id(client):
    headers = auth_header(client)
    r = client.post("/lobby/relay", json={"name": "No Id"}, headers=headers)
    assert r.status_code == 422


def test_host_leave_deletes_lobby(client):
    host = auth_header(client, "host", "pw")
    session_id = _create_p2p(client, host).json()["session_id"]

    r = client.delete(f"/lobby/{session_id}/leave", headers=host)
    assert r.status_code == 204
    assert client.post(f"/lobby/{session_id}/join", json={}, headers=host).status_code == 404


# ── Empty-lobby cleanup (empty_since / empty_lobby_ttl_seconds) ──────────────

def test_empty_lobby_pruned_after_ttl_and_frees_server(client):
    """A still-heartbeating lobby that's been at zero players past the TTL is
    pruned, and its (alive) dedicated container is freed for reuse rather than
    left allocated."""
    db = database.get_db()
    db.game_servers.insert_one(GameServer(
        server_id="srv-empty", ip="1.2.3.4", port=9100, status="allocated",
        session_id="s-empty", last_heartbeat=utcnow(),
    ).to_doc())
    db.lobbies.insert_one(Lobby(
        session_id="s-empty", name="Ghost", host_player_id="p1",
        host_ip="1.2.3.4", host_port=9100, current_players=0, is_dedicated=True,
        server_id="srv-empty", last_heartbeat=utcnow(),  # still alive
        empty_since=utcnow() - timedelta(seconds=lobby_router.settings.empty_lobby_ttl_seconds + 1),
    ).to_doc())

    # Any listing runs prune_stale_lobbies.
    client.get("/lobby", headers=auth_header(client))

    assert db.lobbies.find_one({"_id": "s-empty"}) is None
    srv = db.game_servers.find_one({"_id": "srv-empty"})
    assert srv is not None and srv["status"] == "available" and srv["session_id"] is None


def test_recently_empty_lobby_not_pruned(client):
    """A lobby that just went empty is within the grace window and survives."""
    db = database.get_db()
    db.lobbies.insert_one(Lobby(
        session_id="s-fresh-empty", name="Waiting", host_player_id="p1",
        host_ip="", host_port=0, current_players=0, is_relay=True,
        relay_server_id="r1", last_heartbeat=utcnow(), empty_since=utcnow(),
    ).to_doc())

    client.get("/lobby", headers=auth_header(client))

    assert db.lobbies.find_one({"_id": "s-fresh-empty"}) is not None


def test_join_clears_empty_timer(client):
    """A join resets empty_since so a later prune leaves the lobby alone."""
    db = database.get_db()
    host = auth_header(client, "eh", "pw")
    sid = _create_relay(client, host, relay_server_id="y").json()["session_id"]
    # Empty but still within the grace window (a join arriving after expiry would
    # instead find the lobby already pruned — covered by the prune test above).
    db.lobbies.update_one({"_id": sid}, {"$set": {
        "current_players": 0,
        "empty_since": utcnow() - timedelta(seconds=10),
    }})
    joiner = auth_header(client, "ej", "pw")
    assert client.post(f"/lobby/{sid}/join", json={}, headers=joiner).status_code == 200

    assert db.lobbies.find_one({"_id": sid})["empty_since"] is None
    client.get("/lobby", headers=host)  # prune runs
    assert db.lobbies.find_one({"_id": sid}) is not None


def test_last_player_leaving_starts_empty_timer(client):
    """When the final non-host player leaves and the count hits zero, the empty
    timer starts (rather than the lobby lingering forever)."""
    db = database.get_db()
    db.lobbies.insert_one(Lobby(
        session_id="s-leave", name="X", host_player_id="ghost-host",
        host_ip="1.2.3.4", host_port=9000, current_players=1, is_dedicated=True,
        last_heartbeat=utcnow(), empty_since=None,
    ).to_doc())

    leaver = auth_header(client, "leaver", "pw")
    assert client.delete("/lobby/s-leave/leave", headers=leaver).status_code == 204

    doc = db.lobbies.find_one({"_id": "s-leave"})
    assert doc["current_players"] == 0
    assert doc["empty_since"] is not None
