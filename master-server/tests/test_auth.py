def test_register_and_login(client):
    r = client.post("/auth/register", json={"username": "alice", "password": "secret"})
    assert r.status_code == 200
    data = r.json()
    assert data["username"] == "alice"
    assert "access_token" in data

    r = client.post("/auth/login", json={"username": "alice", "password": "secret"})
    assert r.status_code == 200

    r = client.post("/auth/login", json={"username": "alice", "password": "wrong"})
    assert r.status_code == 401


def test_duplicate_username(client):
    client.post("/auth/register", json={"username": "bob", "password": "pw"})
    r = client.post("/auth/register", json={"username": "bob", "password": "pw2"})
    assert r.status_code == 409


def test_guest_login(client):
    r = client.post("/auth/guest")
    assert r.status_code == 200
    data = r.json()
    assert data["username"].startswith("Guest_")
    assert "access_token" in data


def test_guest_gets_ttl_expiry(client):
    """Guests are stamped with guest_expires_at so the TTL index can prune them."""
    from app import database

    r = client.post("/auth/guest")
    player_id = r.json()["player_id"]
    doc = database.get_db().users.find_one({"_id": player_id})
    assert doc["is_guest"] is True
    assert doc["guest_expires_at"] is not None


def test_registered_user_has_no_ttl_expiry(client):
    """Registered accounts must never carry an expiry (TTL index would delete them)."""
    from app import database

    r = client.post("/auth/register", json={"username": "keeper", "password": "pw"})
    player_id = r.json()["player_id"]
    doc = database.get_db().users.find_one({"_id": player_id})
    assert doc.get("guest_expires_at") is None
