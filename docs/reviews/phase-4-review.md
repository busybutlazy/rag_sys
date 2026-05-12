# Phase 4 Code Review — RAG Pipeline

**Branch:** phase-4-rag  
**Date:** 2026-05-12  
**Reviewer:** Claude Sonnet 4.6

---

## Summary

Phase 4 implements the full ingest pipeline (extract → chunk → embed → store) and vector search with context injection. Two issues found: one security, one dead code.

---

## Findings

### SEC-01 — `_check_secret` is fail-open when INTERNAL_SECRET is unset (PATCH REQUIRED)

**File:** `rag-server/app/main.py`

```python
if _INTERNAL_SECRET and x_internal_secret != _INTERNAL_SECRET:
    raise HTTPException(status_code=403, detail="Forbidden")
```

If `INTERNAL_SECRET` env var is empty or unset, the guard is skipped entirely — all requests pass. This means a misconfigured container exposes the ingest and search endpoints to anyone on the internal network without authentication.

**Fix:** Fail-closed — raise 403 if `_INTERNAL_SECRET` is not configured, or at minimum raise `SystemExit` at startup.

---

### LOGIC-01 — Dead variable `documents` in POST /ingest (PATCH REQUIRED)

**File:** `rag-server/app/main.py` line 37

```python
documents = db.collection("chunks")  # assigned but never used
docs_col = db.collection("documents")
```

The variable `documents` is immediately shadowed by `docs_col` and never read. Remove it.

---

## Not Issues

- **`x_internal_secret` header binding**: FastAPI converts `x_internal_secret` parameter name to `X-Internal-Secret` header automatically. Correct.
- **Batched embedding with empty chunk list**: `extract_text` + `chunk_text` raises `ValueError` before `embed_batch` is called. Correct guard.
- **`delete_chunks` before `store_chunks`**: Re-ingest correctly removes stale chunks before inserting new ones. Correct ordering.
- **AQL `APPROX_NEAR_COSINE`**: ArangoDB 3.12 vector search function. Requires HNSW vector index to be present on the collection (ensured at startup via `ensure_vector_index`).

---

## Patch

1. `rag-server/app/main.py` — fail-closed `_check_secret`: raise `SystemExit` at startup if `INTERNAL_SECRET` is empty.
2. `rag-server/app/main.py` — remove dead `documents` variable in `POST /ingest`.
