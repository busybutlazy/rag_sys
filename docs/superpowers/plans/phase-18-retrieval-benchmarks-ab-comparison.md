# Phase 18 — Retrieval Benchmarks and A/B Comparison

**Branch:** `phase-18-retrieval-benchmarks-ab-comparison`  
**Goal:** Let the owner compare retrieval versions on the same notebook and query set, with reproducible runs and evidence-rich diffs.

---

## Why this phase exists now

Phase 16 gave notebooks immutable retrieval versions.  
Phase 17 made it possible to rebuild payloads for a target version.

Phase 18 should turn those versioned corpora into an actual comparison loop:

```text
retrieval version A ─┐
                     ├─ same notebook + same query set → comparable evidence
retrieval version B ─┘
```

Without this phase, Lab can create and rebuild versions, but cannot yet answer the question that justifies their existence:

> Did the new retrieval design become better on my corpus, or merely different?

---

## Critical boundary discovered while planning

Before adding benchmark UX, Phase 18 must finish the version-isolation seam that Phase 17 intentionally deferred:

1. **RAG search is not yet retrieval-version scoped.**
   - `vector_store.search_vector/search_bm25/search_hybrid` do not accept `retrieval_version_id`.
   - Regular product search currently asks for the notebook's active version config in BE, but does **not** send the active version id to RAG.
   - Once multiple versions are retained, ordinary search could blend chunks from different retrieval versions.

2. **Reindex cleanup currently removes prior chunks immediately after success.**
   - `ReindexJobWorker` deletes the previous version's chunks as soon as target ingestion succeeds.
   - That preserves safety during rebuild, but removes the exact old payload Phase 18 needs for A/B comparison.

### Planning decision

Phase 18 should first establish a clean lifecycle:

- **Retain old and target payloads through comparison.**
- **Product search always filters to the active retrieval version.**
- **Lab search can explicitly request any notebook-local retrieval version.**
- **Chunk cleanup happens only after promotion / explicit pruning, not immediately after rebuild success.**

This is not scope creep; it is the load-bearing correction that makes every later benchmark truthful.

---

## Design principles

- **Version-explicit everywhere.** Every benchmarked retrieval call names the retrieval version it used.
- **Same inputs, paired outputs.** A/B comparison means same notebook, same query, same `top_k`, same search mode unless the run intentionally varies modes.
- **Canonical control plane in MySQL.** Datasets, runs, and result summaries live in MySQL so lineage and authorization stay with the product boundary.
- **Compact immutable result snapshots.** Store enough result identity and metric data to reproduce comparisons without depending on future live search state.
- **Lab-only mutations.** New benchmark and dataset APIs stay behind `DevAdminOnly`; normal users remain on the clean product path.
- **Build the reviewable loop before the ornate one.** A useful side-by-side diff beats a premature analytics suite.

---

## Proposed data model

### `EvaluationDataset`

```
Id            CHAR(36) PK
NotebookId    CHAR(36) FK → Notebooks
UserId        CHAR(36) FK → Users
Name          VARCHAR(160)
Description   TEXT?
CreatedAt     DATETIME
UpdatedAt     DATETIME
```

### `EvaluationQuery`

```
Id                    CHAR(36) PK
DatasetId             CHAR(36) FK → EvaluationDatasets
QueryText             VARCHAR(500)
ExpectedAnswerNotes   TEXT?
GoldSourceNotes       TEXT?
SortOrder             INT
CreatedAt             DATETIME
UpdatedAt             DATETIME
```

### `EvaluationRun`

```
Id                    CHAR(36) PK
NotebookId            CHAR(36) FK → Notebooks
DatasetId             CHAR(36)? nullable
UserId                CHAR(36) FK → Users
RetrievalVersionAId   CHAR(36) FK → NotebookRetrievalVersions
RetrievalVersionBId   CHAR(36) FK → NotebookRetrievalVersions
SearchModesJson       JSON / long text
TopK                  INT
HybridAlpha           DOUBLE
Status                VARCHAR(16): queued|running|failed|succeeded
StartedAt             DATETIME?
CompletedAt           DATETIME?
CreatedAt             DATETIME
```

### `EvaluationResult`

One row per `(run, query, version, mode)`.

