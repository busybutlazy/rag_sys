# RAG System - Hardening Roadmap

This roadmap follows the current project phases in `ROADMAP.md` and focuses on the first round of production hardening after the initial feature build.

Primary review lenses:

1. Maintainability
2. Security
3. Extensibility

## Guiding Principles

- Keep BE server as the ownership and authorization boundary.
- Keep AI/RAG services internal and replaceable.
- Prefer explicit jobs, contracts, and allowlists over hidden background work and client-provided values.
- Add focused tests around multi-user isolation, auth, upload safety, and RAG behavior before broad refactors.

---

## Priority Order

| Priority | Phase | Theme | Why first |
|----------|-------|-------|-----------|
| P0 | Phase 8 | Auth/session correctness | Current refresh flow is intentionally stubbed but frontend depends on it. |
| P0 | Phase 9 | Ingestion reliability | Fire-and-forget ingestion can silently lose work. |
| P0 | Phase 10 | Upload and retrieval security | File parsing and RAG access are high-risk surfaces. |
| P1 | Phase 11 | Test and quality gates | Needed before larger refactors and provider swaps. |
| P1 | Phase 12 | API contracts and maintainability | Reduces controller/service coupling and duplicated checks. |
| P2 | Phase 13 | Extensibility and operations | Makes model/RAG/provider changes safer later. |

---

## Current Progress Snapshot - 2026-05-14

The chat conversation session slice appears complete in the current `main` branch:

- [x] SQL entities and migration exist for persisted chat orchestration:
  - `ChatSessions`
  - `ChatMessages`
  - `ChatRequests`
  - `SessionTasks`
  - `RequestLogs`
- [x] BE session API exists under `GET/POST /api/notebooks/{notebookId}/chat-sessions`.
- [x] BE persists user and assistant messages, request metadata, sources, traces, and request logs.
- [x] BE enforces notebook/session ownership checks inline for chat session listing, creation, messages, tasks, and runs.
- [x] BE forwards chat/agent runs to the AI server with `request_id`, `session_id`, and active `notebook_id`.
- [x] AI server exposes internal `POST /session-state/update` and returns a fallback state if the LLM call is unavailable.
- [x] BE projects returned session state into `SessionTasks` and tracks `ActiveTaskId`.
- [x] Frontend chat UI supports multiple sessions per notebook and reloads persisted messages.

This did **not** mean Phase 8 was complete by itself. In `ROADMAP2.md`, Phase 8 uses "session" to mean auth/login session correctness, not chat conversation sessions.

Phase 8 was subsequently implemented on `phase-8-auth-session-hardening` with full refresh token rotation and tests.

Phase 9 was implemented on `phase-9-reliable-ingestion-jobs`. Source uploads now create durable SQL ingestion jobs that are processed by a BE hosted worker, with retry/error state visible in the API and frontend.

---

## Phase 8 - Auth and Session Hardening

**Goal:** Make login/session behavior explicit, correct, and secure.

- [x] Decide one short-term auth strategy:
  - [x] Option A: implement refresh tokens fully.
  - [ ] Option B: remove frontend refresh behavior and document short-lived in-memory sessions.
- [x] If implementing refresh tokens:
  - [x] Add `refresh_tokens` table with hashed token, user id, expiry, revoked timestamp, created metadata.
  - [x] Implement refresh token rotation on every `/api/auth/refresh`.
  - [x] Revoke refresh token on logout.
  - [x] Detect token reuse and revoke the user's active refresh token family.
  - [x] Keep refresh cookie `HttpOnly`, `SameSite=Strict`, `Secure=true` outside development.
- [x] Add startup guards that reject default production secrets:
  - [x] `JWT_SECRET`
  - [x] `INTERNAL_SECRET`
  - [x] `ADMIN_PASSWORD`
  - [x] database passwords when production environment is enabled
- [x] Add auth tests:
  - [x] successful login
  - [x] invalid login
  - [x] expired access token
  - [x] refresh rotation or no-refresh frontend behavior
  - [x] logout behavior

**Deliverable:** User session behavior matches implementation. No frontend calls an endpoint that is intentionally unimplemented.

**Current status (2026-05-14):** Implemented on `phase-8-auth-session-hardening`. BE now stores hashed refresh tokens, rotates them on refresh, revokes them on logout, revokes the active family on token reuse, and rejects development defaults outside `Development`. Focused auth tests cover login, invalid login, expired access token validation, refresh rotation/reuse, and logout.

