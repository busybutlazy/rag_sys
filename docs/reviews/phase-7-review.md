# Phase 7 Code Review — Polish & Experimentation Tooling

**Branch:** phase-7-experiment-tooling  
**Date:** 2026-05-13  
**Reviewer:** Codex

## Summary

Phase 7 adds notebook-scoped experiment runs, ArangoDB persistence, a frontend experiment dashboard, multi-user indexes, rate limiting, a one-db-per-user Arango helper, and JSON-style logging. Docker builds passed for be-server, rag-server, ai-server, and frontend.

## Findings

### BUG-01 — JSON log formatter can emit invalid JSON (PATCH REQUIRED)

**Files:** `ai-server/app/main.py`, `rag-server/app/main.py`

The current formatter uses a JSON-looking format string. If a log message contains quotes, newlines, or structured exception text, the emitted line is not valid JSON.

**Fix:** Add a small `JsonFormatter` using `json.dumps()` and configure Python services with it.

### BUG-02 — Experiment config can be null in BE request binding (PATCH REQUIRED)

**File:** `be-server/BeServer/Services/RagClient.cs`

`ExperimentRunRequest.Config` is non-nullable by type, but ASP.NET model binding can still produce a null nested object for malformed or partial JSON. `RunExperimentAsync` dereferences it directly.

**Fix:** Make the config nullable and normalize defaults in the controller before calling RAG.

### MAINT-01 — EF model snapshot is stale for content tables (PATCH REQUIRED)

**File:** `be-server/BeServer/Migrations/AppDbContextModelSnapshot.cs`

The snapshot currently only contains `Users`, even though migrations and `AppDbContext` include notebooks, sources, notes, and chat sessions. Future EF migration generation would be incorrect.

**Fix:** Refresh the snapshot to include the current model, including Phase 7 indexes.

## Not Issues

- Experiment results store source refs and counts rather than full chunk text, which keeps Arango records compact.
- RAG experiment endpoints are internal-secret guarded; BE verifies notebook ownership before proxying.
- The Arango one-db-per-user helper is intentionally a script, not an automatic runtime migration.
