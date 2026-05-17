import json
import os
import time
from fastapi import Depends, FastAPI, Header
from fastapi import HTTPException
from fastapi.responses import StreamingResponse
from app.auth import get_current_user, _JWT_SECRET
from app.agent import stream_agent_events
from app import be_client
from app.gateway.openai_provider import OpenAIGateway
from app.json_logging import configure_json_logging
from app.models import AgentRunRequest, ChatRequest, SessionStateUpdateRequest
from app import rag_client

configure_json_logging()

_MIN_SECRET_LEN = 32
if len(_JWT_SECRET) < _MIN_SECRET_LEN:
    raise SystemExit(
        f"JWT_SECRET must be at least {_MIN_SECRET_LEN} characters (got {len(_JWT_SECRET)})"
    )

_AI_INTERNAL_SECRET = os.environ.get("AI_INTERNAL_SECRET") or os.environ.get("INTERNAL_SECRET", "")
if len(_AI_INTERNAL_SECRET) < _MIN_SECRET_LEN:
    raise SystemExit(f"AI_INTERNAL_SECRET must be at least {_MIN_SECRET_LEN} characters")

_RAG_INTERNAL_SECRET = os.environ.get("RAG_INTERNAL_SECRET") or os.environ.get("INTERNAL_SECRET", "")
if len(_RAG_INTERNAL_SECRET) < _MIN_SECRET_LEN:
    raise SystemExit(f"RAG_INTERNAL_SECRET must be at least {_MIN_SECRET_LEN} characters")

app = FastAPI(title="AI Server", version="0.1.0")

_gateway = OpenAIGateway(api_key=os.environ.get("OPENAI_API_KEY", ""))

_ALLOWED_MODELS: set[str] = set(
    m.strip()
    for m in os.environ.get("ALLOWED_MODELS", "gpt-4o-mini,gpt-4o").split(",")
    if m.strip()
)
_SUMMARY_MODEL = os.environ.get("SUMMARY_MODEL", "gpt-4o-mini")

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
    user_id: str = Depends(get_current_user),
    x_correlation_id: str | None = Header(default=None),
):
    if req.model not in _ALLOWED_MODELS:
        raise HTTPException(status_code=422, detail=f"Model '{req.model}' is not allowed")
    messages = [m.model_dump() for m in req.messages]
    sources: list[dict] = []

    if req.notebook_id:
        # Pull the last user message as the search query
        user_messages = [m for m in req.messages if m.role == "user"]
        if user_messages:
            query = user_messages[-1].content
            started = time.perf_counter()
            try:
                sources = await rag_client.search(query, req.notebook_id, user_id, correlation_id=x_correlation_id)
                await be_client.log_request(
                    chat_request_id=req.request_id,
                    session_id=req.session_id,
                    service="rag-server",
                    operation="search.hybrid",
                    method="GET",
                    request_json={"query": query, "notebook_id": req.notebook_id, "top_k": 5},
                    response_json={"result_count": len(sources)},
                    duration_ms=int((time.perf_counter() - started) * 1000),
                    correlation_id=x_correlation_id,
                )
            except Exception as exc:
                await be_client.log_request(
                    chat_request_id=req.request_id,
                    session_id=req.session_id,
                    service="rag-server",
                    operation="search.hybrid",
                    method="GET",
                    request_json={"query": query, "notebook_id": req.notebook_id, "top_k": 5},
                    duration_ms=int((time.perf_counter() - started) * 1000),
                    error=str(exc),
                    correlation_id=x_correlation_id,
                )
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
            started = time.perf_counter()
            async for token in _gateway.stream_complete(messages, req.model):
                yield f"data: {json.dumps({'token': token})}\n\n"
            await be_client.log_request(
                chat_request_id=req.request_id,
                session_id=req.session_id,
                service="openai",
                operation="chat.completions.stream",
                method="POST",
                request_json={"model": req.model, "message_count": len(messages), "stream": True},
                response_json={"completed": True},
                duration_ms=int((time.perf_counter() - started) * 1000),
                correlation_id=x_correlation_id,
            )
        except Exception as exc:
            await be_client.log_request(
                chat_request_id=req.request_id,
                session_id=req.session_id,
                service="openai",
                operation="chat.completions.stream",
                method="POST",
                request_json={"model": req.model, "message_count": len(messages), "stream": True},
                duration_ms=int((time.perf_counter() - started) * 1000),
                error=str(exc),
                correlation_id=x_correlation_id,
            )
            yield f"data: {json.dumps({'error': str(exc)})}\n\n"
        finally:
            yield "data: [DONE]\n\n"

    return StreamingResponse(event_stream(), media_type="text/event-stream")


