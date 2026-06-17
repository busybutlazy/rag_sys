# Phase 19 — GraphRAG Foundations

**Branch:** `phase-19-graphrag-foundations`
**Goal:** Add an opt-in entity/fact graph layer to the retrieval plane and make it directly A/B-comparable against vector/BM25/hybrid using the comparison engine Phase 18 already built.

This phase replaces the vague placeholder previously called "Phase 21 — GraphRAG Readiness" in `ROADMAP3.md`. It is promoted ahead of "Human Relevance Labels" and "Agent Packages" per `docs/POSITIONING.md`: the project's near-term story is Graph RAG design and evaluation, not breadth.

---

## Why this phase exists now

Phase 16–18 built immutable retrieval versions, safe reindexing, and a real A/B comparison loop (`/lab/retrieval-bench`) — but every version compared so far is a variant of the same idea: chunk, embed, vector/BM25/RRF. There is no structural alternative on the board yet. `docs/POSITIONING.md` reframes the project around demonstrating Graph RAG specifically, and the reference project at `/home/jett/Documents/graph` shows a working (if rougher) version of the missing piece: an LLM-confined extraction stage that turns chunks into entities and facts, queried via a graph branch fused with vector search.

Phase 19 ports the *shape* of that design into `rag_sys`'s existing seams — not the code, and not its multi-tenant-per-DB or service-JWT machinery, which don't fit here. See `docs/POSITIONING.md` § "Reference project" for what is and isn't being reused.

---

## Critical architecture decision: where does extraction LLM usage live?

This is the load-bearing call for the whole phase, equivalent to Phase 18's version-isolation seam.

`CLAUDE.md` currently states the RAG server only talks to OpenAI for embeddings. Entity/fact extraction needs an LLM *completion* call (structured output: mentions + facts per chunk). Two options:

1. Add a completion call directly inside `rag-server`. Fast, but breaks the existing service boundary and duplicates LLM-provider concerns that `ai-server/gateway/` already owns.
2. **Run extraction in `ai-server`, behind a new internal endpoint, using the existing `LLMGateway` abstraction. `rag-server` only ever receives already-extracted structured data and writes it deterministically to Arango.**

**Decision: option 2.** This mirrors `graph`'s own separation (LLM confined to extractor stages; everything else deterministic) while keeping `rag_sys`'s existing rule that LLM provider selection lives in `ai-server`. Concretely:

```
BE (IngestionJobWorker, after normal ingest succeeds)
  │  if NotebookRetrievalVersion.EnableGraph
  ▼
AI server  POST /ai/extract/graph   { chunks: [{chunk_index, text}] }
  │  LLMGateway.* — structured extraction, confined here only
  ▼  returns { chunk_index, mentions[], facts[] }[]
BE
  ▼
RAG server  POST /graph/ingest   { source_id, notebook_id, user_id,
                                    retrieval_version_id, chunk_extractions[] }
  │  deterministic: resolve (alias merge) → assemble (entities/facts/edges) → write
  ▼
ArangoDB: entities, facts, + edges
```

No controller calls RAG or AI directly for this — BE's existing `IngestionJobWorker` orchestrates it as one more step, the same pattern already used for reindex (`CLAUDE.md`: "Never call RAG directly from a controller").

---

## Design principles

