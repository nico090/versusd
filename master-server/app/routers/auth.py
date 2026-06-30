import uuid

from fastapi import APIRouter, Depends, HTTPException, status
from pymongo.database import Database
from pymongo.errors import DuplicateKeyError

from ..database import get_db
from ..models import User
from ..ratelimit import auth_rate_limit
from ..schemas import AuthRequest, TokenResponse
from ..security import create_access_token, hash_password, verify_password

# Throttle credential endpoints per client IP to curb brute force / guest spam.
router = APIRouter(
    prefix="/auth", tags=["auth"], dependencies=[Depends(auth_rate_limit)]
)


def _token_for(user: User) -> TokenResponse:
    return TokenResponse(
        access_token=create_access_token(user.player_id),
        player_id=user.player_id,
        username=user.username,
    )


@router.post("/register", response_model=TokenResponse)
def register(body: AuthRequest, db: Database = Depends(get_db)):
    user = User(
        player_id=str(uuid.uuid4()),
        username=body.username,
        password_hash=hash_password(body.password),
        is_guest=False,
    )
    # The unique index on username is the real guard against races; translate the
    # collision into the same 409 the explicit check used to return.
    try:
        db.users.insert_one(user.to_doc())
    except DuplicateKeyError:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT, detail="Username already taken"
        )
    return _token_for(user)


@router.post("/login", response_model=TokenResponse)
def login(body: AuthRequest, db: Database = Depends(get_db)):
    doc = db.users.find_one({"username": body.username})
    user = User.from_doc(doc) if doc is not None else None
    if (
        user is None
        or user.password_hash is None
        or not verify_password(body.password, user.password_hash)
    ):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid username or password",
        )
    return _token_for(user)


@router.post("/guest", response_model=TokenResponse)
def guest(db: Database = Depends(get_db)):
    player_id = str(uuid.uuid4())
    # Short, readable guest handle derived from the id.
    username = f"Guest_{player_id[:8]}"
    user = User(
        player_id=player_id,
        username=username,
        password_hash=None,
        is_guest=True,
    )
    db.users.insert_one(user.to_doc())
    return _token_for(user)
