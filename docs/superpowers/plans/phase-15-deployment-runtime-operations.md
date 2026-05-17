# Phase 15 — Deployment and Runtime Operations

**Branch:** `phase-15-runtime-operations`  
**Goal:** Add a practical production baseline: explicit production compose usage, backup/restore guidance, liveness/readiness split, lightweight operational metrics, and dependency-maintenance policy.

---

## Current gaps

| Surface | Gap |
|---|---|
| Compose | Base file defaults to development-friendly behavior; no explicit production overlay |
| Operations docs | No backup/restore runbook for MySQL, ArangoDB, or uploads |
| Health | `/health` mixes liveness and dependency checks inconsistently across services |
| Metrics | Request logs exist, but there is no simple operational endpoint for queue depth / request counters / dependency-facing latency summaries |
| Maintenance | No documented dependency update policy |

---

## Implementation steps

1. Add `docker-compose.prod.yml` that forces production environment, removes dev defaults, and documents persistent volume expectations without exposing databases.
2. Add operator docs:
   - deployment guide
   - backup/restore runbook
   - dependency update policy
3. Split health endpoints:
   - `/health` = liveness only
   - `/ready` = dependency readiness
4. Add lightweight `/metrics` JSON endpoints:
   - BE: request-log aggregates, ingestion queue depth, ingestion durations
   - AI: in-memory request counters and LLM request counters
   - RAG: in-memory request counters and retrieval counters
5. Point container healthchecks at `/ready` where dependency readiness matters.
6. Add focused tests where practical and update ROADMAP2/review docs.