**Verification:**
- `docker run --rm -v /home/jett/Documents/rag_sys/be-server:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test BeServer.Tests/BeServer.Tests.csproj --logger "console;verbosity=normal"`
- `docker compose build be-server`

**Review references:**
- `frontend/src/contexts/AuthContext.tsx`
- `be-server/BeServer/Auth/AuthController.cs`

---

## Phase 9 - Reliable Ingestion Jobs

**Goal:** Replace fire-and-forget ingestion with durable, observable jobs.

- [x] Add an `ingestion_jobs` SQL table:
  - [x] id
  - [x] source_id
  - [x] notebook_id
  - [x] user_id
  - [x] status: `queued`, `running`, `succeeded`, `failed`, `retrying`, `cancelled`
  - [x] attempt count
  - [x] max attempts
  - [x] last error
  - [x] timestamps
- [x] Update source upload flow:
  - [x] Save source record.
  - [x] Write file.
  - [x] Create ingestion job.
  - [x] Return source and job status to frontend.
- [x] Add a BE background worker or dedicated worker service:
  - [x] Picks queued jobs.
  - [x] Calls RAG `/ingest`.
  - [x] Retries transient failures with backoff.
  - [x] Updates `sources.status` from job result.
  - [x] Handles graceful shutdown.
- [x] Add job visibility:
  - [x] `GET /api/notebooks/{id}/sources` includes current ingestion status.
  - [x] Optional endpoint for source/job details.
  - [x] Frontend displays queued/running/error states.
- [x] Add cleanup behavior:
  - [x] If file write fails after DB insert, mark source/job failed or remove record in a transaction-safe way.
  - [x] If RAG delete fails, record cleanup debt instead of only logging to stderr.

**Deliverable:** Upload ingestion survives normal service lifecycle events and has inspectable status.

**Current status (2026-05-14):** Implemented on `phase-9-reliable-ingestion-jobs`. BE now creates SQL-backed ingestion jobs on upload, processes them with `IngestionJobWorker`, records retry/failure metadata, cancels active jobs when a source is deleted, and records failed RAG-delete cleanup debt. Source list/detail responses include current ingestion job metadata, and the frontend displays/polls queued, running, retrying, ready, failed, and cancelled states.

**Verification:**
- `docker run --rm -v /home/jett/Documents/rag_sys/be-server:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test BeServer.Tests/BeServer.Tests.csproj --logger "console;verbosity=normal"`
- `npm run build` in `frontend/`
- `docker compose build be-server frontend`

**Review references:**
- `be-server/BeServer/Content/SourcesController.cs`
- `rag-server/app/main.py`

---

## Phase 10 - Upload, Parser, and Internal API Security

**Goal:** Harden file and internal-service attack surfaces.

- [x] Replace client-provided MIME trust with server-side validation:
  - [x] Check file signature/magic bytes for PDF, DOCX, JSON/text-like files.
  - [x] Cross-check extension, content type, and detected type.
  - [x] Store detected MIME separately from original content type.
- [x] Add parser limits:
  - [x] Max PDF pages.
  - [x] Max extracted characters.
  - [x] Max JSON size/depth.
  - [x] Max DOCX paragraphs/text length.
  - [x] Parser timeout or worker cancellation.
- [x] Add upload limits:
  - [x] Enforce per-user total storage quota.
  - [x] Enforce per-notebook source count limit.
  - [x] Reject empty files.
- [x] Protect internal APIs beyond a single raw shared secret:
  - [x] Enforce minimum `INTERNAL_SECRET` length in all services.
  - [x] Use different internal secrets per caller where practical.
  - [x] Add rotation plan.
  - [x] Consider service JWT or mTLS before multi-host deployment.
- [x] Add Nginx response headers:
  - [x] `X-Content-Type-Options: nosniff`
  - [x] `Referrer-Policy`
  - [x] minimal `Content-Security-Policy`
- [x] Add security tests:
  - [x] spoofed MIME type upload
  - [x] unsupported file upload
  - [x] oversized upload
  - [x] missing/invalid internal secret
  - [x] path traversal filename cases

**Deliverable:** Upload and internal endpoints fail closed with clear limits.

**Current status (2026-05-15):** Implemented on `phase-10-upload-parser-internal-api-security`. Uploads now validate detected content against the extension and claimed MIME type, persist original and detected MIME metadata, enforce storage/source-count limits, and reject ambiguous files. RAG extraction now has page/character/JSON/DOCX limits plus a timeout. Internal service credentials are split into RAG and AI trust boundaries with minimum-length checks, a documented rotation path, and compatibility fallback during local migration. Frontend nginx now emits baseline hardening headers.

