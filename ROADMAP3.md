# RAG System - Personal Knowledge Lab Roadmap

This roadmap extends the product after `ROADMAP.md` and `ROADMAP2.md`.

The product remains a **personal knowledge base**. The new `/lab` area is not a second product and not a public-facing analytics suite; it is a restricted internal workbench for iterating on retrieval, prompts, and agent behavior against the same notebooks and sources used by the main product.

Primary goals:

1. Keep the normal notebook experience clean.
2. Make RAG and agent changes versioned, reproducible, and reversible.
3. Let the owner compare retrieval and prompt variants with both human judgment and model-assisted evaluation.
4. Preserve BE server ownership boundaries while allowing ArangoDB to evolve toward future GraphRAG work.

---

## Relationship to ROADMAP2

`ROADMAP3` should **not** replace the unfinished work in `ROADMAP2`.

### Must finish before or inside the first ROADMAP3 phase

- [ ] Complete the remaining **Phase 13** contract work that ROADMAP3 depends on:
  - [x] Stop accepting arbitrary frontend model names; frontend should send a preset/mode, BE resolves the concrete model, AI validates allowlist.
  - [x] Persist RAG config snapshots on chat requests, not only source ingestion.
  - [x] Add explicit retrieval/version metadata hooks needed for future re-ingest and comparisons.
- [ ] Complete the essential parts of **Phase 14**:
  - [ ] Add `user_id` to Arango retrieval documents where practical.
  - [ ] Add cleanup tests for source / notebook / user deletion paths.
  - [ ] Add isolation tests covering retrieval and experiment access.

### Can be deferred until after ROADMAP3 begins

- [ ] Full one-ArangoDB-database-per-user promotion. The current helper script is enough for now if Arango documents carry ownership metadata.
- [ ] Most of **Phase 15** deployment/operations work:
  - [ ] production compose profile
  - [ ] backup/restore runbooks
  - [ ] broad metrics
  - [ ] dependency update policy

Reasoning: the Lab needs trustworthy ownership, versioning, and reproducibility before it needs production-grade operations. Otherwise it would generate sophisticated experiments on top of ambiguous data lineage.

---

## Product Shape

```text
[Knowledge Product]
  notebooks / sources / notes / chat / agent
            │
            └── [Restricted /lab]
                  retrieval preset library
                  notebook-local retrieval version trees
                  retrieval versions
                  prompt versions
                  agent packages / versions / presets
                  re-index jobs
                  A/B comparisons
                  human labels
                  judge evaluations
```

### Access Model

- [ ] Add a restricted capability for `/lab`.
- [ ] Initial implementation may use a simple `dev_admin` user flag or environment-seeded privileged account.
- [ ] Regular users should not see Lab navigation or access Lab APIs.
- [ ] Keep BE server as the final authorization boundary; AI/RAG remain internal services.

### Design Principles

- Prefer **immutable versions** over mutable global tuning knobs.
- Prefer **preset-first UX** with deeper notebook-local forks only inside Lab.
- Prefer **snapshots** over implicit dependence on current environment variables.
- Prefer **jobs** for re-ingest/re-embed work over synchronous controller actions.
- Prefer **paired human + judge evaluation** over judge-only automation.
- Prefer **installable agent packages** over monolithic hard-coded agent behavior.
- Treat ArangoDB as a retrieval projection layer today, while adding enough metadata to support future GraphRAG and safe cleanup.

---

## Target Data Ownership

### MySQL - canonical product and experiment control plane

- users
- notebooks
- sources
- notes
- chat sessions / messages / requests / tasks
- ingestion and re-index jobs
- retrieval presets
- notebook retrieval versions
- prompt versions
- agent packages / versions / presets
- evaluation datasets / queries / runs
- human relevance labels
- judge evaluations

### ArangoDB - retrieval and experimental payload plane

- documents
- chunks
- experiments / retrieval result payloads
- future graph entities / relations

### Ownership Rule

- MySQL owns product truth and workflow state.
- ArangoDB stores searchable projections and experiment payloads.
- Arango documents must contain enough lineage to be cleaned up and audited:
  - `user_id`
  - `notebook_id`
  - `source_id`
  - `retrieval_version_id`
  - `embedding_model`
  - `embedding_dimensions`

---

## Proposed Core Data Model

### New MySQL entities

