# Phase 3 Code Review — LLM Gateway & AI Server

**Branch:** phase-3-llm  
**Date:** 2026-05-12  
**Reviewer:** Claude Sonnet 4.6

---

## Summary

Phase 3 introduces the LLM gateway abstraction (OpenAI provider), JWT-guarded `/chat/completions` SSE endpoint, and a React ChatPanel component with real-time streaming. Overall the implementation is clean. One security gap found.

---

## Findings

### SEC-01 — Missing JWT_SECRET startup guard in ai-server (PATCH REQUIRED)

**File:** `ai-server/app/auth.py`

`_JWT_SECRET` is read at module import time, but there is no minimum-length check. The be-server raises `SystemExit` at startup if `len(JWT_SECRET) < 32`. The ai-server silently accepts an empty string; PyJWT will fail-closed (empty key → `DecodeError` → 401), but an operator misconfiguration gives no clear error.

**Fix:** Add a startup length guard matching the be-server pattern.

---

## Not Issues

- **`stream_complete` return type**: `async def` with `yield` correctly satisfies `AsyncGenerator[str, None]` — Python infers the generator type.
- **`_gateway` module-level init**: Creating `AsyncOpenAI` with an empty key defers failure to request time, which is acceptable for an API key (unlike a signing secret).
- **ChatPanel `assistantIdx`**: Captured as `history.length` before the functional `setMessages` that appends the placeholder — stable because React state updates are batched and the functional form is used for all subsequent mutations.
- **`[DONE]` break scope**: `break` exits the inner `for` loop; the outer `while` then calls `reader.read()` which returns `done: true` when the server closes the stream. Correct behavior.
- **SSE chunk split**: Buffer accumulation with `buf = lines.pop() ?? ''` correctly handles chunks that split a `data:` line across reads.

---

## Patch

1. `ai-server/app/main.py` — add startup guard: raise `SystemExit` if `JWT_SECRET` is shorter than 32 characters.
