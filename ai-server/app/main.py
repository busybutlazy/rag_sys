import json
import os
from fastapi import Depends, FastAPI
from fastapi.responses import StreamingResponse
from app.auth import get_current_user
from app.gateway.openai_provider import OpenAIGateway
from app.models import ChatRequest

app = FastAPI(title="AI Server", version="0.1.0")

_gateway = OpenAIGateway(api_key=os.environ.get("OPENAI_API_KEY", ""))


@app.get("/health")
async def health():
    return {"status": "ok", "service": "ai-server"}


@app.post("/chat/completions")
async def chat_completions(
    req: ChatRequest,
    _user_id: str = Depends(get_current_user),
):
    messages = [m.model_dump() for m in req.messages]

    async def event_stream():
        try:
            async for token in _gateway.stream_complete(messages, req.model):
                yield f"data: {json.dumps({'token': token})}\n\n"
        except Exception as exc:
            yield f"data: {json.dumps({'error': str(exc)})}\n\n"
        finally:
            yield "data: [DONE]\n\n"

    return StreamingResponse(event_stream(), media_type="text/event-stream")
