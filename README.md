# RAG System

[繁體中文](./README.zh-TW.md) | English

> **Vibe coding practice project** — designed to be built phase by phase with Claude Code as the primary implementer. Each phase follows a structured plan-implement-review-patch cycle, letting the AI agent carry the full development loop while the human steers intent and reviews results.

---

## What This Is

A full-stack Retrieval-Augmented Generation (RAG) system built as an intentional learning exercise in **AI-assisted software development**. The project is defined entirely through roadmaps (`ROADMAP.md`, `ROADMAP2.md`), and Claude Code completes each phase autonomously — writing code, performing self-review, applying patches, and opening pull requests.

The result is a working product *and* a record of how an AI agent navigates real engineering decisions across a multi-service architecture.

---

## Architecture

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
[AI Server: Python FastAPI]  ──→  [LLM Gateway → OpenAI]
    │  internal
    ▼
[RAG Server: Python FastAPI] ──→  [ArangoDB]  (vectors + BM25 + GraphRAG planned)
```

### Services

| Service     | Stack                | Internal Port | Exposed  |
|-------------|----------------------|--------------|----------|
| frontend    | React + Vite + Nginx | 80           | **5987** |
| be-server   | .NET 8 Web API       | 8001         | no       |
| ai-server   | Python FastAPI       | 8002         | no       |
| rag-server  | Python FastAPI       | 8003         | no       |
| mysql       | MySQL 8              | 3306         | no       |
| arangodb    | ArangoDB 3.12        | 8529         | no       |

Only the frontend port is exposed to the host. All inter-service communication happens over an internal Docker bridge network.

---

## Features (Completed Phases)

| Phase | Feature |
|-------|---------|
| 0 | Infrastructure & Docker scaffolding |
| 1 | JWT authentication (login, protected routes) |
| 2 | Notebook & content management (CRUD, file upload) |
| 3 | LLM gateway + streaming chat (OpenAI, SSE) |
| 4 | RAG pipeline (PDF/DOCX ingestion → chunking → embedding → ArangoDB vector search) |
| 5 | Hybrid search (BM25 + vector via RRF, benchmark tooling) |
| 6 | AI agent system (ReAct loop, tool calling, notebook-scoped search) |
| 7 | Experiment dashboard & RAG config A/B testing |

Hardening phases (8–15) are defined in `ROADMAP2.md` and cover auth session correctness, ingestion reliability, upload security, test suites, API maintainability, extensibility, multi-user isolation, and production deployment.

Current note: chat conversation session orchestration is already implemented on `main` (persisted chat sessions, messages, requests, tasks, and frontend session switching). This is separate from Phase 8 auth/session hardening, which is still open.

---

## Vibe Coding Workflow

This project is built by having Claude Code complete the roadmap one phase at a time. Each phase follows a fixed sequence:

```
1. Plan    →  implementation plan written to docs/superpowers/plans/
2. Branch  →  git checkout -b phase-N-<name>
3. Build   →  Claude Code implements the plan, commits on the feature branch
4. Review  →  agent writes docs/reviews/phase-N-review.md
5. Patch   →  git checkout -b phase-N-patch (from feature branch)
6. Rebase  →  patch rebased onto feature branch, merged --ff-only
7. PR      →  single clean PR: phase-N-<name> → main
8. Merge   →  ROADMAP updated, learnings recorded
```

The human role is to:
- Define intent in the roadmap
- Review PRs and code-review documents
- Steer scope if the agent drifts
- Approve merges

Claude Code's role is to:
- Read the roadmap phase and write an implementation plan
- Implement, commit, and self-review
- Produce patch branches for review findings
- Open pull requests

---

## Installation

### Prerequisites

| Tool | Minimum Version | Notes |
|------|----------------|-------|
| Docker | 24+ | |
| Docker Compose | v2 (plugin) | `docker compose` not `docker-compose` |
| OpenAI API key | — | For embeddings and chat completions |

No other runtimes (.NET, Python, Node) are required — everything runs inside containers.

---

### Step 1 — Clone

```bash
git clone <repo-url>
cd rag_sys
```

---

### Step 2 — Configure environment

```bash
cp .env.template .env
```

Open `.env` and fill in the required values:

| Variable | Required | Description |
|----------|----------|-------------|
| `OPENAI_API_KEY` | Yes | Your OpenAI API key (`sk-...`) |
| `ADMIN_USERNAME` | Yes | Login username for the single built-in user |
| `ADMIN_PASSWORD` | Yes | Login password — use something strong |
| `JWT_SECRET` | Yes | JWT signing secret, **minimum 32 characters** |
| `INTERNAL_SECRET` | Yes | Shared secret for internal service-to-service calls |
| `MYSQL_ROOT_PASSWORD` | Yes | MySQL root password |
| `MYSQL_PASSWORD` | Yes | MySQL application user password |
| `ARANGO_ROOT_PASSWORD` | Yes | ArangoDB root password |
| `ARANGO_PASSWORD` | Yes | ArangoDB application user password |

> The remaining variables (`MYSQL_DATABASE`, `MYSQL_USER`, ports, etc.) have sensible defaults in `.env.template` and usually don't need changing.

---

### Step 3 — Start all services

```bash
docker compose up --build
```

First startup takes a few minutes — Docker pulls images and builds each service.

<!-- TODO: add screenshot of docker compose up output -->

---

### Step 4 — Verify

Once all containers are healthy, open:

```
http://localhost:5987
```

You should see the login page. Sign in with the `ADMIN_USERNAME` / `ADMIN_PASSWORD` you set in `.env`.

<!-- TODO: add screenshot of login page -->

<!-- TODO: add screenshot of main dashboard -->

You can also confirm every service is up by checking the health endpoints from inside the Docker network, or simply watching the compose logs for `Healthy` status on all containers.

---

### Development mode (hot reload)

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up --build
```

This mounts source directories into the containers so changes are picked up without a full rebuild:

- **Frontend** — Vite dev server with HMR
- **AI server / RAG server** — Uvicorn `--reload`
- **BE server** — `dotnet watch`

---

## Project Structure

```
rag_sys/
├── frontend/          # React + Vite + TypeScript + Tailwind
├── be-server/         # .NET 8 Web API (auth, notebooks, sources, notes, sessions)
├── ai-server/         # Python FastAPI (LLM gateway, chat, agent)
├── rag-server/        # Python FastAPI (ingestion, chunking, embedding, search)
├── db/                # MySQL init scripts
├── docs/
│   ├── superpowers/plans/   # Phase implementation plans (written by Claude Code)
│   └── reviews/             # Phase code reviews (written by Claude Code)
├── scripts/           # Utility scripts
├── ROADMAP.md         # Feature phases 0–7 + future backlog
├── ROADMAP2.md        # Hardening phases 8–15
└── .env.template      # All required environment variables documented
```

---

## Roadmap Status

See [`ROADMAP.md`](./ROADMAP.md) for feature phases and [`ROADMAP2.md`](./ROADMAP2.md) for hardening phases.

**Phases 0–7:** Complete  
**Chat conversation sessions:** Implemented on `main`  
**Phases 8–15:** Defined; Phase 8 auth/session hardening is the next recommended step

---

## Future Backlog

- GraphRAG with ArangoDB graph traversal
- vLLM / local model provider behind the LLM gateway
- Full user management UI
- Organization/team sharing model
- Podcast-style audio summary
- Document redaction and PII detection
- Evaluation dataset builder for RAG experiments
- Admin dashboard for ingestion jobs and usage
