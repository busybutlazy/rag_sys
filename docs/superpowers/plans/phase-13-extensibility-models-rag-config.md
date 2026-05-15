# Phase 13 — Extensibility: Models, RAG Config, and Providers

**Branch:** `phase-13-extensibility-models-rag-config`  
**Goal:** Make model and retrieval changes configurable through env/config rather than scattered hardcoded literals. Provide a re-ingest path for when RAG config changes.

---

## Current state (gaps to close)

| Surface | Problem |
|---|---|
| `ChatSessionsController.cs:185` | Frontend can send any `model` string; BE uses it verbatim or falls back to `"gpt-4o-mini"` |
| `AgentRunRequest` / `ChatRequest` (AI server) | Accepts arbitrary `model` from BE with no validation |
| `gateway/openai_provider.py` | Only implements `stream_complete`; non-streaming structured output is called directly via `_gateway._client` in `main.py` |
| `embedder.py` | Embedding model/dimensions are module-level `_MODEL` / `_DIMENSIONS` constants with no abstraction layer |
| `chunker.py` | `chunk_size=800`, `chunk_overlap=100` are function-default literals |
| `vector_store.py` | `top_k=5`, `alpha=0.5` are function-default literals |
| ArangoDB `documents` record | Does not store the chunking/embedding config used during ingestion |
| `ChatRequest.ContextSnapshotJson` | Does not snapshot the search config used to retrieve context |
| No re-ingest path | Changing RAG config has no way to re-process existing sources |

---

## Step 1 — Server-side model registry (BE)

**Files:** `be-server/BeServer/Services/ModelRegistry.cs` (new), `be-server/BeServer/appsettings.json`, `be-server/BeServer/Program.cs`, `be-server/BeServer/Content/ChatSessionsController.cs`

### 1a. `appsettings.json` — add `Models` section

```json
"Models": {
  "ChatDefault":  "gpt-4o-mini",
  "AgentDefault": "gpt-4o-mini",
  "SummaryDefault": "gpt-4o-mini",
  "Allowed": "gpt-4o-mini,gpt-4o"
}
```

These values can be overridden via environment variables using the standard ASP.NET Core `__` separator (`Models__ChatDefault=gpt-4o`).

### 1b. `ModelRegistry` service

```csharp
public class ModelRegistry(IConfiguration config)
{
    private static readonly string[] DefaultAllowed = ["gpt-4o-mini"];

    public string ChatDefault    => config["Models:ChatDefault"]    ?? "gpt-4o-mini";
    public string AgentDefault   => config["Models:AgentDefault"]   ?? "gpt-4o-mini";
    public string SummaryDefault => config["Models:SummaryDefault"] ?? "gpt-4o-mini";

    public IReadOnlyList<string> AllowedModels =>
        (config["Models:Allowed"] ?? string.Join(',', DefaultAllowed))
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string Resolve(string? requested, string @default) =>
        string.IsNullOrWhiteSpace(requested) || !AllowedModels.Contains(requested)
            ? @default
            : requested;
}
```

Register as singleton in `Program.cs`: `builder.Services.AddSingleton<ModelRegistry>();`

### 1c. `ChatSessionsController` — use registry

Replace:
```csharp
var model = string.IsNullOrWhiteSpace(req.Model) ? "gpt-4o-mini" : req.Model.Trim();
```
With:
```csharp
var mode = NormalizeMode(req.Mode ?? session.Mode);
var model = modelRegistry.Resolve(
    req.Model,
    mode == "agent" ? modelRegistry.AgentDefault : modelRegistry.ChatDefault);
```

Inject `ModelRegistry modelRegistry` in the controller constructor.

---

## Step 2 — AI server model allowlist

**Files:** `ai-server/app/main.py`, `docker-compose.yml`, `docker-compose.dev.yml`

### 2a. Env var

Add to `ai-server` service in `docker-compose.yml`:
```yaml
ALLOWED_MODELS: ${ALLOWED_MODELS:-gpt-4o-mini,gpt-4o}
```

### 2b. Validation in `main.py`

At module level:
```python
_ALLOWED_MODELS: set[str] = set(
    os.environ.get("ALLOWED_MODELS", "gpt-4o-mini,gpt-4o").split(",")
)
```

In both `/chat/completions` and `/agent/run` handlers, after the model is read from the request, add:
```python
if req.model not in _ALLOWED_MODELS:
    raise HTTPException(status_code=422, detail=f"Model '{req.model}' is not allowed")
```

The session-state endpoint uses `"gpt-4o-mini"` directly (not from request) — add a module-level `_SUMMARY_MODEL = os.environ.get("SUMMARY_MODEL", "gpt-4o-mini")` constant and use it there instead.

