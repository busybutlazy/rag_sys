# Phase 0 — Infrastructure & Scaffolding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every service starts, passes a `/health` check, and can reach its database. Zero features — just the skeleton that all later phases build on.

**Architecture:** Six Docker containers on one internal bridge network (`rag_net`). Only the React frontend exposes port 5987. All inter-service calls use Docker service names (e.g. `http://be-server:8001`). MySQL and ArangoDB are initialized with skeleton schemas. Every service has a multi-stage Dockerfile.

**Tech Stack:**
- Frontend: React 18 + Vite 5 + TypeScript + Tailwind CSS 3
- BE Server: .NET 8 Minimal API (C#)
- AI Server: Python 3.12 + FastAPI 0.111 + uv
- RAG Server: Python 3.12 + FastAPI 0.111 + uv + python-arango 7.x
- MySQL 8.0, ArangoDB 3.12, Docker Compose v2

---

## File Map

```
rag_sys/
├── docker-compose.yml
├── .env.template
├── .gitignore
├── ROADMAP.md
│
├── frontend/
│   ├── Dockerfile
│   ├── nginx.conf
│   ├── package.json
│   ├── vite.config.ts
│   ├── tsconfig.json
│   ├── tailwind.config.ts
│   ├── postcss.config.js
│   ├── index.html
│   └── src/
│       ├── main.tsx
│       ├── index.css
│       ├── App.tsx
│       └── pages/
│           └── LoginPage.tsx
│
├── be-server/
│   ├── Dockerfile
│   ├── BeServer.sln
│   └── BeServer/
│       ├── BeServer.csproj
│       └── Program.cs
│
├── ai-server/
│   ├── Dockerfile
│   ├── pyproject.toml
│   └── app/
│       ├── __init__.py
│       └── main.py
│
├── rag-server/
│   ├── Dockerfile
│   ├── pyproject.toml
│   └── app/
│       ├── __init__.py
│       ├── db.py
│       └── main.py
│
└── db/
    ├── mysql/
    │   └── 01_init.sql
    └── arango/
        └── init.js
```

---

## Task 1: Root Config Files

**Files:**
- Create: `.env.template`
- Create: `.gitignore`
- Create: `docker-compose.yml`

- [ ] **Step 1: Verify `.env.template` exists**

```bash
cat .env.template
```

Expected: file shows OPENAI_API_KEY, ADMIN_USERNAME, JWT_SECRET, MySQL and ArangoDB vars.

- [ ] **Step 2: Verify `.gitignore` exists**

```bash
cat .gitignore
```

Expected: covers `.env`, Python `__pycache__`, .NET `bin/obj/`, Node `node_modules/`.

- [ ] **Step 3: Verify `docker-compose.yml` exists**

```bash
docker compose config --quiet && echo "VALID"
```

Expected: prints `VALID` with no errors. (Requires `.env` to exist — copy from `.env.template` first.)

- [ ] **Step 4: Commit**

```bash
git add .env.template .gitignore docker-compose.yml
git commit -m "chore: add root config files and docker-compose scaffold"
```

---

## Task 2: MySQL Init Script

**Files:**
- Create: `db/mysql/01_init.sql`

- [ ] **Step 1: Verify init script**

```bash
cat db/mysql/01_init.sql
```

Expected: `CREATE TABLE IF NOT EXISTS users` with columns `id CHAR(36), username, password_hash, created_at, updated_at`.

- [ ] **Step 2: Commit**

```bash
git add db/mysql/01_init.sql
git commit -m "chore: add MySQL init script with users table skeleton"
```

---

## Task 3: ArangoDB Init Script

**Files:**
- Create: `db/arango/init.js`

- [ ] **Step 1: Verify init script**

```bash
cat db/arango/init.js
```

Expected: creates `rag_db` database, creates `raguser`, creates collections `documents`, `chunks`, `notebooks`.

- [ ] **Step 2: Commit**

```bash
git add db/arango/init.js
git commit -m "chore: add ArangoDB init script with skeleton collections"
```

---

## Task 4: BE Server (.NET 8 Minimal API)

**Files:**
- Create: `be-server/Dockerfile`
- Create: `be-server/BeServer.sln`
- Create: `be-server/BeServer/BeServer.csproj`
- Create: `be-server/BeServer/Program.cs`

- [ ] **Step 1: Verify Program.cs has /health endpoint**

```bash
grep -n "health" be-server/BeServer/Program.cs
```

Expected: `app.MapGet("/health", ...)` line present.

- [ ] **Step 2: Commit**

```bash
git add be-server/
git commit -m "chore: scaffold .NET 8 be-server with /health endpoint"
```

---

## Task 5: AI Server (Python FastAPI)

**Files:**
- Create: `ai-server/Dockerfile`
- Create: `ai-server/pyproject.toml`
- Create: `ai-server/app/__init__.py`
- Create: `ai-server/app/main.py`

- [ ] **Step 1: Verify /health endpoint**

```bash
grep -n "health" ai-server/app/main.py
```

Expected: `@app.get("/health")` present.

- [ ] **Step 2: Commit**

```bash
git add ai-server/
git commit -m "chore: scaffold Python FastAPI ai-server with /health endpoint"
```

---

## Task 6: RAG Server (Python FastAPI + python-arango)

**Files:**
- Create: `rag-server/Dockerfile`
- Create: `rag-server/pyproject.toml`
- Create: `rag-server/app/__init__.py`
- Create: `rag-server/app/db.py`
- Create: `rag-server/app/main.py`

`db.py` uses a module-level singleton so ArangoDB client is created once:

```python
import os
from arango import ArangoClient

_db = None

def get_db():
    global _db
    if _db is None:
        client = ArangoClient(hosts=os.environ["ARANGO_URL"])
        _db = client.db(
            os.environ["ARANGO_DB"],
            username=os.environ["ARANGO_USER"],
            password=os.environ["ARANGO_PASSWORD"],
        )
    return _db
```

`main.py` calls `db.version()` in `/health` to verify the connection is live:

```python
from fastapi import FastAPI
from app.db import get_db

app = FastAPI(title="RAG Server", version="0.1.0")

@app.get("/health")
async def health():
    db = get_db()
    db.version()
    return {"status": "ok", "service": "rag-server"}
```

- [ ] **Step 1: Verify files**

```bash
grep -n "version" rag-server/app/main.py
```

Expected: `db.version()` present in health handler.

- [ ] **Step 2: Commit**

```bash
git add rag-server/
git commit -m "chore: scaffold Python FastAPI rag-server with /health + ArangoDB connection"
```

---

## Task 7: Frontend (React + Vite + TypeScript + Tailwind)

**Files:**
- Create: `frontend/Dockerfile`
- Create: `frontend/nginx.conf`
- Create: `frontend/package.json`
- Create: `frontend/vite.config.ts`
- Create: `frontend/tsconfig.json`
- Create: `frontend/tailwind.config.ts`
- Create: `frontend/postcss.config.js`
- Create: `frontend/index.html`
- Create: `frontend/src/index.css`
- Create: `frontend/src/main.tsx`
- Create: `frontend/src/App.tsx`
- Create: `frontend/src/pages/LoginPage.tsx`

nginx reverse-proxies `/api/` → `be-server:8001` and `/ai/` → `ai-server:8002` so the frontend never needs to know internal ports.

- [ ] **Step 1: Verify nginx proxy config**

```bash
grep -A3 "location /api" frontend/nginx.conf
```

Expected: `proxy_pass http://be-server:8001/;`

- [ ] **Step 2: Commit**

```bash
git add frontend/
git commit -m "chore: scaffold React + Vite frontend with login page placeholder"
```

---

## Task 8: Smoke Test — Full Stack Boot

**Prerequisites:** Docker Desktop running. Copy `.env.template` to `.env` before starting.

- [ ] **Step 1: Copy env file**

```bash
cp .env.template .env
```

- [ ] **Step 2: Build all images**

```bash
docker compose build --no-cache
```

Expected: all 5 service images build without error.

- [ ] **Step 3: Start all services**

```bash
docker compose up -d
```

- [ ] **Step 4: Wait for health checks**

```bash
docker compose ps
```

Run until all services show `(healthy)`. MySQL and ArangoDB may take 30–60 seconds on first boot.

- [ ] **Step 5: Verify be-server via nginx proxy**

```bash
curl -s http://localhost:5987/api/health
```

Expected:
```json
{"status":"ok","service":"be-server"}
```

- [ ] **Step 6: Verify ai-server via nginx proxy**

```bash
curl -s http://localhost:5987/ai/health
```

Expected:
```json
{"status":"ok","service":"ai-server"}
```

- [ ] **Step 7: Verify rag-server + ArangoDB connection**

```bash
docker compose exec rag-server curl -s http://localhost:8003/health
```

Expected:
```json
{"status":"ok","service":"rag-server"}
```

- [ ] **Step 8: Verify ArangoDB collections**

```bash
docker compose exec arangodb arangosh \
  --server.password arangopass \
  --javascript.execute-string "db._useDatabase('rag_db'); print(db._collections().map(c => c.name()).join(', '));"
```

Expected: output includes `documents, chunks, notebooks`.

- [ ] **Step 9: Verify MySQL schema**

```bash
docker compose exec mysql mysql -u raguser -pragpass rag_sys \
  -e "SHOW TABLES; DESCRIBE users;"
```

Expected: `users` table with columns `id, username, password_hash, created_at, updated_at`.

- [ ] **Step 10: Open frontend**

Navigate to `http://localhost:5987` — login page renders with username/password fields.

- [ ] **Step 11: Tear down**

```bash
docker compose down
```

- [ ] **Step 12: Final commit**

```bash
git add .
git commit -m "chore: Phase 0 complete — all health checks verified"
```

---

## Self-Review

**Spec coverage:**
- [x] Docker compose internal bridge network — Task 1
- [x] Only port 5987 exposed — Task 1 (frontend maps `FRONTEND_PORT:80`)
- [x] `.env.template` — Task 1
- [x] MySQL skeleton schema — Task 2
- [x] ArangoDB skeleton schema + collections — Task 3
- [x] BE server `/health` — Task 4
- [x] AI server `/health` — Task 5
- [x] RAG server `/health` + ArangoDB connection verification — Task 6
- [x] React frontend with login page placeholder — Task 7
- [x] End-to-end smoke test — Task 8
