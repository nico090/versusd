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
