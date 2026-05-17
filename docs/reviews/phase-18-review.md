# Phase 18 Review — Retrieval Benchmarks and A/B Comparison

## Summary

Phase 18 completes the first useful Lab feedback loop: retrieval versions can now coexist, datasets provide reusable notebook-local inputs, comparison runs persist immutable snapshots, and `/lab/retrieval-bench` lets the owner inspect real A/B evidence instead of relying on memory.

The most important architectural correction in this phase is not the UI. It is the retrieval-version isolation seam: product search is now pinned to the active retrieval version, while Lab search can name any retained version explicitly.

## Findings addressed during implementation

### 1. Reindex success originally destroyed the older payload needed for A/B

Phase 17 deleted prior chunks immediately after a successful rebuild. That was safe for cutover, but incompatible with Phase 18's purpose because the old version vanished before comparison could happen.

**Resolution:** successful reindex now retains prior chunks. A new explicit Lab prune endpoint deletes payloads only for inactive retrieval versions, keeping cleanup intentional rather than implicit.

### 2. Product search needed active-version pinning once multiple payloads coexist

Before Phase 18, BE resolved the active version's defaults but did not send the active `retrieval_version_id` to RAG. Once multiple payloads remain indexed, this would let ordinary product search mix chunks from several versions.

**Resolution:** vector, BM25, hybrid, and benchmark search now accept retrieval-version scope; normal product search forwards the notebook's active version id.

## Security

- Positive: dataset and bench APIs remain behind `DevAdminOnly`.
- Positive: every Lab query is constrained by notebook ownership and current user id.
- Positive: cross-notebook version comparison is rejected.
- Positive: active payloads cannot be pruned through the new cleanup endpoint.

## Performance

- Dataset runs are synchronous in the first release, with an explicit 50-query cap and at most three modes. That is appropriate for the current personal-lab scale.
- Result snapshots store short text previews instead of full chunk bodies, keeping historical runs compact while preserving enough evidence for inspection.
- Future pressure point: larger datasets should move to durable background jobs rather than stretching request lifetimes.

## Maintainability

- Positive: metrics are computed from stored ordered snapshots, so formulas can evolve without rewriting historical runs.
- Positive: evaluation entities keep MySQL as the experiment control plane while Arango remains the retrieval payload plane.
- Residual concern: `AppDbContextModelSnapshot.cs` is already stale relative to the hand-authored migrations used in recent phases. Runtime behavior is currently correct because migrations are explicit, but future generated migrations could be misleading until the snapshot is regenerated or the migration workflow is standardized.

## Logic

- The first-release metrics are intentionally descriptive, not evaluative: overlap and rank movement show difference, but do not claim quality. That boundary is correct; Phase 19 is where human labels and judge evaluation should enter.
- Keeping prune explicit is the right lifecycle choice for a Lab. Promotion means "make live," not necessarily "forget the baseline."

## Verdict

Phase 18 is functionally coherent and mergeable after final roadmap bookkeeping. The one remaining process-level improvement is to normalize EF snapshot maintenance before future schema-heavy phases make that drift harder to unwind.
