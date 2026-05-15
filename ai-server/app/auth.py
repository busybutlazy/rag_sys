import os
import jwt
from fastapi import HTTPException, Header


_JWT_SECRET = os.environ.get("JWT_SECRET", "")
_ISSUER = "rag-sys"
_AUDIENCE = "rag-sys-frontend"


def get_current_user(authorization: str | None = Header(default=None)) -> str:
    if not authorization or not authorization.startswith("Bearer "):
        raise HTTPException(status_code=401, detail="Missing or invalid Authorization header")
    token = authorization.removeprefix("Bearer ")
    try:
        payload = jwt.decode(
            token,
            _JWT_SECRET,
            algorithms=["HS256"],
            audience=_AUDIENCE,
            issuer=_ISSUER,
        )
        user_id: str = payload["sub"]
        return user_id
    except jwt.ExpiredSignatureError:
        raise HTTPException(status_code=401, detail="Token expired")
    except jwt.PyJWTError:
        raise HTTPException(status_code=401, detail="Invalid token")
