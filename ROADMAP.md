# RAG System — Project Roadmap

## Architecture Overview

```
[Browser]
    │  port 5987 (only exposed port)
    ▼
[Frontend: React + Vite + TS]
    │  internal network
    ▼
[BE Server: .NET 8 Web API]  ──→  [MySQL 8]
    │  internal
    ▼
[AI Server: Python FastAPI]  ──→  [LLM Gateway → OpenAI / future: vllm]
    │  internal
    ▼
[RAG Server: Python FastAPI] ──→  [ArangoDB]  (vectors + BM25 + future GraphRAG)
```

### Services & Ports (all internal except frontend)

| Service    | Image/Stack         | Internal Port | Exposed |
|------------|---------------------|--------------|---------|
| frontend   | React + Vite + Nginx| 80           | **5987** |
| be-server  | .NET 8 Web API      | 8001         | no      |
| ai-server  | Python FastAPI      | 8002         | no      |
| rag-server | Python FastAPI      | 8003         | no      |
| mysql      | MySQL 8             | 3306         | no      |
| arangodb   | ArangoDB 3.12       | 8529         | no      |

### ID Strategy

| Entity          | Type        | Length | Notes                    |
|-----------------|-------------|--------|--------------------------|
| users           | UUID v4     | 36     | CHAR(36), high-value key |
| notebooks       | UUID v4     | 36     | CHAR(36)                 |
| sources         | UUID v4     | 36     | CHAR(36)                 |
| chunks          | UUID v4     | 36     | ArangoDB `_key`          |
| chat_sessions   | UUID v4     | 36     | CHAR(36)                 |
| messages        | UUID v4     | 36     | CHAR(36)                 |
| notes           | UUID v4     | 36     | CHAR(36)                 |

---

## Phase Workflow (every phase follows this sequence)

1. **Plan** — detailed implementation plan written to `docs/superpowers/plans/`
2. **Branch** — `git checkout -b phase-N-<short-name>` from `main`
3. **Implement** — follow the plan task by task, commit on the feature branch
4. **Code Review** — agent writes `docs/reviews/phase-N-review.md` (security / performance / maintainability / logic)
5. **Patch branch** — `git checkout -b phase-N-patch` **from `phase-N-<short-name>`** (NOT from main)
6. **Apply fixes** on `phase-N-patch`, then **rebase back onto `phase-N-<short-name>`**:
   ```
   git rebase phase-N-<short-name>
   git checkout phase-N-<short-name>
   git merge --ff-only phase-N-patch
   ```
7. **Single PR** — `phase-N-<short-name>` → `main` (one clean PR containing feature + patch commits)
8. **Merge & update ROADMAP** — mark phase complete, record learnings

> **Note (Phase 1 correction):** Phase 1 incorrectly merged `phase-1-auth` and `phase-1-patch`
> as two separate PRs directly into main. From Phase 2 onward the patch is rebased onto the
> feature branch first, resulting in a single PR per phase.

---

## Phase 0 — Infrastructure & Scaffolding ✅
**Goal:** Every service starts, passes health checks, and can reach its database. No real features yet.

- [x] `docker-compose.yml` — all 6 services on a single internal bridge network, only frontend port exposed
- [x] `.env.template` — all required variables documented
- [x] `.gitignore` — covers Python, .NET, Node, Docker secrets
- [x] Minimal Dockerfiles for each service (multi-stage where sensible)
- [x] `GET /health` endpoint on be-server, ai-server, rag-server (returns `{"status":"ok"}`)
- [x] MySQL init script — `users` table skeleton
- [x] ArangoDB init script — `rag_db` database + collections skeleton
- [x] React app scaffold — Vite + TypeScript + Tailwind, login page placeholder
- [x] All services reachable across the internal network

**Deliverable:** `docker compose up` → all containers healthy.

**Note:** Smoke test (`docker compose up`) requires a real `.env` file — copy `.env.template` and fill in values before running.

---

## Phase 1 — Authentication & User Management ✅
**Goal:** JWT-based login; single user from `.env`; SQL schema ready for multi-user.

- [x] MySQL schema: `users` table (id CHAR(36), username, hashed_password, created_at)
- [x] BE server: `POST /api/auth/login` → JWT access token + httpOnly refresh cookie
- [x] BE server: JWT middleware — Bearer token validation via `AddAuthentication`
- [x] Frontend: Login page → stores JWT in memory (AuthContext), httpOnly refresh cookie
- [x] Frontend: Protected route wrapper (`ProtectedRoute.tsx`)
- [x] Single user bootstrapped from `.env` on first startup (idempotent seed)

**Deliverable:** Login flow works end-to-end.

**Learnings:**
- `/api/auth/refresh` is stubbed as 501 until Phase 2-ish adds a `refresh_tokens` table — users must re-login each session for now.
- `ASPNETCORE_ENVIRONMENT` is now parameterized; set to `Production` in real deployments.
- BCrypt work factor 12, constant-time login (always verify to prevent username enumeration).

