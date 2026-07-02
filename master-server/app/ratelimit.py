"""Minimal in-memory, per-client sliding-window rate limiter.

Dependency-free (no Redis/slowapi). NOTE: state is per-process, so with more than
one uvicorn/gunicorn worker each worker counts independently and the effective
limit is multiplied by the worker count. Run the master server with a single
worker, or swap this for a shared store (e.g. Redis) before scaling out."""

import time
from collections import defaultdict, deque

from fastapi import HTTPException, Request, status

from .config import settings


class RateLimiter:
    def __init__(self, max_requests: int, window_seconds: float):
        self.max_requests = max_requests
        self.window_seconds = window_seconds
        self._hits: dict[str, deque[float]] = defaultdict(deque)

    def __call__(self, request: Request) -> None:
        client = request.client.host if request.client else "unknown"
        now = time.monotonic()
        hits = self._hits[client]
        cutoff = now - self.window_seconds
        while hits and hits[0] < cutoff:
            hits.popleft()
        if len(hits) >= self.max_requests:
            raise HTTPException(
                status_code=status.HTTP_429_TOO_MANY_REQUESTS,
                detail="Too many requests, slow down.",
            )
        hits.append(now)


# Auth attempts per client IP (brute-force / guest-spam guard).
auth_rate_limit = RateLimiter(
    max_requests=settings.auth_rate_limit_max,
    window_seconds=settings.auth_rate_limit_window_seconds,
)

# Lobby create/join per client IP (curbs mass lobby creation / dedicated-server spawns).
lobby_rate_limit = RateLimiter(
    max_requests=settings.lobby_rate_limit_max,
    window_seconds=settings.lobby_rate_limit_window_seconds,
)
