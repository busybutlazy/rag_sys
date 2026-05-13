import json
import os
from fastapi import Depends, FastAPI, Header
from fastapi.responses import StreamingResponse
from app.auth import get_current_user, _JWT_SECRET
from app.agent import stream_agent_events
from app.gateway.openai_provider import OpenAIGateway
from app.models import AgentRunRequest, ChatRequest
from app import rag_client

_MIN_SECRET_LEN = 32
if len(_JWT_SECRET) < _MIN_SECRET_LEN:
    raise SystemExit(
        f"JWT_SECRET must be at least {_MIN_SECRET_LEN} characters (got {len(_JWT_SECRET)})"
    )

app = FastAPI(title="AI Server", version="0.1.0")

_gateway = OpenAIGateway(api_key=os.environ.get("OPENAI_API_KEY", ""))

_RAG_SYSTEM_PROMPT = (
    "You are a helpful assistant. Answer the user's question based on the provided context. "
    "If the context does not contain relevant information, say so clearly.\n\n"
    "Context:\n{context}"
)


@app.get("/health")
async def health():
    return {"status": "ok", "service": "ai-server"}


@app.post("/chat/completions")
async def chat_completions(
    req: ChatRequest,
    _user_id: str = Depends(get_current_user),
):
    messages = [m.model_dump() for m in req.messages]
    sources: list[dict] = []

    if req.notebook_id:
        # Pull the last user message as the search query
        user_messages = [m for m in req.messages if m.role == "user"]
        if user_messages:
            query = user_messages[-1].content
            try:
                sources = await rag_client.search(query, req.notebook_id)
            except Exception:
                sources = []

        if sources:
            context = "\n\n---\n\n".join(
                f"[Chunk {i+1} from source {s['source_id'][:8]}…]\n{s['text']}"
                for i, s in enumerate(sources)
            )
            rag_system = _RAG_SYSTEM_PROMPT.format(context=context)
            # Prepend RAG system message (or merge with existing system message)
            if messages and messages[0]["role"] == "system":
                messages[0]["content"] = rag_system + "\n\n" + messages[0]["content"]
            else:
                messages = [{"role": "system", "content": rag_system}] + messages

    async def event_stream():
        # Emit sources metadata before streaming tokens
        if sources:
            source_refs = [
                {"source_id": s["source_id"], "chunk_index": s["chunk_index"]}
                for s in sources
            ]
            yield f"data: {json.dumps({'sources': source_refs})}\n\n"

        try:
            async for token in _gateway.stream_complete(messages, req.model):
                yield f"data: {json.dumps({'token': token})}\n\n"
        except Exception as exc:
            yield f"data: {json.dumps({'error': str(exc)})}\n\n"
        finally:
            yield "data: [DONE]\n\n"

    return StreamingResponse(event_stream(), media_type="text/event-stream")


@app.post("/agent/run")
async def agent_run(
    req: AgentRunRequest,
    _user_id: str = Depends(get_current_user),
    authorization: str | None = Header(default=None),
):
    async def event_stream():
        try:
            async for event in stream_agent_events(req, authorization):
                yield f"data: {json.dumps(event, ensure_ascii=False)}\n\n"
        except Exception as exc:
            yield f"data: {json.dumps({'error': str(exc)})}\n\n"
        finally:
            yield "data: [DONE]\n\n"

    return StreamingResponse(event_stream(), media_type="text/event-stream")
