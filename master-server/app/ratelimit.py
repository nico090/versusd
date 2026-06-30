"""Minimal in-memory, per-client sliding-window rate limiter.

Dependency-free (no Redis/slowapi). Adequate for a single-process master server;
swap for a shared store if you scale to multiple workers."""

import time
from collections import defaultdict, deque

from fastapi import HTTPException, Request, status


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


# 10 auth attempts per minute per client IP.
auth_rate_limit = RateLimiter(max_requests=10, window_seconds=60.0)