---

## Phase 2 — Notebook & Content Management
**Goal:** CRUD for notebooks, sources (file upload), and notes; stored in MySQL; files on disk (Docker volume).

- [ ] MySQL schema: `notebooks`, `sources`, `notes`, `chat_sessions` tables
- [ ] BE server: REST endpoints for notebooks, sources (file upload), notes
- [ ] RAG server: `POST /ingest` — accept file bytes, persist to ArangoDB raw document collection (no chunking yet)
- [ ] Frontend: Notebook list, notebook detail, source upload, note editor (markdown)
- [ ] File storage: Docker named volume mounted to both be-server and rag-server

**Deliverable:** Create a notebook, upload a PDF, write a note — all persisted and visible in the UI.

---

## Phase 3 — LLM Gateway & AI Server
**Goal:** Thin gateway abstraction over OpenAI; AI server exposes a chat-completion endpoint; no RAG context yet.

- [ ] LLM Gateway interface: `complete(messages, model, stream) → AsyncIterator[str]`
- [ ] OpenAI provider implementation
- [ ] AI server: `POST /chat/completions` — proxies through gateway, supports SSE streaming
- [ ] Frontend: Chat panel — sends messages, renders streamed response
- [ ] Configuration: model name + API key from `.env` via gateway config

**Deliverable:** Chat with the LLM from the UI without RAG context.

---

## Phase 4 — RAG Pipeline (Vector Search)
**Goal:** Ingest → chunk → embed → store in ArangoDB; vector similarity search endpoint.

- [ ] Chunking strategy: recursive character splitter, configurable `chunk_size` / `chunk_overlap`
- [ ] Embedding: OpenAI `text-embedding-3-small` via LLM Gateway
- [ ] ArangoDB: `chunks` collection with `embedding` field; `vector_index` (HNSW)
- [ ] RAG server: `POST /ingest` triggers chunking + embedding pipeline (async background task)
- [ ] RAG server: `GET /search/vector?q=...&top_k=5&notebook_id=...` → ranked chunks
- [ ] AI server: augment chat prompt with retrieved chunks (basic RAG flow)
- [ ] Frontend: chat panel shows "sources cited" panel

**Deliverable:** Ask a question about an uploaded document and get a grounded answer.

---

## Phase 5 — Sparse Search (BM25) & Hybrid Search
**Goal:** ArangoDB full-text analyzer for BM25; hybrid re-ranker combining vector + BM25 scores.

- [ ] ArangoDB: `ArangoSearch` view on `chunks` collection with text analyzer
- [ ] RAG server: `GET /search/bm25?q=...` → BM25-ranked chunks
- [ ] Hybrid search: RRF (Reciprocal Rank Fusion) combining vector + BM25 results
- [ ] RAG server: `GET /search/hybrid?q=...` (default search used by AI server)
- [ ] Configurable weight `alpha` for vector vs BM25 blend
- [ ] Benchmark endpoint: run vector / BM25 / hybrid side by side for a query, return comparison JSON
- [ ] Frontend: search results panel with mode selector (vector / BM25 / hybrid)

**Deliverable:** Hybrid search demonstrably outperforms pure vector search on keyword-heavy queries.

---

## Phase 6 — AI Agent System
**Goal:** Tool-calling agent loop; tools: search, create_note, list_notebooks, get_source.

- [ ] Agent framework: simple ReAct loop using function-calling
- [ ] Tools: `search_knowledge`, `create_note`, `list_notebooks`, `get_source_content`
- [ ] AI server: `POST /agent/run` — runs agent, returns full trace + final answer (SSE)
- [ ] Notebook-scoped context: agent only searches within the active notebook (or global)
- [ ] Frontend: agent mode toggle in chat panel; shows tool call trace

**Deliverable:** Agent autonomously searches knowledge base, creates a note, and answers with citations.

---

## Phase 7 — Polish & Experimentation Tooling
**Goal:** Stable, usable product; experiment framework to compare RAG configurations.

- [ ] Experiment runner: parametric test runs (embedding model, chunk size, search mode, top_k)
- [ ] Results stored in ArangoDB `experiments` collection
- [ ] Frontend: experiment dashboard — run config, latency, relevance scores
- [ ] Multi-user readiness: enforce `user_id` FK across all SQL tables; ArangoDB one-db-per-user migration helper
- [ ] Rate limiting, input validation hardening
- [ ] Structured logging (JSON) across all services

**Deliverable:** Can run A/B experiments on RAG config and compare results in the UI.

---

## Future Backlog (not scheduled)
- GraphRAG: ArangoDB graph traversal for entity-relationship reasoning
- vLLM / local model provider in LLM Gateway
- Multi-user promotion (full user management UI)
- Podcast-style audio summary (open-notebook inspiration)
- CI/CD pipeline
