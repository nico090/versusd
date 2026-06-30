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


def test_host_leave_deletes_lobby(client):
    host = auth_header(client, "host", "pw")
    session_id = _create_p2p(client, host).json()["session_id"]

    r = client.delete(f"/lobby/{session_id}/leave", headers=host)
    assert r.status_code == 204
    assert client.post(f"/lobby/{session_id}/join", json={}, headers=host).status_code == 404
