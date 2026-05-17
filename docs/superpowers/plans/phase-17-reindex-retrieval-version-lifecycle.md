# Phase 17 — Re-indexing and Retrieval Version Lifecycle

**Branch:** `phase-17-reindex-retrieval-version-lifecycle`  
**Goal:** Safely rebuild source and notebook indexes when retrieval configuration changes, without corrupting the live knowledge base.

---

## Design Principles

- **Preserve old chunks until promotion.** During rebuild, new chunks are written with `retrieval_version_id = target`. Old chunks remain until explicit promotion by the owner.
- **No chunk deletion before success.** The worker only deletes the old version's chunks after the new version's ingestion succeeds.
- **Source-level and notebook-level jobs.** A notebook-level job fans out into one reindex task per source under a single parent job.
- **Re-ingest only for now.** Re-embed-only (same chunking, different embedding) is deferred; Phase 17 always re-processes the full source file.
- **BE is the control plane.** RAG server gains version-scoped chunk operations. BE orchestrates everything.

---

## Current State (relevant to this phase)

- `IngestionJob` tracks `ingest` and `delete_cleanup` types. Worker handles both.
- Chunks in ArangoDB already carry `retrieval_version_id`, `user_id`, `notebook_id`, `source_id`.
- `delete_chunks(db, source_id, user_id)` deletes ALL chunks for a source regardless of version — needs extension.
- `Source.ActiveRetrievalVersionId` / `Source.LastIndexedRetrievalVersionId` already exist.
- `Notebook.ActiveRetrievalVersionId` already exists.
- `LabRetrievalVersionsController.Activate` sets `ActiveRetrievalVersionId` on notebook + all sources but does NOT trigger reindex.

---

## Task List

### Task 1 — ReindexJob entity + migration (BE)

New entity `BeServer/Data/Entities/ReindexJob.cs`:

```
Id                         CHAR(36) PK
NotebookId                 CHAR(36) FK → Notebooks
UserId                     CHAR(36) FK → Users
SourceId                   CHAR(36)? nullable (null = notebook-level)
Scope                      VARCHAR(16): "source" | "notebook"
TargetRetrievalVersionId   CHAR(36) FK → NotebookRetrievalVersions
PreviousRetrievalVersionId CHAR(36)?
Status                     VARCHAR(16): queued|running|retrying|failed|succeeded|cancelled
SourcesTotal               INT default 0  (notebook-level: total sources)
SourcesSucceeded           INT default 0
SourcesFailed              INT default 0
AttemptCount               INT default 0
MaxAttempts                INT default 1  (notebook-level jobs don't retry globally)
LastError                  TEXT?
AvailableAt                DATETIME
StartedAt                  DATETIME?
CompletedAt                DATETIME?
CreatedAt                  DATETIME
UpdatedAt                  DATETIME
```

Add static class `ReindexJobScopes { Source, Notebook }` and `ReindexJobStatuses` (same values as IngestionJobStatuses).

Migration: `20260517020000_Phase17ReindexJobs.cs` — creates `reindex_jobs` table.

Add `DbSet<ReindexJob>` to `AppDbContext`.

---

### Task 2 — Version-scoped RAG operations (rag-server)

**`vector_store.py`:**

Add `retrieval_version_id` parameter to `delete_chunks`:
```python
def delete_chunks(db, source_id: str, user_id: str, retrieval_version_id: str | None = None) -> None:
    # if retrieval_version_id is None: delete all chunks for source (existing behavior)
    # if set: only delete chunks with that retrieval_version_id
```

Add `delete_version_chunks(db, notebook_id, user_id, retrieval_version_id)`:
```python
# delete all chunks for a notebook with a specific retrieval_version_id
```

Extend `search_vector`, `search_bm25`, `search_hybrid` to accept optional `retrieval_version_id`:
```python
# if set: add FILTER doc.retrieval_version_id == @rv_id to AQL
# if None: existing behavior (search all versions)
```

**`main.py` (rag-server):**

Add endpoint:
```
DELETE /sources/{source_id}/chunks
  query params: user_id (required), retrieval_version_id (optional)
  → calls delete_chunks with version scope
```

Add endpoint:
```
DELETE /notebooks/{notebook_id}/chunks
  query params: user_id (required), retrieval_version_id (required)
  → calls delete_version_chunks
```

No changes needed to `/ingest` — it already stores `retrieval_version_id` from the retrieval config.

**`models.py`:**

No new models needed. `IngestRequest.retrieval.retrieval_version_id` already exists.

---

### Task 3 — ReindexJobWorker (BE)

New `BeServer/Services/ReindexJobWorker.cs` (IHostedService / BackgroundService):

```
Poll interval: 5s
Pick one queued/retrying job ordered by AvailableAt, CreatedAt
```

**Source-level job flow:**
1. Load source; cancel job if source gone.
2. Load target retrieval version.
3. Mark job running, source.Status = SourceStatuses.Running.
4. Call `rag.IngestAsync(source, ..., targetRetrieval)` — writes new chunks tagged with target version id.
5. On success:
   - Call `rag.DeleteSourceVersionChunksAsync(sourceId, userId, previousVersionId)` to remove old chunks.
   - Set `source.LastIndexedRetrievalVersionId = targetVersionId`.
   - Job status = Succeeded.
6. On failure: retry logic same as IngestionJobWorker (backoff, max attempts = 3).

**Notebook-level job flow:**
1. Load all sources for the notebook.
2. Set `SourcesTotal = sources.Count`, mark job running.
3. For each source, run ingest + old-chunk delete sequentially:
   - Increment `SourcesSucceeded` or `SourcesFailed` per outcome.
