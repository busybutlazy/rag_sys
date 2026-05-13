# Phase 7 Plan — Polish & Experimentation Tooling

## Goal

Add a usable experiment workflow for comparing RAG search configurations, store results in ArangoDB, expose a frontend dashboard, and harden the service baseline for multi-user and production readiness.

## Scope

- RAG server owns experiment execution and result persistence in ArangoDB.
- BE server proxies experiment endpoints after JWT + notebook ownership checks.
- Frontend adds an experiment dashboard inside notebook detail.
- Multi-user readiness focuses on explicit schema constraints and an Arango user-database migration helper script.
- Service hardening includes targeted input validation, rate limiting for sensitive BE endpoints, and JSON logging configuration.

## Implementation Steps

1. Add `experiments` collection creation in `rag-server`.
2. Add experiment models and endpoints:
   - `POST /experiments/run`
   - `GET /experiments?notebook_id=...`
   - `GET /experiments/{id}`
3. Experiment runner:
   - accepts multiple queries and search mode/top_k/alpha config
   - runs vector, BM25, and/or hybrid search
   - stores latency, result count, and returned source refs
4. Add BE proxy endpoints under `api/notebooks/{notebookId}/experiments`.
5. Add frontend `ExperimentPanel` in notebook detail.
6. Add DB/index hardening for `user_id` foreign-key paths where missing.
7. Add Arango migration helper script for future one-db-per-user promotion.
8. Add rate limiting to auth and upload-heavy BE routes.
9. Add JSON logging configuration for .NET and Python services.
10. Build-verify affected services.

## Review Checklist

- Experiments are notebook-scoped and internal-secret guarded at RAG server.
- BE endpoints verify notebook ownership before proxying.
- Stored experiment payloads avoid full chunk text bloat.
- Frontend keeps controls compact and operational, not a marketing page.
- Rate limits do not block normal local workflows.