---

## Step 3 — Complete LLM gateway abstraction

**Files:** `ai-server/app/gateway/base.py`, `ai-server/app/gateway/openai_provider.py`, `ai-server/app/main.py`

### 3a. Add `complete_structured` to the ABC

```python
@abstractmethod
async def complete_structured(
    self,
    messages: list[dict],
    schema: dict,
    model: str,
) -> dict:
    """Return a structured JSON response validated against schema."""
    ...
```

### 3b. Implement in `OpenAIGateway`

Move the inline `chat.completions.create` call from `session_state_update` in `main.py` into `OpenAIGateway.complete_structured`:

```python
async def complete_structured(self, messages, schema, model) -> dict:
    response = await self._client.chat.completions.create(
        model=model,
        messages=messages,
        response_format={"type": "json_schema", "json_schema": schema},
    )
    content = response.choices[0].message.content or "{}"
    return json.loads(content)
```

Wrap provider-specific exceptions (`openai.APIError`, `openai.APITimeoutError`) and raise a local `GatewayError(message, retryable: bool)` instead. Add `GatewayError` to `base.py`.

### 3c. Update `main.py` — `session_state_update`

Replace the direct `_gateway._client.chat.completions.create(...)` call with:
```python
state = await _gateway.complete_structured(
    messages=[...],
    schema=schema,
    model=_SUMMARY_MODEL,
)
```

---

## Step 4 — Embedding gateway in RAG server

**Files:** `rag-server/app/gateway/` (new directory), `rag-server/app/embedder.py`, `rag-server/app/chunker.py`, `rag-server/app/vector_store.py`, `rag-server/app/main.py`

### 4a. Create `rag-server/app/gateway/base.py`

```python
from abc import ABC, abstractmethod

class EmbeddingGateway(ABC):
    dimensions: int
    model: str

    @abstractmethod
    async def embed(self, text: str) -> list[float]: ...

    @abstractmethod
    async def embed_batch(self, texts: list[str]) -> list[list[float]]: ...
```

### 4b. Create `rag-server/app/gateway/openai_embedding.py`

Move the existing `embedder.py` logic here, reading model/dimensions from env vars:

```python
_MODEL = os.environ.get("EMBEDDING_MODEL", "text-embedding-3-small")
_DIMENSIONS = int(os.environ.get("EMBEDDING_DIMENSIONS", "1536"))
```

Keep `embedder.py` as a thin shim that instantiates `OpenAIEmbeddingGateway` and re-exports `embed`, `embed_batch`, and `DIMENSIONS` for backwards compatibility.

---

## Step 5 — RAG config module

**Files:** `rag-server/app/rag_config.py` (new), `rag-server/app/chunker.py`, `rag-server/app/vector_store.py`, `rag-server/app/main.py`

### 5a. `rag_config.py`

```python
import os
from dataclasses import dataclass

@dataclass(frozen=True)
class RagConfig:
    chunk_size: int
    chunk_overlap: int
    embedding_model: str
    embedding_dimensions: int
    search_mode: str      # "vector" | "bm25" | "hybrid"
    top_k: int
    hybrid_alpha: float

def current_config() -> RagConfig:
    return RagConfig(
        chunk_size=int(os.environ.get("RAG_CHUNK_SIZE", "800")),
        chunk_overlap=int(os.environ.get("RAG_CHUNK_OVERLAP", "100")),
        embedding_model=os.environ.get("EMBEDDING_MODEL", "text-embedding-3-small"),
        embedding_dimensions=int(os.environ.get("EMBEDDING_DIMENSIONS", "1536")),
        search_mode=os.environ.get("RAG_SEARCH_MODE", "hybrid"),
        top_k=int(os.environ.get("RAG_TOP_K", "5")),
        hybrid_alpha=float(os.environ.get("RAG_HYBRID_ALPHA", "0.5")),
    )
```

Add startup validation in `main.py` (after `configure_json_logging()`):
```python
_cfg = current_config()
if _cfg.chunk_overlap >= _cfg.chunk_size:
    raise SystemExit("RAG_CHUNK_OVERLAP must be less than RAG_CHUNK_SIZE")
if not (0.0 <= _cfg.hybrid_alpha <= 1.0):
    raise SystemExit("RAG_HYBRID_ALPHA must be between 0.0 and 1.0")
```

### 5b. Thread `RagConfig` through callers

- `chunker.chunk_text(text)` → `chunker.chunk_text(text, chunk_size, chunk_overlap)` — callers pass from config (remove Python default args to make config explicit at call site)
- `vector_store.search_hybrid(db, ..., top_k, alpha)` — already parameterized; callers use config values
- `main.py` `/ingest` route: call `current_config()` and pass values to `chunk_text` and `store_chunks`
- `main.py` `/search/*` routes: use `_cfg.top_k` and `_cfg.hybrid_alpha` as defaults when query params are absent