| Entity | Purpose |
|--------|---------|
| `RetrievalPresets` | Global starter templates such as `general`, `longform`, or `keyword-heavy` |
| `NotebookRetrievalVersions` | Immutable notebook-local retrieval version tree, forked from presets or prior notebook versions |
| `PromptVersions` | Immutable prompt snapshots that may be referenced by agent versions or direct chat modes |
| `AgentPackages` | Installed agent modules known to the system |
| `AgentVersions` | Immutable published versions of an agent package |
| `AgentPresets` | User-facing choices that map friendly frontend labels to active agent versions |
| `ReindexJobs` | Durable source / notebook rebuild work |
| `EvaluationDatasets` | Named sets of evaluation queries |
| `EvaluationQueries` | Query text, expected answer notes, optional gold source metadata |
| `EvaluationRuns` | One run of a dataset against one or more retrieval / prompt versions |
| `EvaluationResults` | Per-query outputs, sources, metrics, traces |
| `RelevanceLabels` | Human labels over result items |
| `JudgeEvaluations` | Model-produced scores and rationale |

### Existing entities to extend

| Entity | Additions |
|--------|-----------|
| `Notebooks` | active notebook retrieval version id |
| `Sources` | active retrieval version id, last indexed version id |
| `ChatRequests` | retrieval version snapshot, prompt version id, agent version id, optional evaluation lineage |
| `IngestionJobs` or new `ReindexJobs` | distinguish ordinary ingestion from notebook-wide rebuilds |

### Arango document additions

| Collection | Additions |
|------------|-----------|
| `documents` | `user_id`, `retrieval_version_id`, ingestion snapshot |
| `chunks` | `user_id`, `retrieval_version_id`, embedding model/dimensions, optional chunk hash |
| `experiments` | version references instead of config-only ad hoc payloads |

---

## `/lab` Information Architecture

```text
/lab
├─ Overview
│  ├─ recent runs
│  ├─ active retrieval version
│  └─ pending re-index jobs
├─ Retrieval Versions
│  ├─ create immutable version
│  ├─ compare configs
│  └─ promote / rollback
├─ Re-indexing
│  ├─ re-ingest source
│  ├─ re-ingest whole notebook
│  └─ re-embed / status / failures
├─ Retrieval Bench
│  ├─ ad hoc query compare
│  ├─ dataset run
│  └─ version A/B diff
├─ Relevance Review
│  ├─ human labels
│  └─ disagreement queue
├─ Judge Evaluation
│  ├─ relevance
│  ├─ groundedness
│  ├─ usefulness
│  └─ citation quality
└─ Prompt / Agent Playground
   ├─ prompt versions
   ├─ trace compare
   └─ answer compare
```

---

## Phase 16 - Lab Foundations and Versioned Retrieval

**Goal:** Make retrieval configuration explicit, immutable, and reproducible.

- [x] Finish ROADMAP2 dependencies required for versioning:
  - [x] frontend model presets instead of arbitrary model strings
  - [x] chat request RAG config snapshots
  - [x] `user_id` lineage on Arango retrieval documents
- [x] Add Lab authorization gate:
  - [x] `dev_admin` capability
  - [x] backend authorization policy
  - [x] hidden frontend navigation
- [x] Add `RetrievalPresets`:
  - [x] global starter templates
  - [x] examples: `general`, `longform`, `keyword-heavy`, `transcript`
  - [x] editable by developers, not ordinary users
- [x] Add `NotebookRetrievalVersions`:
  - [x] notebook-local immutable versions
  - [x] optional parent version id
  - [x] optional origin preset id
  - [x] chunk size
  - [x] overlap
  - [x] embedding model
  - [x] embedding dimensions
  - [x] default search mode
  - [x] default `top_k`
  - [x] default hybrid alpha
  - [x] created by / created at / notes
  - [x] immutable after creation
- [x] Add preset-first activation model:
  - [x] notebooks start from a preset snapshot
  - [x] Lab edits fork a notebook-local version from the current version
  - [x] notebooks hold one active retrieval version for normal product use
- [x] Add retrieval lineage to ingestion:
  - [x] source active retrieval version
  - [x] document/chunk version metadata
  - [x] chat request retrieval snapshot
- [x] Add `/lab/retrieval-versions` UI:
  - [x] list
  - [x] create
  - [x] fork from preset or prior notebook version
  - [x] inspect
  - [x] mark active for the notebook

**Deliverable:** Every notebook can begin simply from a preset, then grow its own auditable retrieval version tree when Lab work begins.


