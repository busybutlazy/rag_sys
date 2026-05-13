import json
import os
import time
from collections.abc import AsyncGenerator
from typing import Any

from openai import AsyncOpenAI

from app import be_client, rag_client
from app.models import AgentRunRequest
from app.prompt_loader import render_prompt

_MAX_STEPS = 6
_client = AsyncOpenAI(api_key=os.environ.get("OPENAI_API_KEY", ""))

_TOOLS: list[dict[str, Any]] = [
    {
        "type": "function",
        "function": {
            "name": "search_knowledge",
            "description": "Search ingested knowledge in the active notebook with hybrid retrieval.",
            "parameters": {
                "type": "object",
                "properties": {
                    "query": {"type": "string"},
                    "top_k": {"type": "integer", "minimum": 1, "maximum": 10, "default": 5},
                },
                "required": ["query"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "get_source_content",
            "description": "Read ingested chunks for one source in the active notebook.",
            "parameters": {
                "type": "object",
                "properties": {
                    "source_id": {"type": "string"},
                    "max_chars": {"type": "integer", "minimum": 1000, "maximum": 50000, "default": 12000},
                },
                "required": ["source_id"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "list_notebooks",
            "description": "List notebooks owned by the current user.",
            "parameters": {"type": "object", "properties": {}},
        },
    },
    {
        "type": "function",
        "function": {
            "name": "create_note",
            "description": "Create a note for the current user in a notebook.",
            "parameters": {
                "type": "object",
                "properties": {
                    "notebook_id": {"type": "string"},
                    "title": {"type": "string"},
                    "content": {"type": "string"},
                },
                "required": ["content"],
            },
        },
    },
]


async def stream_agent_events(
    req: AgentRunRequest,
    authorization: str | None,
) -> AsyncGenerator[dict[str, Any], None]:
    messages: list[dict[str, Any]] = [
        {
            "role": "system",
            "content": render_prompt("agent_system.j2", notebook_id=req.notebook_id),
        },
        *[m.model_dump() for m in req.messages],
    ]

    for step in range(1, _MAX_STEPS + 1):
        started = time.perf_counter()
        try:
            response = await _client.chat.completions.create(
                model=req.model,
                messages=messages,
                tools=_TOOLS,
                tool_choice="auto",
            )
            await be_client.log_request(
                chat_request_id=req.request_id,
                session_id=req.session_id,
                service="openai",
                operation="agent.step",
                method="POST",
                request_json={"model": req.model, "step": step, "message_count": len(messages)},
                response_json={"tool_calls": len(response.choices[0].message.tool_calls or [])},
                duration_ms=int((time.perf_counter() - started) * 1000),
            )
        except Exception as exc:
            await be_client.log_request(
                chat_request_id=req.request_id,
                session_id=req.session_id,
                service="openai",
                operation="agent.step",
                method="POST",
                request_json={"model": req.model, "step": step, "message_count": len(messages)},
                duration_ms=int((time.perf_counter() - started) * 1000),
                error=str(exc),
            )
            raise
        message = response.choices[0].message
        messages.append(message.model_dump(exclude_none=True))

        if not message.tool_calls:
            yield {"token": message.content or ""}
            return

        for call in message.tool_calls:
            name = call.function.name
            args = _loads_args(call.function.arguments)
            yield {
                "trace": {
                    "step": step,
                    "tool": name,
                    "arguments": args,
                }
            }
            try:
                tool_started = time.perf_counter()
                result = await _run_tool(name, args, req.notebook_id, authorization)
                ok = True
            except Exception as exc:
                result = {"error": str(exc)}
                ok = False
            await be_client.log_request(
                chat_request_id=req.request_id,
                session_id=req.session_id,
                service=_tool_service(name),
                operation=name,
                method="CALL",
                request_json={"arguments": args, "notebook_id": req.notebook_id},
                response_json=_loggable_result(result),
                duration_ms=int((time.perf_counter() - tool_started) * 1000),
                error=None if ok else str(result.get("error")),
            )

            yield {
                "tool_result": {
                    "step": step,
                    "tool": name,
                    "ok": ok,
                    "summary": _summarize_result(result),
                }
            }
            messages.append(
                {
                    "role": "tool",
                    "tool_call_id": call.id,
                    "content": json.dumps(result, ensure_ascii=False),
                }
            )

    messages.append(
        {
            "role": "user",
            "content": "Stop calling tools and provide the final answer from the gathered context.",
        }
    )
    started = time.perf_counter()
    try:
        stream = await _client.chat.completions.create(
            model=req.model,
            messages=messages,
            stream=True,
        )
        async for chunk in stream:
            token = chunk.choices[0].delta.content
            if token:
                yield {"token": token}
        await be_client.log_request(
            chat_request_id=req.request_id,
            session_id=req.session_id,
            service="openai",
            operation="agent.final.stream",
            method="POST",
            request_json={"model": req.model, "message_count": len(messages), "stream": True},
            response_json={"completed": True},
            duration_ms=int((time.perf_counter() - started) * 1000),
        )
    except Exception as exc:
        await be_client.log_request(
            chat_request_id=req.request_id,
            session_id=req.session_id,
            service="openai",
            operation="agent.final.stream",
            method="POST",
            request_json={"model": req.model, "message_count": len(messages), "stream": True},
            duration_ms=int((time.perf_counter() - started) * 1000),
            error=str(exc),
        )
        raise


async def _run_tool(
    name: str,
    args: dict[str, Any],
    active_notebook_id: str | None,
    authorization: str | None,
) -> Any:
    if name == "list_notebooks":
        return await be_client.list_notebooks(authorization)

    if name == "search_knowledge":
        notebook_id = _require_notebook(active_notebook_id)
        query = _require_string(args, "query")
        top_k = _bounded_int(args.get("top_k", 5), 1, 10)
        return await rag_client.search(query, notebook_id, top_k)

    if name == "get_source_content":
        notebook_id = _require_notebook(active_notebook_id)
        source_id = _require_string(args, "source_id")
        max_chars = _bounded_int(args.get("max_chars", 12000), 1000, 50000)
        return await rag_client.get_source_content(source_id, notebook_id, max_chars)

    if name == "create_note":
        notebook_id = args.get("notebook_id") or active_notebook_id
        if not isinstance(notebook_id, str) or not notebook_id.strip():
            raise ValueError("notebook_id is required to create a note")
        if active_notebook_id and notebook_id != active_notebook_id:
            raise ValueError("create_note must use the active notebook")
        title = args.get("title")
        if title is not None and not isinstance(title, str):
            raise ValueError("title must be a string")
        content = _require_string(args, "content")
        return await be_client.create_note(authorization, notebook_id, title, content)

    raise ValueError(f"Unknown tool: {name}")


def _loads_args(raw: str) -> dict[str, Any]:
    if not raw:
        return {}
    parsed = json.loads(raw)
    if not isinstance(parsed, dict):
        raise ValueError("Tool arguments must be a JSON object")
    return parsed


def _require_notebook(notebook_id: str | None) -> str:
    if not notebook_id:
        raise ValueError("An active notebook_id is required for this tool")
    return notebook_id


def _require_string(args: dict[str, Any], key: str) -> str:
    value = args.get(key)
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{key} is required")
    return value


def _bounded_int(value: Any, minimum: int, maximum: int) -> int:
    try:
        parsed = int(value)
    except (TypeError, ValueError) as exc:
        raise ValueError("Expected integer argument") from exc
    return max(minimum, min(maximum, parsed))


def _summarize_result(result: Any) -> str:
    if isinstance(result, list):
        return f"{len(result)} item(s)"
    if isinstance(result, dict):
        if "error" in result:
            return str(result["error"])
        if "text" in result:
            return f"{len(result.get('chunks', []))} chunk(s), {len(result['text'])} chars"
        if "id" in result:
            return f"created {result['id']}"
    return "ok"


def _tool_service(name: str) -> str:
    if name in {"search_knowledge", "get_source_content"}:
        return "rag-server"
    if name in {"list_notebooks", "create_note"}:
        return "be-server"
    return "tool"


def _loggable_result(result: Any) -> Any:
    if isinstance(result, list):
        return {"count": len(result), "preview": result[:3]}
    if isinstance(result, dict) and "text" in result:
        return {**result, "text": str(result["text"])[:1000]}
    return result