**Verification:**
- `docker run --rm -v /home/jett/Documents/rag_sys/be-server:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test BeServer.Tests/BeServer.Tests.csproj --logger "console;verbosity=minimal"`
- `python3 -m compileall rag-server/app ai-server/app`
- `npm run build` in `frontend/`
- `docker compose build be-server ai-server rag-server frontend`

**Review references:**
- `be-server/BeServer/Content/SourcesController.cs`
- `rag-server/app/chunker.py`
- `rag-server/app/main.py`
- `frontend/nginx.conf`

---

## Phase 11 - Test Suite and CI Quality Gates

**Goal:** Establish the minimum test and verification foundation before larger refactors.

- [x] Add BE test project:
  - [x] Auth controller tests.
  - [x] Notebook ownership tests.
  - [x] Search validation tests.
  - [x] Source upload validation tests.
- [x] Add Python tests:
  - [x] chunking behavior
  - [x] vector_store query construction behavior where practical
  - [x] internal secret checks
  - [x] RAG request model validation
- [x] Add frontend tests or smoke checks:
  - [x] login page render
  - [x] protected route redirect
  - [x] API error display behavior
- [x] Add linters/formatters:
  - [x] .NET format/analyzers
  - [x] Python `ruff`
  - [x] TypeScript/React ESLint
- [x] Add CI pipeline:
  - [x] frontend typecheck/build
  - [x] BE build/test
  - [x] Python lint/test
  - [x] Docker compose smoke test when secrets are available

**Deliverable:** Every PR can be validated with repeatable local commands and CI checks.

**Current status (2026-05-15):** Implemented on `phase-11-test-suite-ci-quality-gates`. BE coverage now includes ownership and validation edges, RAG has focused Python unit tests, the frontend has smoke checks plus strict ESLint, and GitHub Actions runs BE, Python, frontend, and optional compose-smoke jobs.

**Verification:**
- `docker run --rm -v /home/jett/Documents/rag_sys/be-server:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test BeServer.Tests/BeServer.Tests.csproj --logger "console;verbosity=minimal"`
- `docker run --rm -v /home/jett/Documents/rag_sys/be-server:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet format BeServer.sln --verify-no-changes`
- `docker run --rm -e RAG_INTERNAL_SECRET=... -e OPENAI_API_KEY=dummy -v /home/jett/Documents/rag_sys/rag-server:/app -w /app rag-sys-rag-server python -m unittest discover -s tests`
- `docker run --rm -v /home/jett/Documents/rag_sys:/repo -w /repo ghcr.io/astral-sh/ruff:latest check rag-server ai-server`
- frontend lint/smoke/build executed in an ephemeral `node:20-alpine` container
- `docker compose build be-server ai-server rag-server frontend`

---

## Phase 12 - API Contracts and Maintainability Refactor

**Goal:** Reduce duplicated controller logic and make service contracts explicit.

**Current status (2026-05-14):** Chat session orchestration is functional, but much of the controller logic remains inline in `ChatSessionsController`. This phase is still a refactor/hardening phase, not a feature-completion phase.

- [ ] Extract BE user/notebook ownership helpers:
  - [ ] Current user id accessor.
  - [ ] Notebook ownership check.
  - [ ] Session ownership check.
  - [ ] Source ownership check.
- [ ] Split `ChatSessionsController` responsibilities:
  - [ ] Session CRUD.
  - [ ] Message persistence.
  - [ ] AI streaming proxy.
  - [ ] Session state projection.
  - [ ] Request logging.
- [ ] Introduce typed RAG client DTOs:
  - [ ] Avoid returning raw JSON strings from `RagClient`.
  - [ ] Keep validation at BE boundary before proxying.
- [ ] Centralize status constants:
  - [ ] source statuses
  - [ ] ingestion job statuses
  - [ ] chat request statuses
  - [ ] task statuses
- [ ] Normalize API error envelopes:
  - [ ] consistent `error.code`
  - [ ] consistent `error.message`
  - [ ] request/correlation id
- [ ] Improve logs:
  - [ ] Add correlation id across BE, AI, and RAG calls.
  - [ ] Redact request logs that may contain prompts, uploaded text, or secrets.
  - [ ] Add retention policy for request logs.

