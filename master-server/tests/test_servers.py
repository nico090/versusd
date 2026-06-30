from .conftest import SERVER_HEADERS, auth_header


def test_register_requires_server_secret(client):
    body = {"ip": "10.0.0.1", "port": 7777, "max_players": 8}
    # Without the shared secret → rejected.
    assert client.post("/servers/register", json=body).status_code == 401
    # A player JWT is NOT sufficient for server endpoints.
    assert client.post(
        "/servers/register", json=body, headers=auth_header(client)
    ).status_code == 401
    # With the shared secret → accepted.
    r = client.post("/servers/register", json=body, headers=SERVER_HEADERS)
    assert r.status_code == 200
    assert r.json()["ip"] == "10.0.0.1"


def test_heartbeat_and_unregister(client):
    server_id = client.post(
        "/servers/register",
        json={"ip": "1.1.1.1", "port": 9001},
        headers=SERVER_HEADERS,
    ).json()["server_id"]

    assert client.put(
        f"/servers/{server_id}/heartbeat", headers=SERVER_HEADERS
    ).status_code == 204
    assert client.delete(
        f"/servers/{server_id}", headers=SERVER_HEADERS
    ).status_code == 204
    # Gone now.
    assert client.get(
        f"/servers/{server_id}/allocation", headers=SERVER_HEADERS
    ).status_code == 404


def test_allocation_starts_unallocated(client):
    server_id = client.post(
        "/servers/register",
        json={"ip": "1.1.1.1", "port": 9002},
        headers=SERVER_HEADERS,
    ).json()["server_id"]

    r = client.get(f"/servers/{server_id}/allocation", headers=SERVER_HEADERS)
    assert r.status_code == 200
    assert r.json()["allocated"] is False


def test_validate_join_token_single_use(client):
    # Issue a token by joining a P2P lobby.
    host = auth_header(client, "host", "pw")
    session_id = client.post(
        "/lobby",
        json={"name": "R", "host_ip": "127.0.0.1", "host_port": 7777},
        headers=host,
    ).json()["session_id"]
    joiner = auth_header(client, "joiner", "pw")
    token = client.post(
        f"/lobby/{session_id}/join", json={}, headers=joiner
    ).json()["join_token"]

    # First validation succeeds and consumes the token.
    r = client.post("/servers/validate-join-token", json={"join_token": token})
    assert r.status_code == 200
    assert r.json()["valid"] is True
    assert r.json()["session_id"] == session_id

    # Replay → invalid.
    r = client.post("/servers/validate-join-token", json={"join_token": token})
    assert r.json()["valid"] is False


def test_validate_garbage_token(client):
    r = client.post("/servers/validate-join-token", json={"join_token": "garbage"})
    assert r.status_code == 200
    assert r.json()["valid"] is False


def test_validate_join_token_empty_session_id(client):
    # Clients that don't plumb the session id send "" (JsonUtility never emits
    # null), so an empty session_id must be treated as "not provided" and accepted
    # rather than mismatched against the lobby's real session id.
    host = auth_header(client, "host", "pw")
    session_id = client.post(
        "/lobby",
        json={"name": "R", "host_ip": "127.0.0.1", "host_port": 7777},
        headers=host,
    ).json()["session_id"]
    joiner = auth_header(client, "joiner", "pw")
    token = client.post(
        f"/lobby/{session_id}/join", json={}, headers=joiner
    ).json()["join_token"]

    r = client.post(
        "/servers/validate-join-token",
        json={"join_token": token, "session_id": ""},
    )
    assert r.status_code == 200
    assert r.json()["valid"] is True
    assert r.json()["session_id"] == session_id


def test_validate_join_token_peek_does_not_consume(client):
    # consume=False validates without burning the token, so a client bounced by a
    # later gameplay gate can reconnect; consume=True finally burns it.
    host = auth_header(client, "host", "pw")
    session_id = client.post(
        "/lobby",
        json={"name": "R", "host_ip": "127.0.0.1", "host_port": 7777},
        headers=host,
    ).json()["session_id"]
    joiner = auth_header(client, "joiner", "pw")
    token = client.post(
        f"/lobby/{session_id}/join", json={}, headers=joiner
    ).json()["join_token"]

    # Peek twice → still valid both times (not consumed).
    for _ in range(2):
        r = client.post(
            "/servers/validate-join-token",
            json={"join_token": token, "consume": False},
        )
        assert r.json()["valid"] is True

    # Consume → valid, and now it's burned.
    r = client.post(
        "/servers/validate-join-token",
        json={"join_token": token, "consume": True},
    )
    assert r.json()["valid"] is True
    r = client.post("/servers/validate-join-token", json={"join_token": token})
    assert r.json()["valid"] is False