```
Id                  CHAR(36) PK
RunId               CHAR(36) FK → EvaluationRuns
QueryId             CHAR(36)? nullable
QueryTextSnapshot   VARCHAR(500)
RetrievalVersionId  CHAR(36)
Mode                VARCHAR(16)
LatencyMs           INT
ResultCount         INT
ResultsJson         JSON / long text
CreatedAt           DATETIME
```

`ResultsJson` should store an ordered immutable snapshot such as:

```json
[
  {
    "rank": 1,
    "source_id": "...",
    "chunk_index": 3,
    "text_preview": "..."
  }
]
```

### Computed comparison payload

Do **not** over-normalize metrics in the first pass. Compute these when serving run detail:

- `overlap_at_k`
- `source_overlap`
- `rank_deltas`
- `result_count_delta`
- latency delta

Persisting raw ordered result snapshots gives us room to revise formulas later without corrupting historical runs.

---

## Task list

## Build checklist with acceptance gates

### Gate A — Retrieval versions are truly isolated

- [x] RAG search accepts `retrieval_version_id` for vector / BM25 / hybrid / benchmark queries.
- [x] Arango search view indexes `retrieval_version_id` for exact filtering.
- [x] Normal product search always passes the notebook's active retrieval version id.
- [x] Successful reindex keeps prior chunks available for later comparison.

**Acceptance**

- [x] With two indexed versions in one notebook, searching version A never returns version B chunks.
- [x] Changing the notebook's active version changes normal product search results without mixing versions.
- [x] A successful rebuild leaves both old and new payloads present until an explicit cleanup path removes one.

### Gate B — Reusable evaluation inputs exist

- [x] MySQL stores datasets, queries, runs, and result snapshots.
- [x] Lab-only dataset CRUD exists.
- [x] Query ordering and optional expectation notes are preserved.

**Acceptance**

- [x] A dev admin can create a notebook-scoped dataset, add ordered queries, edit them, and retrieve them later.
- [x] Another user cannot read or mutate that dataset.
- [x] Historical runs can survive later edits to the source dataset because query text is snapshotted.

### Gate C — A/B comparison is operational

- [x] Ad hoc compare endpoint runs the same query against two notebook-local retrieval versions.
- [x] Comparison service returns latency, overlap@k, source overlap, rank deltas, and result-count delta.
- [x] Dataset runner persists result snapshots for every `(query, version, mode)` pair.

**Acceptance**

- [x] The same query against two versions returns two ordered result lists plus paired metrics.
- [x] A dataset run can be reopened later with the same stored outputs and aggregate metrics.
- [x] Cross-notebook version comparison is rejected.

### Gate D — The Lab loop is usable

- [x] `/lab/retrieval-bench` page exists.
- [x] The page supports ad hoc compare, dataset runs, side-by-side diffs, and recent history.
- [x] Lab navigation exposes the page only through the existing restricted Lab shell.

**Acceptance**

- [x] The owner can select two versions, run a query, and visually inspect changed chunks without leaving the page.
- [x] The owner can launch a dataset run and reopen an older run from history.
- [x] The page shows version context clearly enough that UUIDs are not the only semantic cue.

### Gate E — Phase completion hygiene

- [ ] Focused BE and RAG tests cover version scoping, dataset/run flows, and comparison metrics.
- [ ] Review document written under `docs/reviews/phase-18-review.md`.
- [ ] ROADMAP3 phase status updated after merge-ready implementation.

**Acceptance**

- [ ] Relevant automated tests pass.
- [ ] The review finds no open correctness issue that would make A/B evidence untrustworthy.
- [ ] ROADMAP3 accurately reflects what shipped and what remains deferred.

### Task 1 — Finish retrieval-version isolation seam

**RAG server**

- [x] Extend `vector_store.search_vector`, `search_bm25`, `search_hybrid`, and `get_source_content` with optional `retrieval_version_id`.
- [x] Add `retrieval_version_id` exact-match indexing support to `chunks_view`.
- [x] Extend `/search/vector`, `/search/bm25`, `/search/hybrid`, and `/search/benchmark` query params with optional `retrieval_version_id`.
- Ensure all returned chunks include enough identity for comparison:
  - `source_id`
  - `chunk_index`
  - optionally `retrieval_version_id`

**BE server**