@app.post("/session-state/update")
async def session_state_update(
    req: SessionStateUpdateRequest,
    x_internal_secret: str | None = Header(default=None),
    x_correlation_id: str | None = Header(default=None),
):
    if x_internal_secret != _AI_INTERNAL_SECRET:
        raise HTTPException(status_code=401, detail="Invalid internal secret")

    prev = req.prev_session_state or {}
    fallback = _fallback_session_state(prev, req.user_input, req.assistant_response)
    if not os.environ.get("OPENAI_API_KEY"):
        return fallback

    schema = {
        "name": "SessionState",
        "schema": {
            "type": "object",
            "properties": {
                "summary": {"type": "string"},
                "topic_stack": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "id": {"type": "string"},
                            "title": {"type": "string"},
                            "summary": {"type": "string"},
                            "status": {"type": "string", "enum": ["active", "paused", "done", "cancelled"]},
                            "updated_reason": {"type": "string"},
                        },
                        "required": ["id", "title", "summary", "status", "updated_reason"],
                        "additionalProperties": False,
                    },
                },
            },
            "required": ["summary", "topic_stack"],
            "additionalProperties": False,
        },
    }
    prompt = (
        "Update a persistent chat session state. Preserve useful existing tasks, "
        "remove trace/tool details, and keep the most current task first.\n\n"
        f"Previous state:\n{json.dumps(prev, ensure_ascii=False)}\n\n"
        f"User input:\n{req.user_input}\n\n"
        f"Assistant response:\n{req.assistant_response[:4000]}"
    )
    started = time.perf_counter()
    try:
        state = await _gateway.complete_structured(
            messages=[
                {"role": "system", "content": "Return only structured session state JSON."},
                {"role": "user", "content": prompt},
            ],
            schema=schema,
            model=_SUMMARY_MODEL,
        )
        await be_client.log_request(
            chat_request_id=req.request_id,
            session_id=req.session_id,
            service="openai",
            operation="session-state.update",
            method="POST",
            request_json={"model": _SUMMARY_MODEL},
            response_json={"completed": True},
            duration_ms=int((time.perf_counter() - started) * 1000),
            correlation_id=x_correlation_id,
        )
        return state
    except Exception as exc:
        await be_client.log_request(
            chat_request_id=req.request_id,
            session_id=req.session_id,
            service="openai",
            operation="session-state.update",
            method="POST",
            request_json={"model": "gpt-4o-mini"},
            duration_ms=int((time.perf_counter() - started) * 1000),
            error=str(exc),
            correlation_id=x_correlation_id,
        )
        return fallback


@app.post("/agent/run")
async def agent_run(
    req: AgentRunRequest,
    user_id: str = Depends(get_current_user),
    authorization: str | None = Header(default=None),
    x_correlation_id: str | None = Header(default=None),
):
    if req.model not in _ALLOWED_MODELS:
        raise HTTPException(status_code=422, detail=f"Model '{req.model}' is not allowed")

    async def event_stream():
        try:
            async for event in stream_agent_events(req, authorization, user_id, x_correlation_id):
                yield f"data: {json.dumps(event, ensure_ascii=False)}\n\n"
        except Exception as exc:
            yield f"data: {json.dumps({'error': str(exc)})}\n\n"
        finally:
            yield "data: [DONE]\n\n"

    return StreamingResponse(event_stream(), media_type="text/event-stream")


def _fallback_session_state(prev: dict, user_input: str, assistant_response: str) -> dict:
    summary = prev.get("summary") if isinstance(prev.get("summary"), str) else ""
    if not summary:
        summary = user_input[:160]
    existing = prev.get("topic_stack") if isinstance(prev.get("topic_stack"), list) else []
    tasks: list[dict] = []
    for item in existing:
        if not isinstance(item, dict):
            continue
        task = {
            "id": str(item.get("id") or f"task-{abs(hash(user_input))}"),
            "title": str(item.get("title") or "Conversation")[:120],
            "summary": str(item.get("summary") or summary)[:500],
            "status": item.get("status") if item.get("status") in {"active", "paused", "done", "cancelled"} else "paused",
            "updated_reason": "Preserved from previous state.",
        }
        tasks.append(task)
    if not tasks:
        tasks.append(
            {
                "id": f"task-{abs(hash(user_input))}",
                "title": user_input[:80] or "Conversation",
                "summary": (assistant_response or user_input)[:500],
                "status": "active",
                "updated_reason": "Created from the latest user turn.",
            }
        )
    active_seen = False
    for task in tasks:
        if task["status"] == "active":
            if not active_seen:
                active_seen = True
            else:
                task["status"] = "paused"
    if not active_seen:
        tasks[0]["status"] = "active"
    return {"summary": summary, "topic_stack": tasks}
