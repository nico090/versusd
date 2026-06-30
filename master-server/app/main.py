from contextlib import asynccontextmanager

from fastapi import FastAPI

from .database import init_db
from .routers import auth, lobby, servers, stats
from .security import assert_secrets_configured


@asynccontextmanager
async def lifespan(_app: FastAPI):
    # Abort early if JWT / server secrets still hold insecure defaults.
    assert_secrets_configured()
    init_db()
    yield


app = FastAPI(
    title="BossRoom Master Server",
    description="Auth, lobby browser and player stats for the PvP deathmatch.",
    version="0.1.0",
    lifespan=lifespan,
)

app.include_router(auth.router)
app.include_router(lobby.router)
app.include_router(servers.router)
app.include_router(stats.router)


@app.get("/health", tags=["meta"])
def health():
    return {"status": "ok"}
