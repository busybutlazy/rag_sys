# Product Positioning

**Status:** adopted 2026-06-17. Supersedes the implicit "broad personal knowledge product" framing for prioritization purposes. Does not replace `ROADMAP.md` / `ROADMAP2.md` / `ROADMAP3.md` — it tells you which parts of `ROADMAP3` to fund next and which to leave alone.

---

## Why this document exists

`rag_sys` grew breadth before depth: notebooks, chat, sessions, notes, agent loop, ingestion pipeline, and now a versioned retrieval Lab. Each piece works, but the project doesn't yet answer a single sharp question well enough to be a portfolio centerpiece. Phase 18 (`phase-18-retrieval-benchmarks-ab-comparison`) made retrieval-version A/B comparison real — that capability is the right foundation to specialize on, rather than continuing to widen the product.

This doc fixes the story going forward: **`rag_sys` exists to demonstrate hands-on Graph RAG design and evaluation skill**, using the personal-knowledge-base product as the vehicle, not the point.

---

## Audience

Primarily: a technical reviewer (hiring manager, peer engineer) deciding whether the author can design, build, and *prove* a retrieval system — not just call an embedding API. Secondarily: the author's own future self maintaining a working personal knowledge tool.

The artifact that should exist at the end of this track: a working notebook product where the owner can flip a flag on a notebook, get an entity/fact graph layer built during ingestion, and use the existing `/lab/retrieval-bench` to show **measured** evidence that graph-augmented retrieval beats plain hybrid search (or honestly, that it doesn't, on a given corpus) — not a slide deck claim.

---

## Decision: freeze breadth, fund depth

### Frozen (no new scope, bug fixes only)

- Notebook chat / sessions / notes / agent loop surface (`ai-server/agent.py`, `ChatPanel.tsx`, `NotebookNotesPanel.tsx`).
- `ROADMAP3` Phase 20 (Agent Packages, Prompt Versions, Playground) — was Phase 20 in the prior numbering, stays parked behind the graph track.
- Any new ingestion source types / connectors. The corpus you already have is enough to run experiments on.

### Active (where the next few phases go)

- `rag-server` becomes the centerpiece. Its retrieval logic, not its breadth of supported file types, is the thing being judged.
- `/lab/retrieval-bench` (Phase 18) gets a third comparable axis: graph-augmented retrieval, sitting alongside the existing vector / BM25 / hybrid modes, using the *same* comparison engine (overlap@k, rank deltas, latency) that Phase 18 already built.
- `ROADMAP3` Phase 21 ("GraphRAG Readiness"), which already existed as a vague placeholder anticipating this, is promoted and detailed as the next phase. See `docs/superpowers/plans/phase-19-graphrag-foundations.md`.

### Explicitly deferred, not cancelled

- Phase 19 "Human Relevance Labels and Judge Evaluation" and Phase 20 "Agent Packages" (prior numbering) still matter, but only *after* the graph track has something real to evaluate. Renumbered to Phase 20 and Phase 21 in `ROADMAP3.md`.

---

## Reference project: `/home/jett/Documents/graph`

`graph` (`e_learning_aihub_graph`) is a working multi-tenant Graph-RAG ingestion + query server with an 11-collection ArangoDB schema, LLM-confined extraction stages, and a mode-selectable query pipeline (`auto|vector_only|hybrid_fast|graph_heavy`). It is a **design reference**, not a code source to vendor in:

- Its multi-tenant "one Arango DB per tenant" model doesn't apply here — `rag_sys` already isolates per-notebook/per-user via document fields (`notebook_id`, `user_id`, `retrieval_version_id`) in a single DB, and that convention should be kept for the graph collections too.
- Its service-JWT auth layer is unneeded — `rag_sys` already has a working internal-secret boundary (`RAG_INTERNAL_SECRET` / `AI_INTERNAL_SECRET`).
- Its in-memory ingestion job store is a known gap in `graph` itself — `rag_sys` already has a durable `IngestionJob` + worker model that is strictly better; reuse that.
- What's worth taking: the *shape* of a deterministic ingestion pipeline with LLM usage confined to a single extraction stage, the entity/mention/fact vertex split, the literal-rule alias resolver as a first cut, and the query-mode/budget abstraction for making graph vs. non-graph retrieval directly comparable.

See `docs/superpowers/plans/phase-19-graphrag-foundations.md` for the concrete plan.

---

## How to tell if this is working

- A notebook can opt into graph extraction at ingestion time without affecting notebooks that don't.
- `/lab/retrieval-bench` can run the same query against a graph-enabled retrieval version and a non-graph version and show a real, reproducible delta — not just "looks more sophisticated."
- The README / portfolio narrative can say, with a working link, "I built a hybrid vector+graph retrieval system and benchmarked the graph contribution on my own data" — and that statement is checkable by reading the code and re-running the benchmark.