**Current status (2026-05-17):** Implemented on `phase-16-lab-foundations-versioned-retrieval`. The system now has dev-admin-gated Lab access, seeded global retrieval presets, notebook-local immutable retrieval versions, notebook/source/chat lineage fields, RAG payload metadata for retrieval versions, model-preset handling at the BE boundary, and a first `/lab/retrieval-versions` UI for listing, creating, and activating versions while making the pending re-index boundary explicit.

---

## Phase 17 - Re-indexing and Retrieval Version Lifecycle

**Goal:** Safely rebuild source and notebook indexes when retrieval configuration changes.

- [ ] Add durable re-index job model:
  - [ ] source-level
  - [ ] notebook-level
  - [ ] re-ingest
  - [ ] re-embed only where compatible
  - [ ] queued / running / retrying / failed / succeeded / cancelled
- [ ] Add rebuild orchestration:
  - [ ] re-ingest one source against a chosen retrieval version
  - [ ] re-ingest whole notebook
  - [ ] record previous and target versions
  - [ ] preserve old chunks until target build succeeds
  - [ ] allow rollback / promotion
- [ ] Extend RAG payload:
  - [ ] retrieval-version scoped chunks
  - [ ] search by requested retrieval version
  - [ ] cleanup stale version payloads only after safe promotion
- [ ] Add `/lab/reindex` UI:
  - [ ] queue jobs
  - [ ] progress
  - [ ] failures
  - [ ] promote successful rebuild

**Deliverable:** The owner can rebuild a notebook under a new retrieval design without corrupting the live knowledge base.

---

## Phase 18 - Retrieval Benchmarks and A/B Comparison

**Goal:** Compare retrieval versions on the same notebook and query set.

- [ ] Add evaluation dataset model:
  - [ ] named datasets
  - [ ] notebook scope
  - [ ] reusable query sets
  - [ ] optional expected answer / gold source notes
- [ ] Add evaluation run model:
  - [ ] dataset id
  - [ ] retrieval version A
  - [ ] retrieval version B
  - [ ] search mode set
  - [ ] timestamps / actor
- [ ] Add retrieval metrics:
  - [ ] latency
  - [ ] overlap@k
  - [ ] source overlap
  - [ ] rank deltas
  - [ ] result-count deltas
- [ ] Add `/lab/retrieval-bench` UI:
  - [ ] ad hoc query compare
  - [ ] dataset runner
  - [ ] side-by-side chunk diff
  - [ ] result history

**Deliverable:** The owner can answer, with evidence, whether one retrieval version beats another on their own corpus.

---

## Phase 19 - Human Relevance Labels and Judge Evaluation

**Goal:** Turn comparisons into evaluation, not just inspection.

- [ ] Add human relevance labels:
  - [ ] chunk-level labels as the primary review unit
  - [ ] source-level labels as an explicit aggregate / supporting signal
  - [ ] relevant
  - [ ] partially relevant
  - [ ] irrelevant
  - [ ] optional note
  - [ ] optional expected source / expected answer
- [ ] Add judge evaluation pipeline:
  - [ ] relevance
  - [ ] groundedness
  - [ ] usefulness
  - [ ] citation quality
  - [ ] score + rationale + judge model version
- [ ] Keep judge and human judgments separate:
  - [ ] disagreement view
  - [ ] human-overrides are visible, not destructive
- [ ] Add summary metrics:
  - [ ] precision-style relevance summaries
  - [ ] judge averages
  - [ ] disagreement rate
  - [ ] per-version trend
- [ ] Add `/lab/relevance-review` and `/lab/judge` UI.

**Deliverable:** Retrieval experiments become measurable, reviewable, and auditable.

---

## Phase 20 - Agent Packages, Prompt Versions, and Playground

**Goal:** Make agents installable, replaceable components whose behavior can be versioned and compared on top of controlled retrieval.

- [ ] Add `PromptVersions`:
  - [ ] file-backed source templates remain the developer authoring surface
  - [ ] DB stores immutable prompt snapshots for installed / promoted versions
  - [ ] chat system prompt
  - [ ] RAG system prompt
  - [ ] agent system prompt
  - [ ] tool-use policy
  - [ ] notes
  - [ ] immutable content hash
- [ ] Add `AgentPackages`:
  - [ ] stable package id
  - [ ] name / description
  - [ ] manifest metadata
  - [ ] installation source
  - [ ] enabled / disabled state