- [x] Extend `RagClient.SearchAsync` / `BenchmarkAsync` to accept optional retrieval version id.
- [x] Change ordinary `SearchController` calls to pass the notebook's **active** retrieval version id to RAG.
- [x] Keep ordinary product behavior version-pinned even if multiple payloads coexist.

**Reindex lifecycle**

- [x] Change `ReindexJobWorker` so successful rebuilds no longer delete the previous version's chunks immediately.
- [x] Move stale-chunk deletion to:
  - an explicit Lab prune action for inactive retrieval payloads.
- Record enough lineage so promotion knows what previous version is being retired.

**Tests**

- [x] Search scoped to version A returns only A chunks.
- [x] Search scoped to version B returns only B chunks.
- [x] Product search uses active version id and does not blend versions.
- [x] Reindex success leaves prior chunks available until promotion / explicit cleanup.

---

### Task 2 — Add evaluation dataset schema and CRUD

**BE**

- Add entities + migration:
  - `EvaluationDataset`
  - `EvaluationQuery`
  - `EvaluationRun`
  - `EvaluationResult`
- Add `DbSet<>` registrations and indexes:
  - `(UserId, NotebookId)` on datasets
  - `(DatasetId, SortOrder)` on queries
  - `(UserId, NotebookId, CreatedAt)` on runs
  - `(RunId)` on results
- Add Lab-only dataset controller:
  - `GET /api/lab/notebooks/{notebookId}/evaluation-datasets`
  - `POST /api/lab/notebooks/{notebookId}/evaluation-datasets`
  - `GET /api/lab/evaluation-datasets/{datasetId}`
  - `POST /api/lab/evaluation-datasets/{datasetId}/queries`
  - `PUT /api/lab/evaluation-queries/{queryId}`
  - `DELETE /api/lab/evaluation-queries/{queryId}`

**Validation**

- Dataset/query must belong to current user and notebook.
- Query text non-empty, max 500 chars.
- Stable `SortOrder` so a run preserves curator intent.

---

### Task 3 — Add ad hoc compare endpoint

This is the fastest way to make the Lab useful before full dataset runs exist.

**Endpoint**

`POST /api/lab/notebooks/{notebookId}/retrieval-bench/compare`

Body:

```json
{
  "query": "...",
  "retrieval_version_a_id": "...",
  "retrieval_version_b_id": "...",
  "modes": ["hybrid"],
  "top_k": 5,
  "alpha": 0.5
}
```

Response:

- results for A and B
- latency per side
- overlap metrics
- source overlap
- rank deltas
- result-count delta

**Rules**

- Both versions must belong to the same notebook.
- Default mode may come from each retrieval version, but for a fair first release the UI should encourage comparing the same mode on both sides.
- Return chunk previews directly so the UI can render a side-by-side diff without another fetch.

---

### Task 4 — Add dataset run orchestration

**Endpoint**

`POST /api/lab/notebooks/{notebookId}/retrieval-bench/runs`

Body:

```json
{
  "dataset_id": "...",
  "retrieval_version_a_id": "...",
  "retrieval_version_b_id": "...",
  "modes": ["vector", "bm25", "hybrid"],
  "top_k": 5,
  "alpha": 0.5
}
```

**Execution model for first release**

- Run synchronously inside the request if dataset size is modest and capped.
- Suggested initial caps:
  - max 50 queries per dataset run
  - max 3 modes
- If the request envelope becomes too slow in practice, Phase 18 patch or Phase 19 can lift this into a durable background worker.

**Persistence**

- Create `EvaluationRun`.
- Snapshot every query text into `EvaluationResult`.
- Persist ordered results per version + mode.
- Mark run `Succeeded` / `Failed`.

**Read APIs**

- `GET /api/lab/notebooks/{notebookId}/retrieval-bench/runs`
- `GET /api/lab/retrieval-bench/runs/{runId}`

---

### Task 5 — Metrics engine

Implement a small comparison service in BE, not the frontend.

For each paired `(query, mode)`:

- `overlap_at_k`
  - exact chunk identity overlap using `(source_id, chunk_index)`
- `source_overlap`
  - overlap on distinct `source_id`
- `rank_deltas`
  - positive/negative movement for shared chunks
- `result_count_delta`
- `latency_delta_ms`

For a whole run:

- average overlap@k
- average source overlap
- average latency delta
- count of wins / ties / losses by result coverage heuristic