- **Off by default, zero blast radius.** `EnableGraph` is a field on `NotebookRetrievalVersion`, defaulting `false`. Existing notebooks and retrieval versions are completely unaffected.
- **Deterministic everywhere except one stage.** LLM usage confined to `ai-server`'s new extraction endpoint; resolve/assemble/write in `rag-server` is pure code, mirroring `graph`'s munchkin pipeline.
- **Reuse the comparison engine, don't rebuild one.** Graph-augmented retrieval becomes a new search-mode value, not a parallel feature with its own benchmarking UI. Phase 18's `EvaluationRun`/`EvaluationResult`/comparison metrics already work generically over "mode" — extend the mode list and the metrics, not the architecture.
- **Trim the schema relative to `graph`.** No per-tenant DB, no mention vertex in v1 (start with entities + facts + edges, add a mention vertex later only if evidence requires per-mention confidence). 5 new collections, not 11.
- **Same ownership conventions as everything else in Arango.** Every new vertex/edge carries `notebook_id`, `user_id`, `retrieval_version_id` — exactly like `chunks` and `documents` today, not `graph`'s `tenant_id + scope_ids`.
- **Literal-rule resolver first.** Copy `graph`'s NFKC → case-fold → strip-punctuation → collapse-whitespace alias merge as the v1 resolver. Don't build fuzzy entity resolution before there's evidence the literal rule is insufficient.

---

## Proposed data model

### Arango: new vertex collections

**`entities`**
```
_key                deterministic: hash(notebook_id + canonical_name)
notebook_id
user_id
retrieval_version_id
canonical_name
entity_type
aliases: string[]
mention_count: int
created_at
```

**`facts`**
```
_key                deterministic: hash(notebook_id + retrieval_version_id + fact_signature)
notebook_id
user_id
retrieval_version_id
predicate
statement_text        # verbalized form used for graph-branch retrieval text
confidence: float
created_at
```

### Arango: new edge collections

| Edge | From → To | Purpose |
|---|---|---|
| `chunk_mentions_entity` | `chunks` → `entities` | which chunk a mention came from |
| `fact_has_participant` | `facts` → `entities` | fact's subject/object entities, with `role` field |
| `fact_supported_by_chunk` | `facts` → `chunks` | evidence trail for verbalized facts |

### BE: `NotebookRetrievalVersion` addition

```csharp
public bool EnableGraph { get; set; } = false;
public string? GraphExtractionModel { get; set; }   // mirrors EmbeddingModel pattern
public int MaxGraphHops { get; set; } = 1;
public int MaxFactHits { get; set; } = 8;
```

Immutability rule unchanged: once created, a version's `EnableGraph` and graph budget fields don't change — forking a new version is how you turn graph on for a notebook, same as any other retrieval-affecting change today.

### RAG: search mode addition

