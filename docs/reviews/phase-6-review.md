# Phase 6 Code Review — AI Agent System

**Branch:** phase-6-agent-system  
**Date:** 2026-05-13  
**Reviewer:** Codex

## Summary

Phase 6 adds an OpenAI tool-calling agent, Jinja-maintained prompts, notebook-scoped search/source tools, note creation, and frontend trace rendering. Docker builds passed for `ai-server`, `rag-server`, and `frontend`; updated services start successfully after making Arango initialization idempotent.

## Findings

### PERF-01 — `get_source_content` sends unbounded chunk payloads to the model (PATCH REQUIRED)

**File:** `rag-server/app/vector_store.py`

`get_source_content` truncates the joined `text` field to `max_chars`, but still returns `chunks` with every full chunk text. The AI tool result serializes the entire response into the model conversation, so a large source can exceed context limits or create unnecessary latency/cost.

**Fix:** Limit returned chunks to the same character budget, and include a `truncated` flag so the agent knows when content was clipped.

### UX-01 — Tool trace hides arguments (PATCH REQUIRED)

**File:** `frontend/src/components/ChatPanel.tsx`

The SSE trace includes tool arguments, but the UI only shows tool names and result status. For Phase 6's "shows tool call trace" requirement, users should be able to inspect what the agent searched or which source it read.

**Fix:** Render a compact JSON argument line under each trace item.

## Not Issues

- `create_note` forwards the user bearer token to be-server; notebook ownership is enforced by existing BE APIs.
- `search_knowledge` and `get_source_content` never accept arbitrary notebook ids from the model when an active notebook is present.
- `rag-server` source content endpoint is internal-secret protected and filters by both `source_id` and `notebook_id`.