4. After all sources:
   - If any failed: job.Status = Failed, set LastError = summary.
   - Else: job.Status = Succeeded.
5. Notebook-level jobs do NOT retry globally (MaxAttempts = 1). Source-level failures are counted.

---

### Task 4 — RagClient extensions (BE)

Add to `RagClient.cs`:

```csharp
Task DeleteSourceVersionChunksAsync(string sourceId, string userId, string? retrievalVersionId)
// DELETE /sources/{sourceId}/chunks?user_id=&retrieval_version_id=

Task DeleteNotebookVersionChunksAsync(string notebookId, string userId, string retrievalVersionId)
// DELETE /notebooks/{notebookId}/chunks?user_id=&retrieval_version_id=
```

---

### Task 5 — LabReindexController (BE)

New `BeServer/Content/LabReindexController.cs`:

```
[Authorize(Policy = "DevAdminOnly")]
[Route("api/lab")]

POST   /api/lab/notebooks/{notebookId}/reindex
       body: { target_version_id: string, scope: "notebook" }
       → validate version belongs to notebook, create ReindexJob(Scope=Notebook)

POST   /api/lab/notebooks/{notebookId}/sources/{sourceId}/reindex
       body: { target_version_id: string }
       → validate version + source ownership, create ReindexJob(Scope=Source)

GET    /api/lab/notebooks/{notebookId}/reindex-jobs
       → list all reindex jobs for notebook, ordered by CreatedAt desc

GET    /api/lab/reindex-jobs/{jobId}
       → single job detail

POST   /api/lab/reindex-jobs/{jobId}/promote
       → if job.Status == Succeeded:
           set notebook.ActiveRetrievalVersionId = job.TargetRetrievalVersionId
           set all sources.ActiveRetrievalVersionId = job.TargetRetrievalVersionId
           (old chunks were already deleted by worker on success)
           return updated notebook
       → else: 400

POST   /api/lab/reindex-jobs/{jobId}/cancel
       → if job.Status in (Queued, Retrying): set Cancelled
       → else: 400
```

Response shape for job list/detail:
```json
{
  "id": "...",
  "scope": "notebook|source",
  "source_id": null,
  "target_retrieval_version_id": "...",
  "previous_retrieval_version_id": "...",
  "status": "queued|running|...",
  "sources_total": 3,
  "sources_succeeded": 2,
  "sources_failed": 0,
  "last_error": null,
  "started_at": "...",
  "completed_at": "...",
  "created_at": "..."
}
```

---

### Task 6 — Frontend `/lab/reindex` page

New `frontend/src/pages/LabReindexPage.tsx`.

Sections:
1. **Reindex Jobs list** — table showing all jobs for a selected notebook, with columns: scope, target version, status, progress (succeeded/total), created at, actions.
2. **Queue job panel** — select notebook → select target retrieval version → "Re-index Notebook" button.
3. **Job detail** — expand row to see last error / individual source counts.
4. **Actions per job:**
   - Running/queued: Cancel button.
   - Succeeded: Promote button (updates active version).

Wire into `App.tsx` at `/lab/reindex`. Add nav link in `AppShell.tsx` under Lab section.

All API calls via `lib/api.ts` helpers.

---

### Task 7 — Tests

**BE tests (`ReindexJobTests.cs`):**
- Queue source-level reindex job via controller → job created in DB.
- Queue notebook-level reindex job → job created.
- Promote succeeded job → notebook ActiveRetrievalVersionId updated.
- Promote non-succeeded job → 400.
- Cancel queued job → Cancelled.
- Cancel running job → 400.
- Dev-admin gate: non-admin request → 403.

**RAG tests (`test_vector_store.py`):**
- `delete_chunks` with `retrieval_version_id` only deletes matching version's chunks.
- `delete_chunks` without `retrieval_version_id` deletes all chunks for source (regression).
- `delete_version_chunks` removes only target version from notebook.

---

## File Touch List

| File | Change |
|------|--------|
| `be-server/BeServer/Data/Entities/ReindexJob.cs` | NEW |
| `be-server/BeServer/Data/AppDbContext.cs` | Add DbSet + config |
| `be-server/BeServer/Migrations/20260517020000_Phase17ReindexJobs.cs` | NEW |
| `be-server/BeServer/Migrations/AppDbContextModelSnapshot.cs` | Update |
| `be-server/BeServer/Services/ReindexJobWorker.cs` | NEW |
| `be-server/BeServer/Services/RagClient.cs` | Add 2 delete methods |
| `be-server/BeServer/Content/LabReindexController.cs` | NEW |
| `be-server/BeServer/Program.cs` | Register ReindexJobWorker |
| `be-server/BeServer.Tests/ReindexJobTests.cs` | NEW |
| `rag-server/app/vector_store.py` | Extend delete + search |
| `rag-server/app/main.py` | Add 2 DELETE endpoints |
| `rag-server/tests/test_vector_store.py` | Extend |
| `frontend/src/pages/LabReindexPage.tsx` | NEW |
| `frontend/src/App.tsx` | Add route |
| `frontend/src/components/AppShell.tsx` | Add nav link |
| `frontend/src/lib/api.ts` | Add reindex API helpers |

---

## Out of Scope for Phase 17

- Re-embed-only (same chunks, new embedding model) — Phase 17 always does full re-ingest.
- Per-source retry inside notebook-level jobs (failures are counted but not individually retried).
- Automatic promotion — always requires explicit user action.
- Deleting old chunks for notebook-level rollback — if user cancels after partial success, old and new chunks coexist; cleanup is done by next successful promote or explicit delete.
