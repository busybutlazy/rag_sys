# Phase 5 Code Review — BM25 + Hybrid Search

**Branch:** phase-5-hybrid  
**Date:** 2026-05-12  
**Reviewer:** Claude Sonnet 4.6

---

## Summary

Phase 5 adds BM25 full-text search (ArangoSearch view), hybrid RRF fusion, a benchmark endpoint, and a frontend search panel. One carry-over bug from Codex's Phase 4 review is fixed here. No new security issues found.

---

## Findings

### BUG-01 — nginx strips `/api/` prefix from all BE requests (PATCH REQUIRED — carry-over from Codex review)

**File:** `frontend/nginx.conf:7`

```nginx
location /api/ {
    proxy_pass http://be-server:8001/;   # trailing slash strips /api/
}
```

Nginx's rule: when `proxy_pass` has a URI suffix (even just `/`) and `location` has a matching suffix, nginx strips the location prefix before forwarding. So:

```
Browser:  GET /api/auth/login
Nginx:    GET http://be-server:8001/auth/login   (prefix stripped)
.NET:     [Route("api/auth")]  → expects /api/auth/login → 404
```

Every BE controller (`api/auth`, `api/notebooks`, `api/notebooks/{id}/sources`, `api/notebooks/{id}/search`) is unreachable from the browser under Docker. In dev with Vite's proxy this is masked.

**Fix:** Remove the trailing slash from `proxy_pass`.

```nginx
location /api/ {
    proxy_pass http://be-server:8001;
}
```

---

### PERF-01 — Benchmark endpoint makes 2× the search calls (NOT patched — acceptable)

**File:** `rag-server/app/main.py:130-140`

`search_benchmark` calls `search_vector`, `search_bm25`, and `search_hybrid`. But `search_hybrid` internally calls both `search_vector` and `search_bm25` again (with a larger `fetch_k`). The benchmark endpoint makes 4 DB round-trips instead of 2.

For an experiment/comparison tool running infrequently, this is acceptable. Fixing it would require exposing a `_merge_rrf` helper and threading pre-fetched results into hybrid — adding complexity for no user-facing gain. Leaving as-is.

---

## Not Issues

- **`ANALYZER(doc.text IN TOKENS(@query, 'text_en'), 'text_en')`**: Valid AQL for ArangoSearch BM25 — `TOKENS()` tokenizes the query and `IN` checks if any token matches the indexed text field.
- **`chunk_index: {}` in view properties**: Harmless — ArangoDB accepts empty field config for non-text fields. Does not affect search correctness.
- **`topK` camelCase in frontend URL**: ASP.NET Core query binding is case-insensitive; `topK=5` correctly binds to `[FromQuery] int topK`.
- **RRF `k=60`**: Standard RRF constant (Cormack et al., 2009). Appropriate for small-to-medium result sets.
- **`alpha=0.5` default**: Equal weighting between vector and BM25 is a reasonable starting point; exposed as query param for experimentation.

---

## Patch

1. `frontend/nginx.conf` — remove trailing slash from `proxy_pass` for `/api/` location.
