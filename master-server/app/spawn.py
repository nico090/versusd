"""On-demand dedicated game-server spawning via the Docker CLI.

Isolated here (rather than inline in the router) so tests can monkeypatch
``spawn_game_server`` / ``stop_container`` without invoking real Docker.
"""

import subprocess

from pymongo.database import Database

from .config import settings


def find_free_port(db: Database) -> int | None:
    """Return a UDP port in the configured range not currently held by a live
    GameServer doc, or None if the range is exhausted."""
    used = set(db.game_servers.distinct("port"))
    for port in range(settings.game_server_port_start, settings.game_server_port_end + 1):
        if port not in used:
            return port
    return None


def spawn_game_server(port: int) -> str:
    """Launch a detached, self-removing game-server container bound to ``port``.
    Returns the container name. Raises CalledProcessError on docker failure."""
    container_name = f"gs-{port}"
    subprocess.Popen(
        [
            "docker", "run", "-d", "--rm",
            "--name", container_name,
            "--network", settings.docker_network,
            "-p", f"{port}:{port}/udp",
            "-e", f"MASTER_SERVER_URL={settings.master_server_internal_url}",
            "-e", f"SERVER_IP={settings.vps_public_ip}",
            "-e", f"SERVER_PORT={port}",
            "-e", f"SERVER_SHARED_SECRET={settings.server_shared_secret}",
            settings.game_server_image,
        ]
    )
    return container_name


def stop_container(container_name: str) -> None:
    """Best-effort stop of a container that failed to register in time."""
    subprocess.run(["docker", "stop", container_name], capture_output=True)
