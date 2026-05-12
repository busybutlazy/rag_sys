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

## Phase 2 — Notebook & Content Management ✅
**Goal:** CRUD for notebooks, sources (file upload), and notes; stored in MySQL; files on disk (Docker volume).

- [x] MySQL schema: `notebooks`, `sources`, `notes`, `chat_sessions` tables
- [x] BE server: REST endpoints for notebooks, sources (file upload), notes
- [x] RAG server: `POST /ingest` + `DELETE /documents/:id`, guarded by `X-Internal-Secret`
- [x] Frontend: Notebook list, notebook detail, source upload, note editor
- [x] File storage: Docker named volume mounted to both be-server and rag-server

**Deliverable:** Create a notebook, upload a PDF, write a note — all persisted and visible in the UI.

**Learnings:**
- Fire-and-forget background tasks must use `IServiceScopeFactory` for a fresh `DbContext` scope.
- DB record should be written before the file (avoids orphaned files on DB failure).
- All inter-service endpoints need an `INTERNAL_SECRET` header to prevent unauthenticated access.
- MIME allowlist on upload prevents arbitrary file types; allowlist currently: PDF, text, markdown, CSV, JSON, DOCX.

---

## Phase 3 — LLM Gateway & AI Server ✅
**Goal:** Thin gateway abstraction over OpenAI; AI server exposes a chat-completion endpoint; no RAG context yet.

- [x] LLM Gateway interface: `LLMGateway` ABC with `stream_complete(messages, model) → AsyncGenerator[str, None]`
- [x] OpenAI provider implementation (`AsyncOpenAI`, `stream=True`)
- [x] AI server: `POST /chat/completions` — JWT-guarded, SSE streaming with `[DONE]` sentinel and error envelope
- [x] Frontend: ChatPanel — SSE streaming, stop button, blinking cursor, auto-scroll
- [x] Configuration: model name from request, API key + JWT_SECRET from `.env`

**Deliverable:** Chat with the LLM from the UI without RAG context.

**Learnings:**
- ai-server validates the same JWT (issuer/audience) as be-server — ensures tokens can't be used cross-service without the shared secret.
- `JWT_SECRET` startup guard (SEC-01 from review) catches misconfiguration immediately; match this pattern in every service that holds a signing secret.
- SSE buffer accumulation (`buf = lines.pop()`) is required to handle chunks that split across `data:` lines.
- `notebook_id` is reserved on `ChatRequest` (nullable, unused until Phase 4 RAG injection).

---

## Phase 4 — RAG Pipeline (Vector Search) ✅
**Goal:** Ingest → chunk → embed → store in ArangoDB; vector similarity search endpoint.

- [x] Chunking: sliding-window character splitter (`chunk_size=800`, `chunk_overlap=100`)
- [x] Text extraction: PDF (`pypdf`), DOCX (`python-docx`), plain text / JSON / CSV / Markdown
- [x] Embedding: OpenAI `text-embedding-3-small` (1536 dims), batched `embed_batch`
- [x] ArangoDB: `chunks` collection with HNSW vector index (`metric=cosine`, `dimension=1536`)
- [x] RAG server: `POST /ingest` — extract → chunk → embed → store; status tracked in `documents` collection
- [x] RAG server: `GET /search/vector?q=&notebook_id=&top_k=` — AQL `APPROX_NEAR_COSINE` search
- [x] RAG server: `DELETE /documents/{id}` also removes all associated chunks
- [x] AI server: `rag_client.py` + context injection as system message when `notebook_id` present
- [x] AI server: emits `data: {"sources": [...]}` SSE event before token stream
- [x] Frontend: ChatPanel shows collapsible "N sources cited" under assistant messages
- [x] `IngestRequest` includes `notebook_id` so chunks are scoped per notebook

**Deliverable:** Ask a question about an uploaded document and get a grounded answer.

**Learnings:**
- `_check_secret` must be fail-closed: always compare; raise `SystemExit` at startup if secret unset (SEC-01).
- Chunks need `notebook_id` on every document for per-notebook AQL filtering; add it to `IngestRequest` and thread it through be-server → rag-server.
- `delete_chunks` must run before `store_chunks` on re-ingest, and on `DELETE /documents` — otherwise stale chunks accumulate in ArangoDB.
- Two separate GitHub PRs per phase (`phase-N-xxx` → main, then `phase-N-patch` → main) keeps PR history clean with no cross-phase noise.

---

## Phase 5 — Sparse Search (BM25) & Hybrid Search ✅
**Goal:** ArangoDB full-text analyzer for BM25; hybrid re-ranker combining vector + BM25 scores.

- [x] ArangoDB: `ArangoSearch` view (`chunks_view`) created at startup with `text_en` analyzer on `text` field
- [x] RAG server: `GET /search/bm25?q=...` → BM25-ranked chunks via AQL `SEARCH` + `BM25()`
- [x] Hybrid search: RRF (Reciprocal Rank Fusion) combining vector + BM25 results
- [x] RAG server: `GET /search/hybrid?q=...` (default search used by AI server)
- [x] Configurable weight `alpha` for vector vs BM25 blend (default 0.5)
- [x] Benchmark endpoint: `GET /search/benchmark` — runs all three modes, returns comparison JSON
- [x] Frontend: `SearchPanel` with vector / BM25 / hybrid / benchmark mode selector in notebook detail page
- [x] BE server: `GET /api/notebooks/{id}/search` and `.../benchmark` proxy endpoints with JWT + ownership check

**Deliverable:** Hybrid search demonstrably outperforms pure vector search on keyword-heavy queries.

**Learnings:**
- `proxy_pass http://backend/;` (trailing slash) in nginx strips the location prefix — all BE `/api/` routes were returning 404 in Docker. Always omit the trailing slash: `proxy_pass http://backend;`.
- ArangoSearch view must explicitly declare the `text_en` analyzer on the `text` field; the `notebook_id` field needs the `identity` analyzer for exact-match filtering inside `SEARCH`.
- RRF constant `k=60` is standard (Cormack et al., 2009). Alpha weighting lets users tune vector vs BM25 contribution without changing the rank-fusion formula.
- Benchmark endpoint is intentionally allowed to make 2× DB calls (hybrid internally re-runs vector + BM25 with larger `fetch_k`) — simplicity over micro-optimization for an experiment tool.

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
