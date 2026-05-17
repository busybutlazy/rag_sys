# Phase 16 — Lab Foundations and Versioned Retrieval

**Branch:** `phase-16-lab-foundations-versioned-retrieval`  
**Goal:** Turn retrieval settings from ambient configuration into explicit, immutable notebook versions, and introduce the first restricted `/lab` surface for inspecting and activating them.

---

## Current state (gaps to close)

| Surface | Gap |
|---|---|
| Phase 13 carry-over | BE still accepts concrete frontend model names rather than product presets; `ChatRequest.ContextSnapshotJson` captures message context only, not retrieval lineage/config. |
| Authorization | There is only authenticated/not-authenticated access; no privileged capability for Lab routes or navigation. |
| Retrieval config | RAG config exists as process config and ingestion snapshot, but no canonical MySQL preset/version entities exist. |
| Notebook/source lineage | Notebooks and sources do not point at active retrieval versions, so a future rebuild cannot know which version is live or indexed. |
| Frontend | Product UI has search/experiments, but no `/lab` route or retrieval-version management surface. |

---

## Scope decisions for this phase

1. **Phase 16 owns the remaining ROADMAP2 dependencies that directly block versioning.** Phase 14 ownership work is already complete; the remaining concrete carry-over here is the Phase 13 model-preset and chat retrieval snapshot work.
2. **Preset-first UX, notebook-local history.** Developers maintain a small global preset library; notebooks receive immutable snapshots and may fork local versions later.
3. **Version metadata first, multi-version payload later.** This phase records active/last-indexed lineage and sends the active version into ingestion/search metadata, but Phase 17 will own side-by-side payload preservation, rebuild orchestration, and promotion safety.
4. **Lab is capability-gated, not a separate product.** We add a simple `dev_admin` user capability and keep BE as the final authority.

---

## Step 1 — Finish the Phase 13 dependencies ROADMAP3 needs

**Files:** BE chat model flow, frontend chat request flow, AI request validation tests

- Replace arbitrary frontend `model` submission with semantic `model_preset` values such as `chat_default` / `agent_default`.
- Resolve concrete models only in BE through `ModelRegistry`.
- Keep AI allowlist validation for the resolved concrete model.
- Extend chat request snapshots so stored context includes:
  - active retrieval version id
  - retrieval config snapshot
  - chosen search mode / top_k defaults
  - existing message context

**Acceptance:** frontend no longer controls provider model names; each persisted chat request can explain which retrieval design was active when it ran.

---

## Step 2 — Add Lab authorization capability

**Files:** `User`, auth claims/JWT issuance, BE authorization policy, app shell/navigation

- Add `IsDevAdmin` to `Users` and seed the env-created bootstrap admin as privileged.
- Add the capability claim to JWTs.
- Register a `DevAdminOnly` authorization policy.
- Add minimal `/api/lab/*` route surface protected by that policy.
- Hide Lab navigation for ordinary users; do not rely on the frontend for enforcement.

**Acceptance:** ordinary users cannot call Lab APIs or see Lab navigation; privileged users can.

---

## Step 3 — Add the retrieval control-plane schema

**Files:** new entities, EF migration, `AppDbContext`

### New entities

- `RetrievalPreset`
  - `Id`, `Key`, `Name`, `Description`
  - chunk size / overlap
  - embedding model / dimensions
  - default search mode / top_k / hybrid alpha
  - `CreatedAt`, `UpdatedAt`
- `NotebookRetrievalVersion`
  - `Id`, `NotebookId`, `CreatedByUserId`
  - optional `ParentVersionId`, optional `OriginPresetId`
  - immutable config fields matching the preset schema
  - `Notes`, `CreatedAt`

### Existing entity extensions

- `Notebook.ActiveRetrievalVersionId`
- `Source.ActiveRetrievalVersionId`
- `Source.LastIndexedRetrievalVersionId`
- `ChatRequest.RetrievalVersionId`

### Seed/bootstrap behavior

- Seed starter presets idempotently: `general`, `longform`, `keyword-heavy`, `transcript`.
- When a notebook is created, snapshot the `general` preset into its first notebook-local version and activate it.

**Acceptance:** every newly created notebook has an immutable active retrieval version from birth.

---

## Step 4 — Thread retrieval lineage into product flows

**Files:** notebook creation, source upload/ingestion jobs, `RagClient`, RAG request models/storage

- Sources inherit the notebook active retrieval version when created.
- Ingestion payload includes retrieval version id plus the config snapshot that should drive chunking/embedding.
- RAG documents/chunks persist `retrieval_version_id`, `embedding_model`, and `embedding_dimensions` alongside existing ownership metadata.
- Search requests use the notebook active retrieval version’s defaults when caller does not override mode/top_k.
- Chat request snapshot stores the version/config used by retrieval at request time.

**Acceptance:** notebook → source → RAG payload → chat request forms one auditable lineage chain.

---

## Step 5 — Add first Lab retrieval-version APIs

**Files:** new Lab controller/service, tests

- `GET /api/lab/retrieval-presets`
- `GET /api/lab/notebooks/{notebookId}/retrieval-versions`
- `POST /api/lab/notebooks/{notebookId}/retrieval-versions`
  - create from a preset or parent notebook version
  - immutable after creation
- `POST /api/lab/notebooks/{notebookId}/retrieval-versions/{versionId}/activate`
  - switch notebook active version only
  - do **not** rebuild payload yet; return clear pending-index state for Phase 17

**Acceptance:** Lab can inspect, fork, and activate retrieval versions without mutating historical records.

---

## Step 6 — Add minimal `/lab/retrieval-versions` UI

**Files:** router/app shell, new Lab page/components, API helpers

- Add restricted `/lab/retrieval-versions` page.
- Show presets, selected notebook, version history, parent/origin lineage, and active badge.
- Allow creating a version from preset/current parent and activating it.
- Surface the distinction between **active config** and **indexed payload** so users can see when a notebook is awaiting Phase 17-style rebuild work.

**Acceptance:** a privileged user can manage retrieval versions without touching raw APIs.

---

## Step 7 — Verification, review, and roadmap update

- Add/extend BE tests for:
  - Lab authorization
  - notebook default version creation
  - immutable version creation / activation
  - source inheritance
  - chat request retrieval snapshot
- Add/extend RAG tests for retrieval metadata persistence.
- Add/update frontend tests where existing harness permits.
- Write `docs/reviews/phase-16-review.md`.
- Create `phase-16-patch`, apply review fixes, rebase/ff-merge back into the feature branch.
- Mark Phase 16 complete in `ROADMAP3.md` with a concise current-status note.

---

## Out of scope until Phase 17+

- Re-ingest orchestration and durable rebuild jobs
- Serving multiple retrieval-version payloads side by side
- Safe promotion after rebuild success
- Evaluation datasets / A-B comparisons / relevance labels
- Prompt and agent versioning
