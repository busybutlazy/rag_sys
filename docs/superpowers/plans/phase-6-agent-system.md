# Phase 6 Plan — AI Agent System

## Goal

Add a notebook-scoped tool-calling agent that can search the knowledge base, read source content, list notebooks, create notes, stream its tool trace to the frontend, and return a cited final answer.

## Scope

- Agent loop lives in `ai-server`.
- Prompt text is maintained as Jinja templates under `ai-server/app/prompts/`.
- Existing JWT auth is reused for `/agent/run`.
- Existing RAG hybrid search remains the default search tool.
- Existing BE notebook/note APIs are reused through an internal HTTP client that forwards the user's bearer token.
- Source content is exposed by `rag-server` through an internal-secret endpoint that returns ingested chunks for one source inside one notebook.
- Frontend `ChatPanel` gets an agent mode toggle and renders streamed tool-call trace events.

## Implementation Steps

1. Add `jinja2` to `ai-server` dependencies and add prompt template loading helpers.
2. Add `agent_system.j2` with tool-use instructions, notebook scoping rules, and citation requirements.
3. Extend `ai-server` models with `AgentRunRequest`.
4. Add an authenticated BE client in `ai-server` for `list_notebooks` and `create_note`.
5. Extend `rag_client.py` with `get_source_content`.
6. Implement a compact OpenAI tool-calling loop with max-step guard and SSE events:
   - `trace` when the model requests a tool
   - `tool_result` after execution
   - `token` while streaming final answer
   - `[DONE]` sentinel
7. Add `rag-server` endpoint `GET /documents/{source_id}/content?notebook_id=...`.
8. Update frontend `ChatPanel`:
   - agent/chat toggle
   - call `/ai/agent/run` when agent mode is enabled
   - render tool traces under assistant messages
9. Validate with Docker builds for `ai-server`, `rag-server`, and `frontend`.

## Review Checklist

- Agent cannot search outside the active notebook when `notebook_id` is provided.
- Internal RAG endpoints require `X-Internal-Secret`.
- BE tool calls preserve user authorization by forwarding the bearer token.
- Tool arguments are validated before execution.
- Agent loop has a deterministic max iteration limit.
- Prompt changes live in `.j2` files, not inline strings.
