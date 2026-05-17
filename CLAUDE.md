# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

## Build and Test Commands

All services run in Docker. No local runtimes are required except for the commands below, which use ephemeral containers.

### BE server (.NET 8)

```bash
# Build
docker compose build be-server

# Test
docker run --rm -v /home/jett/Documents/rag_sys/be-server:/src -w /src \
  mcr.microsoft.com/dotnet/sdk:8.0 dotnet test BeServer.Tests/BeServer.Tests.csproj \
  --logger "console;verbosity=minimal"

# Format check
docker run --rm -v /home/jett/Documents/rag_sys/be-server:/src -w /src \
  mcr.microsoft.com/dotnet/sdk:8.0 dotnet format BeServer.sln --verify-no-changes
```

### Python (ai-server / rag-server)

```bash
# Syntax check
python3 -m compileall rag-server/app ai-server/app

# RAG server unit tests (requires RAG_INTERNAL_SECRET + OPENAI_API_KEY env vars)
docker run --rm \
  -e RAG_INTERNAL_SECRET=<secret> -e OPENAI_API_KEY=dummy \
  -v /home/jett/Documents/rag_sys/rag-server:/app -w /app \
  rag-sys-rag-server python -m unittest discover -s tests

# Lint (both servers)
docker run --rm -v /home/jett/Documents/rag_sys:/repo -w /repo \
  ghcr.io/astral-sh/ruff:latest check rag-server ai-server
```

### Frontend (React + Vite + TypeScript)

```bash
cd frontend
npm run build      # TypeScript check + production build
npm run lint       # ESLint
npm run smoke      # Vitest smoke tests
```

### Full stack (dev mode with hot reload)

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up --build
```

Production port: `http://localhost:5987`

---

## Architecture

### Service topology

```
Browser → frontend:80 (nginx, only exposed port: 5987)
  → BE server:8001 (.NET 8, /api/*)
  → AI server:8002 (FastAPI, /ai/* proxied by nginx)

BE server → MySQL:3306  (EF Core, code-first migrations)
BE server → RAG server:8003 (via RagClient)
BE server → AI server:8002 (HttpClient "ai-server")

AI server → RAG server:8003 (rag_client.py)
AI server → BE server:8001 (be_client.py, for internal request logging)
AI server → OpenAI API

RAG server → ArangoDB:8529 (python-arango)
RAG server → OpenAI API (embeddings only)
```

### BE server (`be-server/`)

- **Auth boundary**: BE is the sole auth/ownership enforcer. All user-facing routes require a JWT. Internal routes from AI server use `X-Internal-Secret` (`AI_INTERNAL_SECRET`).
- **Ownership pattern**: `CurrentUserAccessor` reads the user id from the JWT claim. `OwnershipService` wraps the three common ownership checks (notebook, session, source). Controllers inject both.
- **Error envelope**: All error responses go through `ApiErrors` → `{ error: { code, message }, correlationId }`. Never return raw `BadRequest(string)`.
- **Status constants**: Use `SourceStatuses`, `ChatRequestStatuses`, `SessionTaskStatuses` (in `Data/Entities/StatusConstants.cs`) and `IngestionJobStatuses` (in `Data/Entities/IngestionJob.cs`). Never hardcode status strings in controllers or entities.
- **Correlation ID**: `CorrelationIdMiddleware` sets `HttpContext.TraceIdentifier` from `X-Correlation-Id` header and echoes it back. Pass it forward to downstream calls.
- **Request logging**: `RequestLogSanitizer.Redact()` must wrap any JSON containing user content before it is persisted to `request_logs`.
- **Ingestion**: Source uploads create an `IngestionJob` record; `IngestionJobWorker` (hosted service) picks it up. Never call RAG directly from a controller.
- **Migrations**: EF Core code-first. Add a new migration file under `Migrations/` following the existing timestamp naming convention and update `AppDbContextModelSnapshot.cs`.

### AI server (`ai-server/`)

- `main.py` — FastAPI routes: `/chat/completions`, `/agent/run`, `/session-state/update`.
- `gateway/` — `LLMGateway` ABC + `OpenAIGateway` implementation. Phase 13 will add more providers here.
- `agent.py` — ReAct loop; calls `rag_client` and `be_client` with `correlation_id` forwarded.
- `rag_client.py` / `be_client.py` — All outbound HTTP. Both accept `correlation_id: str | None` and pass `X-Correlation-Id` header downstream.
- Auth: user-facing routes validate the JWT (`auth.py`); internal `/session-state/update` validates `AI_INTERNAL_SECRET`.

### RAG server (`rag-server/`)

- `chunker.py` — Text extraction (PDF/DOCX/JSON/text) with enforced limits (`PARSER_MAX_*` env vars); `chunk_text()` for splitting.
- `embedder.py` — OpenAI `text-embedding-3-small`, 1536 dimensions. Model and dimensions are module-level constants.
- `vector_store.py` — ArangoDB: `chunks` collection (vector index, cosine), `chunks_view` (ArangoSearch for BM25). Hybrid search uses RRF.
- All routes validate `X-Internal-Secret` against `RAG_INTERNAL_SECRET`.

### Frontend (`frontend/`)

- `lib/api.ts` — All fetch calls go through `apiGet`, `apiPost`, `apiPut`, `apiDelete`, `apiUpload`. No direct `fetch` in components.
- `contexts/AuthContext.tsx` — Token storage, refresh logic.
- `pages/NotebookDetailPage.tsx` — Tab-based workspace; all notebook-level API calls live here (except notes, which are self-contained in `NotebookNotesPanel`).
- `components/NotebookNotesPanel.tsx` — Self-contained: manages its own API calls for note CRUD.
- `components/ChatPanel.tsx` — Self-contained: manages sessions, messages, and SSE streaming.

---

## Key conventions

### Branch and PR workflow

Feature branches: `phase-N-<kebab-name>`. Patch branches: `fix-<kebab-name>` or `phase-N-patch`. Phase implementation plans go in `docs/superpowers/plans/`. Code reviews go in `docs/reviews/`.

### Internal secret split

Three secrets with a fallback chain:
- `RAG_INTERNAL_SECRET` — BE → RAG and AI → RAG
- `AI_INTERNAL_SECRET` — BE → AI and AI → BE
- `INTERNAL_SECRET` — legacy fallback; both services try their specific secret first

Minimum length 32 chars enforced at startup in all three services.

### Status string ownership

| Constant class | Location | Values |
|---|---|---|
| `SourceStatuses` | `StatusConstants.cs` | `Uploaded`, `Ingested` |
| `ChatRequestStatuses` | `StatusConstants.cs` | `Running`, `Completed`, `Failed` |
| `SessionTaskStatuses` | `StatusConstants.cs` | `Active`, `Paused`, `Done`, `Cancelled` |
| `IngestionJobStatuses` | `IngestionJob.cs` | `Queued`, `Running`, `Succeeded`, `Failed`, `Retrying`, `Cancelled` |

### Hardening roadmap (ROADMAP2.md)

Phases 8–12 are complete. Phase 13 (extensibility) is next. See `ROADMAP2.md` for full checklist and current status snapshots.
