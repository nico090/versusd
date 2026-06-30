import secrets
from datetime import datetime, timedelta, timezone

from fastapi import Depends, Header, HTTPException, status
from fastapi.security import OAuth2PasswordBearer
from jose import JWTError, jwt
from passlib.context import CryptContext
from pymongo.database import Database

from .config import INSECURE_SECRET_PLACEHOLDERS, settings
from .database import get_db
from .models import User

pwd_context = CryptContext(schemes=["bcrypt"], deprecated="auto")

# tokenUrl is informational (Swagger "Authorize"); the game client sends the
# Bearer token directly. auto_error=False lets us raise our own 401.
oauth2_scheme = OAuth2PasswordBearer(tokenUrl="auth/login", auto_error=False)


def hash_password(password: str) -> str:
    return pwd_context.hash(password)


def verify_password(plain: str, hashed: str) -> bool:
    return pwd_context.verify(plain, hashed)


def create_access_token(player_id: str) -> str:
    expire = datetime.now(timezone.utc) + timedelta(
        minutes=settings.access_token_expire_minutes
    )
    payload = {"sub": player_id, "exp": expire}
    return jwt.encode(payload, settings.secret_key, algorithm=settings.algorithm)


def get_current_user(
    token: str | None = Depends(oauth2_scheme),
    db: Database = Depends(get_db),
) -> User:
    credentials_exc = HTTPException(
        status_code=status.HTTP_401_UNAUTHORIZED,
        detail="Could not validate credentials",
        headers={"WWW-Authenticate": "Bearer"},
    )
    if not token:
        raise credentials_exc
    try:
        payload = jwt.decode(
            token, settings.secret_key, algorithms=[settings.algorithm]
        )
        player_id = payload.get("sub")
    except JWTError:
        raise credentials_exc
    if not player_id:
        raise credentials_exc

    doc = db.users.find_one({"_id": player_id})
    if doc is None:
        raise credentials_exc
    return User.from_doc(doc)


def require_server(x_server_secret: str | None = Header(default=None)) -> None:
    """Dependency guarding privileged endpoints called by game-server processes
    (not players). The server presents the shared secret via the X-Server-Secret
    header. Uses a constant-time compare to avoid timing leaks."""
    if not x_server_secret or not secrets.compare_digest(
        x_server_secret, settings.server_shared_secret
    ):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid or missing server credentials",
        )


def assert_secrets_configured() -> None:
    """Abort startup if secrets still hold insecure placeholder values.
    Set REQUIRE_SECURE_SECRETS=false (env) to bypass for local development."""
    if not settings.require_secure_secrets:
        return
    insecure = []
    if settings.secret_key in INSECURE_SECRET_PLACEHOLDERS:
        insecure.append("SECRET_KEY")
    if settings.server_shared_secret in INSECURE_SECRET_PLACEHOLDERS:
        insecure.append("SERVER_SHARED_SECRET")
    if insecure:
        raise RuntimeError(
            "Refusing to start with insecure default(s): "
            + ", ".join(insecure)
            + ". Set them in the environment/.env, or set REQUIRE_SECURE_SECRETS=false "
            "for local development."
        )