`RagConfig.search_mode` / `RetrievalConfig.search_mode` gains a new value: `"graph_hybrid"` (vector + BM25 RRF fusion, plus a graph branch seeded from the top vector hits' chunks → `chunk_mentions_entity` → `fact_has_participant` → `fact_supported_by_chunk`, verbalized facts merged into the fused result set). This is additive — `vector`, `bm25`, `hybrid` are untouched.

### Phase 18 metrics extension

Add to the existing comparison payload (`EvaluationResult` / run-detail computation), only populated when either side of a comparison used `graph_hybrid`:

- `graph_hit_rate` — fraction of queries where the graph branch contributed a result absent from plain `hybrid` on the same version.
- `fact_coverage` — average number of distinct facts surfaced per query.

No new tables. No new UI surface beyond adding `graph_hybrid` to the existing mode picker in `/lab/retrieval-bench` (Phase 18 already renders per-mode columns and history).

---

## Task list

### Gate A — Graph schema foundation

- [x] Add `entities`, `facts` vertex collections and `chunk_mentions_entity`, `fact_has_participant`, `fact_supported_by_chunk` edge collections to `vector_store.ensure_collections`.
- [x] Add a named graph (`notebook_knowledge_graph`) joining the new collections, mirroring `graph`'s `knowledge_graph`.
- [x] Add persistent indexes on `(notebook_id, retrieval_version_id)` for `entities` and `facts`.
- [x] Add an ArangoSearch view (BM25 over `canonical_name`/`aliases`) for keyword entity lookup — explicitly **not** vector search on entities in v1, matching `graph`'s own current limitation. Named `entities_view` rather than `entities_search_view` to match the existing `chunks_view` naming convention in this codebase.

**Acceptance**
- [x] Fresh deployment creates all 5 new collections + view + named graph without affecting existing `documents`/`chunks`/`notebooks`/`experiments`. (`ensure_collections`/`ensure_knowledge_graph`/`ensure_graph_indexes`/`ensure_entities_view` are all additive and idempotent; 8 unit tests against a fake db client in `rag-server/tests/test_vector_store.py`, plus 6 integration tests against a real ArangoDB in `rag-server/tests/test_vector_store_integration.py` — skipped unless `ARANGO_URL` is set, run via `docker compose up -d arangodb arango-init` first.)
- [ ] Every written vertex/edge carries `notebook_id` + `user_id` + `retrieval_version_id`. (Deferred to Gate B — nothing writes to these collections yet.)

### Gate B — Extraction pipeline wired through existing job seams

**AI server**
- [x] Add `POST /ai/extract/graph` (internal secret protected): input `{ chunks: [{chunk_index, text}] }`, output `{ chunk_index, mentions: [...], facts: [...] }[]`.
- [x] Implement via `LLMGateway` structured-output call, confined to this one module — no other AI server code gains LLM extraction responsibility. (`app/graph_extraction.py`.)
- [x] Add `correlation_id` forwarding per existing convention (`rag_client.py` / `be_client.py` pattern). (Route accepts `X-Correlation-Id` and forwards it on the `be_client.log_request` audit call, matching `/session-state/update`.)

**RAG server**
- [x] Add `POST /graph/ingest`: input `{ source_id, notebook_id, user_id, retrieval_version_id, chunk_extractions[] }`.
- [x] Implement deterministic resolver: NFKC normalize → case-fold → strip punctuation → collapse whitespace → merge aliases within the same `(notebook_id, retrieval_version_id)` scope. (`app/graph_ingest.py:normalize_entity_name`.)
- [x] Implement assembler: write `entities` (upsert by deterministic `_key`), `facts`, and the three edge collections in one batch, atomically per collection (mirrors existing `store_chunks` batching). Uses `overwrite_mode="replace"` so retried/re-run ingests upsert instead of duplicating.
- [x] Add `DELETE` cleanup path mirroring `delete_chunks(retrieval_version_id=...)` so graph data is retired exactly when a version's chunks are. Implemented as `vector_store.delete_graph_payload`, called from the existing `DELETE /notebooks/{notebook_id}/chunks` handler in lockstep with `delete_version_chunks` rather than as a separate endpoint BE would need to remember to call.

**BE**
- [x] Add `EnableGraph`, `GraphExtractionModel`, `MaxGraphHops`, `MaxFactHits` to `NotebookRetrievalVersion` + migration + model snapshot. `FromPreset`/`Fork` accept optional overrides (Fork inherits the parent's graph settings by default); `LabRetrievalVersionsController.Create` and the version listing expose the new fields. Migration generated via `dotnet ef migrations add` with explicit `HasDefaultValue(1)`/`HasDefaultValue(8)` so existing rows backfill to the intended defaults rather than CLR `0`. 5 new BE tests, full suite 60/60, `dotnet format --verify-no-changes` clean.
- [x] Extend `IngestionJobWorker` (and `ReindexJobWorker`, same code path): after a successful ingest, if the target retrieval version has `EnableGraph`, fetch chunk texts (existing `GET /documents/{source_id}/content`), call AI server's extraction endpoint, then call RAG's `/graph/ingest`. Factored into a shared `GraphExtractionService` so both workers (and `ReindexJobWorker`'s notebook-scope multi-source path) call one orchestration path; `RagClient` gained `GetSourceContentAsync`/`GraphIngestAsync`.
- [x] Extraction failure must not fail the underlying ingestion — log and mark a `GraphExtractionStatus` field (`Skipped|Succeeded|Failed`) on the job record; vector/BM25 retrieval for that source must remain usable either way. `GraphExtractionService.ExtractAndIngestAsync` never throws; notebook-scope reindex aggregates per-source statuses (`GraphExtractionService.Aggregate`: any failure dominates, then any success, then skipped).

**Acceptance**
- [x] A notebook with `EnableGraph=false` ingests exactly as it does today — no AI-server graph call, no new Arango writes. (`ExtractAndIngestAsync` returns `Skipped` before any HTTP call when `EnableGraph` is false; covered by `Worker_SkipsGraphExtraction_WhenVersionDoesNotEnableGraph` / `Worker_SourceScope_SkipsGraphExtraction_WhenTargetVersionDoesNotEnableGraph`.)
- [x] A notebook with `EnableGraph=true` produces entities/facts/edges scoped to the correct `retrieval_version_id` after ingest. (End-to-end orchestration covered by `Worker_RunsGraphExtractionAndSucceeds_WhenVersionEnablesGraph` / `Worker_SourceScope_MarksGraphExtractionSucceeded_WhenTargetVersionEnablesGraph`; the actual Arango write correctness was independently verified in Gate B's RAG-side live-ArangoDB integration tests.)
- [x] An extraction failure leaves the source `Ingested` with vector/BM25 search intact, and is visible in job status. (`Worker_MarksGraphExtractionFailed_ButIngestionStillSucceeds_WhenAiServerErrors`: job stays `Succeeded`, source stays `Ingested`, `GraphExtractionStatus` is `Failed`.)

Additionally verified the real cross-service chain with no mocks: brought up live `arangodb` + `rag-server` + `ai-server` containers, seeded a chunk in Arango, then called the actual HTTP endpoints in sequence (`rag-server` `GET /documents/{id}/content` → `ai-server` `POST /ai/extract/graph` → `rag-server` `POST /graph/ingest`) exactly as `GraphExtractionService` does. With only a placeholder `OPENAI_API_KEY` in this dev environment, the LLM call inside `ai-server` failed and degraded to an empty `{mentions: [], facts: []}` per chunk rather than erroring — a real (not simulated) demonstration of the "extraction failure must not break the pipeline" guarantee.

### Gate C — Graph-aware retrieval branch

- [ ] Add `graph_hybrid` as a valid `search_mode` value in RAG `models.py` / `rag_config.py` validation.
- [ ] Implement graph branch in `vector_store.py`: seed from top-N vector hits' chunks → traverse `chunk_mentions_entity` → `fact_has_participant` → `fact_supported_by_chunk` → verbalized fact text, capped by `max_graph_hops` / `max_fact_hits`.
- [ ] Fuse graph branch results into the existing Python-side RRF alongside vector + BM25 (do not add an AQL join — matches `graph`'s own design choice and keeps fusion logic in one place).
- [ ] Extend `/search/hybrid` and `/search/benchmark` to accept `search_mode=graph_hybrid` and return graph-branch provenance (`fact_id`, `participant entities`) alongside the existing chunk identity fields.
- [ ] Respect `retrieval_version_id` scoping exactly like every other mode (Phase 18's isolation guarantee must hold for the new mode too).

**Acceptance**
- [ ] `graph_hybrid` on a non-graph-enabled version behaves identically to `hybrid` (no entities/facts exist, branch is a no-op) — never errors.
- [ ] `graph_hybrid` on a graph-enabled version returns at least one result with fact provenance when the underlying corpus supports it.
- [ ] Cross-version graph data never leaks (same isolation tests as Phase 18 Gate A, extended to the new collections).

### Gate D — Lab retrieval-bench integration

- [ ] Add `graph_hybrid` to the mode picker in `LabRetrievalBenchPage.tsx` ad hoc compare and dataset runner.
- [ ] Extend BE's `RetrievalComparisonService` to compute `graph_hit_rate` and `fact_coverage` when either compared side used `graph_hybrid`.
- [ ] Surface fact provenance in the side-by-side diff (which facts/entities backed a given result), reusing the existing diff layout rather than adding a new page.

**Acceptance**
- [ ] The owner can compare a `hybrid` version against a `graph_hybrid`-enabled version on the same dataset and see overlap/rank-delta metrics plus the two new graph metrics.
- [ ] A dataset run involving `graph_hybrid` persists and reopens identically to existing runs (no special-cased history view).

### Gate E — Phase completion hygiene

- [ ] Focused tests: AI extraction endpoint, RAG resolver/assembler, graph branch isolation, BE job orchestration failure handling, comparison metrics.
- [ ] Review document under `docs/reviews/phase-19-review.md`.
- [ ] `ROADMAP3.md` Phase 19 section updated to reflect what shipped; `docs/POSITIONING.md` "how to tell if this is working" checked against the actual result.

---

## File touch list

| Area | Expected files |
|---|---|
| AI server | new `routes/extract.py` (or equivalent), uses existing `gateway/` |
| RAG server | `vector_store.py`, `main.py`, `models.py`, `rag_config.py`, new resolver/assembler module |
| BE entities | `NotebookRetrievalVersion.cs` |
| BE data | `AppDbContext.cs`, new migration, model snapshot |
| BE services | `IngestionJobWorker.cs`, `ReindexJobWorker.cs`, `RagClient.cs`, `RetrievalComparisonService.cs` |
| Frontend | `LabRetrievalBenchPage.tsx`, `LabRetrievalVersionsPage.tsx` (expose `EnableGraph` on version creation) |
| Tests | AI extraction unit tests, RAG graph ingest/search tests, BE job-orchestration and comparison-metric tests |

---

## Suggested implementation order

1. Arango schema (Gate A) — inert until anything writes to it.
2. AI extraction endpoint, tested standalone with fixed sample chunks.
3. RAG `/graph/ingest` resolver + assembler, tested standalone with fixed extraction payloads.
4. BE wiring: `EnableGraph` field + job orchestration calling both, with extraction-failure isolation.
5. RAG `graph_hybrid` search mode + fusion.
6. Phase 18 comparison engine extension (two new metrics).
7. Lab UI: mode picker + fact provenance in the diff.
8. Review + patch pass.

---

## Open questions to resolve before implementation

1. **Extraction cost/latency.** LLM extraction per chunk is the expensive new operation. Recommended: gate it strictly behind explicit per-notebook opt-in (`EnableGraph`), run it as a best-effort step after normal ingestion succeeds (never blocking), and cap chunks-per-extraction-call to keep AI-server requests bounded.
2. **Resolver sophistication.** Start with the literal-rule resolver only (matches `graph`'s own current state). Do not build embedding-based entity resolution until the literal rule visibly produces bad merges on real data.
3. **Graph seed strategy.** Implement `from_chunks` only (seed from vector hits), matching `graph`'s main pipeline. Skip `from_text` seeding — `graph` itself never integrated it past a debug endpoint, so there's no proven design to port.
4. **Entity embeddings.** Not stored in v1 — entity search stays keyword-only via `entities_search_view`, exactly mirroring `graph`'s current (acknowledged) limitation. Revisit only if the comparison data shows keyword-only entity matching is the bottleneck.

---

## Deliverable

The owner can:

1. Fork a notebook-local retrieval version with `EnableGraph=true`.
2. Reindex a notebook and get an entity/fact graph layer built alongside the existing vector index, without affecting any other notebook or version.
3. Run `graph_hybrid` queries that fuse vector/BM25 results with graph-derived facts, scoped correctly to the retrieval version.
4. Use the existing `/lab/retrieval-bench` — unmodified in structure, just one more mode — to get reproducible evidence of whether the graph branch actually helps on their own corpus.

This is the phase where `rag_sys` stops being "a RAG product with a graph database it doesn't use for graphs" and becomes the thing `docs/POSITIONING.md` says it should be.