- [ ] Add `AgentVersions`:
  - [ ] immutable package version
  - [ ] referenced prompt versions
  - [ ] tool set
  - [ ] tool policy
  - [ ] planner / loop strategy
  - [ ] max steps and config schema
  - [ ] compatibility metadata
- [ ] Add `AgentPresets`:
  - [ ] frontend-friendly choices such as `General Assistant`, `Research Assistant`, or `Note Builder`
  - [ ] map presets to active agent versions
  - [ ] allow promotion / rollback from Lab
- [ ] Prefer install-first over upload-first:
  - [ ] first release loads reviewed repo-native agent packages with manifests
  - [ ] arbitrary uploaded executable bundles are out of scope for this roadmap
- [ ] Add prompt installation flow:
  - [ ] developer edits source-controlled prompt files
  - [ ] install / promote action captures immutable DB snapshot
  - [ ] later source-file edits never rewrite historical prompt versions
- [ ] Add agent / prompt lineage:
  - [ ] chat requests store prompt version id
  - [ ] agent runs store prompt version id
  - [ ] agent runs store agent package id and version id
  - [ ] evaluation runs can bind a prompt version
- [ ] Add Lab package management:
  - [ ] discover installed agent packages
  - [ ] enable / disable packages
  - [ ] inspect manifests and versions
  - [ ] promote a version into a user-facing preset
- [ ] Add playground capabilities:
  - [ ] same query, same retrieval version, different prompt or agent version
  - [ ] trace comparison
  - [ ] answer comparison
  - [ ] token / latency / source comparison
- [ ] Extend judge evaluation for answer quality:
  - [ ] completeness
  - [ ] faithfulness
  - [ ] actionability where relevant
- [ ] Add `/lab/agents` and `/lab/prompt-playground` UI.

**Deliverable:** The main product exposes simple agent presets, while Lab can install, version, compare, promote, and roll back pluggable agent implementations.

---

## Phase 21 - GraphRAG Readiness

**Goal:** Prepare the retrieval plane for graph experiments without forcing GraphRAG into the main product prematurely.

- [ ] Finalize ownership and lineage conventions across Arango documents.
- [ ] Define graph candidate entities:
  - [ ] entities
  - [ ] relations
  - [ ] mentions
  - [ ] source/chunk backlinks
- [ ] Add graph extraction experiments behind Lab only.
- [ ] Add graph-version metadata parallel to retrieval / prompt versions.
- [ ] Compare vector / sparse / hybrid / graph-augmented retrieval in Lab.

**Deliverable:** GraphRAG can be explored as a controlled retrieval variant without destabilizing the personal knowledge product.

---

## Suggested Implementation Order

| Order | Work | Why |
|------:|------|-----|
| 1 | Finish ROADMAP2 essentials from Phases 13 and 14 | Lab needs clean contracts and ownership first |
| 2 | Phase 16 | Versioning is the spine of every later Lab feature |
| 3 | Phase 17 | A/B is only meaningful if versions can be rebuilt safely |
| 4 | Phase 18 | Comparison becomes useful once versioned corpora exist |
| 5 | Phase 19 | Evaluation should be layered on top of stable comparisons |
| 6 | Phase 20 | Prompt experiments become meaningful after retrieval is controlled |
| 7 | Phase 21 | GraphRAG comes after the evaluation machinery can judge it |

---

## Explicit Non-Goals for the First Lab Release

- Do not expose Lab features to ordinary users.
- Do not make judge-model scores the sole source of truth.
- Do not mutate retrieval versions in place.
- Do not delete prior retrieval payloads before the replacement version is proven healthy.
- Do not split Lab into a separate product yet; keep it close to the knowledge base until the experimental tooling becomes genuinely corpus-agnostic.

---

## Open Design Questions

1. Resolved: use a global preset library plus notebook-local immutable retrieval version trees.
2. Resolved: the live product uses one active retrieval version per notebook; per-query overrides stay inside Lab.
3. Resolved: allow re-embed-only only when chunk boundaries are unchanged; if chunking changes, require full re-ingest so lineage remains semantically clean.
4. Resolved: store both chunk-level and source-level human labels; use chunk-level review as the primary Lab workflow and source-level labels as an explicit aggregate/supporting layer.
5. Resolved: prompt authoring remains file-backed for reviewability; installed/promoted prompt versions are immutable DB snapshots for reproducibility.
6. Resolved: keep agent packages repo-native for this roadmap; uploaded executable bundles are intentionally out of scope.
