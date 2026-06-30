from .conftest import SERVER_HEADERS, auth_header


def test_match_result_requires_server_secret(client):
    # Register a player so we have a real player_id.
    client.post("/auth/register", json={"username": "p1", "password": "pw"})
    r = client.post("/auth/login", json={"username": "p1", "password": "pw"})
    player_id = r.json()["player_id"]
    body = {"player_ids": [player_id], "winner_player_id": player_id}

    # A player cannot self-report (no server secret) → 401, stats unchanged.
    headers = {"Authorization": f"Bearer {r.json()['access_token']}"}
    assert client.post("/stats/match-result", json=body, headers=headers).status_code == 401

    stats = client.get(f"/stats/{player_id}", headers=headers).json()
    assert stats["games_played"] == 0
    assert stats["games_won"] == 0


def test_server_authoritative_match_result(client):
    headers = auth_header(client, "winner", "pw")
    player_id = client.post("/auth/login", json={"username": "winner", "password": "pw"}).json()[
        "player_id"
    ]

    r = client.post(
        "/stats/match-result",
        json={"player_ids": [player_id], "winner_player_id": player_id},
        headers=SERVER_HEADERS,
    )
    assert r.status_code == 204

    stats = client.get(f"/stats/{player_id}", headers=headers).json()
    assert stats["games_played"] == 1
    assert stats["games_won"] == 1