---

## Step 6 — RAG config snapshots

### 6a. Snapshot on ingestion

**File:** `rag-server/app/main.py` — `/ingest` route

When inserting/updating the ArangoDB `documents` record, include:
```python
"ingestion_config": {
    "chunk_size": cfg.chunk_size,
    "chunk_overlap": cfg.chunk_overlap,
    "embedding_model": cfg.embedding_model,
    "embedding_dimensions": cfg.embedding_dimensions,
    "chunk_count": len(chunks),
}
```

### 6b. Snapshot on chat context retrieval

**File:** `ai-server/app/rag_client.py` — `search()` return

Have `search()` return a richer object that includes the config metadata from the RAG server response. The RAG server `/search/hybrid` response should include a `config` field:
```json
{
  "results": [...],
  "config": { "top_k": 5, "alpha": 0.5, "mode": "hybrid", "embedding_model": "..." }
}
```

**File:** `ai-server/app/main.py` — `/chat/completions`

Include this config in the log sent to `be_client.log_request` as `request_json`:
```python
request_json={"query": query, "notebook_id": req.notebook_id, "rag_config": rag_meta}
```

This persists the retrieval config in `request_logs` without requiring a new DB column.

---

## Step 7 — Re-ingest endpoint

**Files:** `be-server/BeServer/Content/SourcesController.cs`, `be-server/BeServer/Data/Entities/IngestionJob.cs`

### 7a. New BE endpoint

```
POST /api/notebooks/{notebookId}/sources/{sourceId}/reingest
```

Handler:
1. Ownership check via `OwnershipService.SourceExistsAsync`
2. Load the source — confirm it has a `FilePath` and its current status is `ingested` or `failed`
3. Cancel any `queued`/`running`/`retrying` ingestion jobs for this source
4. Create a new `IngestionJob` with `Status = IngestionJobStatuses.Queued`
5. Set `source.Status = IngestionJobStatuses.Queued`
6. Return `202 Accepted` with the new job id

No new SQL migration needed — re-ingest reuses existing `ingestion_jobs` table and `IngestionJobWorker`.

### 7b. Frontend (optional for this phase)

Add a "Re-ingest" button in `NotebookSourcesPanel` next to sources whose status is `ingested` or `failed`. Calls the new endpoint and triggers a reload.

---

## Step 8 — Tests and verification

### BE tests

Add to `NotebookAndSearchControllerTests.cs` or a new `ModelRegistryTests.cs`:
- `Resolve` returns default when model is empty
- `Resolve` returns default when model is not in allowlist
- `Resolve` returns requested model when it is in allowlist

Add to `IngestionJobTests.cs`:
- Re-ingest creates a new queued job and cancels the active one

### Python tests

Add to `rag-server/tests/`:
- `test_rag_config.py`: `current_config()` returns correct defaults; startup validation rejects bad values
- `test_chunker.py`: update existing tests to pass explicit `chunk_size`/`chunk_overlap`

### Verification commands

```bash
# BE build + test
docker run --rm -v /path/to/be-server:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet test BeServer.Tests/BeServer.Tests.csproj --logger "console;verbosity=minimal"

# Python compile check
python3 -m compileall rag-server/app ai-server/app

# Ruff lint
docker run --rm -v /path/to/repo:/repo -w /repo ghcr.io/astral-sh/ruff:latest \
  check rag-server ai-server

# Frontend build
cd frontend && npm run build
```

---

## Deliverable checklist

- [ ] `ModelRegistry` service in BE with `appsettings.json` defaults
- [ ] `ChatSessionsController` resolves model through registry; arbitrary strings rejected
- [ ] AI server validates `model` against `ALLOWED_MODELS` env var
- [ ] `_SUMMARY_MODEL` env var replaces hardcoded `"gpt-4o-mini"` in session-state handler
- [ ] `LLMGateway.complete_structured()` method; `OpenAIGateway` implements it; `GatewayError` raised for provider failures
- [ ] `EmbeddingGateway` ABC + `OpenAIEmbeddingGateway` in RAG server; `embedder.py` re-exports for compatibility
- [ ] `rag_config.py` with `RagConfig` dataclass and startup validation
- [ ] Chunker and search callers use config values; no remaining magic-number defaults
- [ ] ArangoDB `documents` record stores `ingestion_config` snapshot
- [ ] RAG server search responses include `config` field; logged in BE request logs
- [ ] `POST .../sources/{id}/reingest` endpoint with ownership check and job creation
- [ ] Tests cover model resolution, re-ingest, and RAG config validation