Keep formulas simple and transparent. Phase 19 will add relevance judgment; Phase 18 should avoid pretending overlap alone means quality.

---

### Task 6 — `/lab/retrieval-bench` frontend

New page: `frontend/src/pages/LabRetrievalBenchPage.tsx`

Suggested layout:

```text
[ notebook selector ] [ version A ] [ version B ]

┌ Ad hoc compare ────────────────────────────────┐
│ query box · mode · top_k · alpha · Compare      │
└─────────────────────────────────────────────────┘

┌ Side-by-side diff ──────────────────────────────┐
│ Version A                    Version B          │
│ ranked chunks                 ranked chunks     │
│ overlap / rank movement / latency summary        │
└─────────────────────────────────────────────────┘

┌ Dataset runner ─────────────────────────────────┐
│ dataset picker · run button · recent runs        │
└─────────────────────────────────────────────────┘
```

UI requirements:

- ad hoc query compare
- dataset CRUD light enough to build reusable query sets
- dataset runner
- result history
- side-by-side ranked chunk diff
- make version ids human-readable via notes / ancestry where possible, not raw UUID alone

Wire route:

- `/lab/retrieval-bench`

Add nav entry under the Lab section in `AppShell`.

---

### Task 7 — Tests

**BE**

- dataset CRUD ownership and `DevAdminOnly`
- compare rejects cross-notebook versions
- compare returns paired metrics
- dataset run persists one result row per `(query, version, mode)`
- run detail reconstructs aggregate metrics correctly
- ordinary product search stays pinned to active retrieval version

**RAG**

- vector / BM25 / hybrid filters by retrieval version id
- benchmark endpoint respects retrieval version id
- `chunks_view` includes `retrieval_version_id` exact-match support

**Frontend**

- page loads datasets / versions / history
- ad hoc compare renders both columns
- result history selection updates diff view

---

## File touch list

| Area | Expected files |
|------|----------------|
| BE entities | `EvaluationDataset.cs`, `EvaluationQuery.cs`, `EvaluationRun.cs`, `EvaluationResult.cs` |
| BE data | `AppDbContext.cs`, new migration, model snapshot |
| BE services | `RagClient.cs`, new `RetrievalComparisonService.cs` |
| BE controllers | new Lab dataset controller, new Lab retrieval bench controller, `SearchController.cs` |
| BE lifecycle | `ReindexJobWorker.cs`, possibly `LabReindexController.cs` |
| RAG | `vector_store.py`, `main.py`, maybe `models.py` |
| Frontend | `LabRetrievalBenchPage.tsx`, `App.tsx`, `AppShell.tsx`, `lib/api.ts` if helpers are added |
| Tests | BE retrieval bench tests, RAG vector-store/search tests, frontend page tests if present |

---

## Suggested implementation order

1. Version-scoped search + active-version product pinning
2. Reindex retention correction
3. Evaluation schema
4. Ad hoc compare API
5. Dataset CRUD
6. Dataset run persistence
7. Retrieval bench UI
8. Review + patch pass

This order keeps the corpus truthful before building interfaces over it.

---

## Open questions to resolve before implementation

1. **Cleanup timing**
   - Recommended for Phase 18: keep both versions after successful rebuild; delete the old payload only when the owner explicitly promotes and no longer needs comparison, or defer deletion entirely to a future prune flow.

2. **Run execution**
   - Recommended for Phase 18: synchronous with strict caps; promote to background jobs only once dataset sizes or latency justify it.

3. **Result payload location**
   - Recommended for Phase 18: MySQL owns run/result snapshots; keep Arango as the live retrieval plane rather than splitting benchmark truth across stores too early.

4. **Legacy experiment feature**
   - Recommended for Phase 18: leave the old notebook experiment panel intact for now, but treat `/lab/retrieval-bench` as the future-facing versioned workflow rather than extending the legacy config-only experiment model.

---

## Deliverable

The owner can:

1. Keep two retrieval versions indexed at once.
2. Run the same query against both.
3. See exactly which chunks changed, how ranks moved, and how latency shifted.
4. Save reusable notebook-local query sets.
5. Re-run them later and inspect historical evidence instead of relying on memory.

At the end of Phase 18, Lab stops being a version editor and becomes a real retrieval workbench.
