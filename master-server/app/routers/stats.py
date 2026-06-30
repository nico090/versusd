from fastapi import APIRouter, Depends, HTTPException, status
from pymongo.database import Database

from ..database import get_db
from ..models import User
from ..schemas import MatchResultRequest, StatsResponse
from ..security import get_current_user, require_server

router = APIRouter(prefix="/stats", tags=["stats"])


@router.post(
    "/match-result",
    status_code=status.HTTP_204_NO_CONTENT,
    dependencies=[Depends(require_server)],
)
def submit_match_result(
    body: MatchResultRequest,
    db: Database = Depends(get_db),
):
    """Each listed player gets +1 games_played; the winner also +1 games_won.
    Unknown player_ids (e.g. guests already purged) are skipped silently.

    Server-authoritative: only a dedicated game server (presenting the shared
    secret) may report results, so players can't forge their own win counts.
    P2P matches are therefore unranked."""
    for player_id in set(body.player_ids):
        inc = {"games_played": 1}
        if body.winner_player_id is not None and player_id == body.winner_player_id:
            inc["games_won"] = 1
        # update_one with no upsert silently no-ops for unknown player_ids.
        db.users.update_one({"_id": player_id}, {"$inc": inc})


@router.get("/{player_id}", response_model=StatsResponse)
def get_stats(
    player_id: str,
    db: Database = Depends(get_db),
    _user: User = Depends(get_current_user),
):
    doc = db.users.find_one({"_id": player_id})
    if doc is None:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail="Player not found"
        )
    user = User.from_doc(doc)
    return StatsResponse(
        player_id=user.player_id,
        username=user.username,
        games_played=user.games_played,
        games_won=user.games_won,
    )
