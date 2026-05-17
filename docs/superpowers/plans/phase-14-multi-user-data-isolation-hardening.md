# Phase 14 — Multi-User and Data Isolation Hardening

**Branch:** `phase-14-multi-user-isolation`  
**Goal:** Make user ownership explicit across SQL, RAG payloads, cleanup paths, and agent tool calls; add focused cross-user regression tests.

---

## Current gaps

| Surface | Gap |
|---|---|
| Arango `documents` / `chunks` / `experiments` | Scoped by `notebook_id`, but do not persist or filter `user_id` |
| RAG search/content APIs | Trust notebook scope alone; no user lineage in query contract |
| Notebook archive | Soft-deletes SQL notebook but leaves retrieval payloads behind |
| Cleanup helpers | Source cleanup exists; notebook/user cleanup do not |
| BE tests | Only a few ownership checks exist; sources/notes/experiments/chat isolation coverage is thin |
| RAG tests | No assertions that user scope appears in AQL |
| Agent tools | Active-notebook guard exists, but needs regression coverage |

---

## Implementation steps

1. Add `user_id` to RAG ingest/search/content/experiment contracts and persist it on Arango documents/chunks/experiments.
2. Filter retrieval, content, and experiment queries by `user_id` as well as notebook scope.
3. Add notebook/user cleanup helpers in RAG; call notebook cleanup when a notebook is archived.
4. Keep BE as the authorization boundary by threading `CurrentUserAccessor.UserId` through typed `RagClient` methods.
5. Add focused tests:
   - BE cross-user notebook/source/note/search/chat/experiment access fails or returns empty.
   - RAG AQL includes user filters and cleanup functions target source/notebook/user scopes.
   - AI agent tools reject notebook override attempts.
6. Update ROADMAP2 and write phase review after verification.