**Deliverable:** Controllers become thin orchestration layers with explicit DTOs and reusable ownership checks.

**Review references:**
- `be-server/BeServer/Content/ChatSessionsController.cs`
- `be-server/BeServer/Services/RagClient.cs`
- `be-server/BeServer/Content/SearchController.cs`

---

## Phase 13 - Extensibility: Models, RAG Config, and Providers

**Goal:** Make model and retrieval changes configurable, testable, and reversible.

- [ ] Add server-side model registry:
  - [ ] `chat_default`
  - [ ] `agent_default`
  - [ ] `summary_default`
  - [ ] allowed model list per environment
  - [ ] max output/token/cost controls
- [ ] Stop accepting arbitrary model names from frontend:
  - [ ] Frontend sends a preset or mode.
  - [ ] BE resolves preset to configured model.
  - [ ] AI server validates model against allowlist.
- [ ] Complete LLM gateway abstraction:
  - [ ] Streaming chat completion.
  - [ ] non-streaming structured output.
  - [ ] embeddings or separate embedding gateway.
  - [ ] provider-specific error normalization.
- [ ] Make RAG config explicit:
  - [ ] chunk size
  - [ ] overlap
  - [ ] embedding model
  - [ ] embedding dimensions
  - [ ] search mode
  - [ ] top_k
  - [ ] hybrid alpha
- [ ] Store RAG config snapshot:
  - [ ] on source ingestion
  - [ ] on experiment run
  - [ ] on chat request context snapshot
- [ ] Add migration path for future retrieval versions:
  - [ ] re-ingest by source
  - [ ] re-embed by notebook
  - [ ] compare retrieval versions in experiments

**Deliverable:** Model/RAG changes are made through config and versioned metadata, not scattered code edits.

**Review references:**
- `ai-server/app/gateway/openai_provider.py`
- `ai-server/app/main.py`
- `ai-server/app/agent.py`
- `rag-server/app/embedder.py`
- `rag-server/app/chunker.py`

---

## Phase 14 - Multi-User and Data Isolation Hardening

**Goal:** Prove that multi-user data boundaries hold across SQL, ArangoDB, files, and agent tools.

- [ ] Add isolation tests for every user-scoped API:
  - [ ] notebooks
  - [ ] sources
  - [ ] notes
  - [ ] search
  - [ ] chat sessions
  - [ ] agent tools
  - [ ] experiments
- [ ] Ensure RAG chunks carry enough ownership metadata:
  - [ ] source_id
  - [ ] notebook_id
  - [ ] user_id where practical
- [ ] Add ArangoDB cleanup checks:
  - [ ] deleting source removes chunks
  - [ ] deleting notebook removes all related vector records
  - [ ] deleting user removes all related vector records
- [ ] Validate agent tool calls:
  - [ ] tool calls must use active notebook unless explicitly allowed
  - [ ] BE remains final authorization layer
  - [ ] no direct user-provided notebook id bypass

**Deliverable:** Cross-user access attempts are covered by automated tests and fail consistently.

---

## Phase 15 - Deployment and Runtime Operations

**Goal:** Prepare the system for a real deployment environment.

- [ ] Add production compose or deployment profile:
  - [ ] no default development secrets
  - [ ] production `ASPNETCORE_ENVIRONMENT`
  - [ ] persistent volume backup guidance
  - [ ] no direct database exposure
- [ ] Add backup/restore runbooks:
  - [ ] MySQL
  - [ ] ArangoDB
  - [ ] uploaded files volume
- [ ] Add health/readiness split:
  - [ ] liveness endpoint
  - [ ] readiness endpoint that checks dependencies
- [ ] Add basic metrics:
  - [ ] request count/latency/error rate
  - [ ] ingestion queue depth
  - [ ] ingestion duration
  - [ ] LLM request latency/errors
  - [ ] retrieval latency
- [ ] Add dependency update policy:
  - [ ] Docker base image update cadence
  - [ ] Python dependency lock strategy
  - [ ] npm audit policy
  - [ ] .NET package audit

**Deliverable:** Operators can deploy, monitor, back up, and recover the system with documented procedures.

---

## Updated Future Backlog

- GraphRAG with ArangoDB graph traversal.
- vLLM/local model provider behind the LLM gateway.
- Full user management UI.
- Organization/team sharing model.
- Podcast-style audio summary.
- Document redaction and PII detection.
- Evaluation dataset builder for RAG experiments.
- Admin dashboard for ingestion jobs, failed requests, and usage.
