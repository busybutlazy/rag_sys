# Phase 16 Review — Lab Foundations and Versioned Retrieval

## Summary

Phase 16 establishes the right control-plane seam: retrieval presets live globally, notebooks own immutable local versions, product flows carry retrieval lineage, and `/lab` begins behind a backend capability gate. The architecture is coherent with ROADMAP3 and leaves rebuild orchestration cleanly for Phase 17.

## Findings

### 1. Existing bootstrap admins would not become `dev_admin` after migration

`SeedAdminUser` only sets `IsDevAdmin = true` when it creates a brand-new user. Existing installations already containing the configured bootstrap username would migrate with the new column defaulting to `false`, leaving the owner unable to enter Lab despite being the intended privileged account.

**Risk:** functional lockout on upgraded environments.  
**Fix:** make the seed path idempotently promote the configured bootstrap admin to `IsDevAdmin = true` when it already exists.

### 2. Add an auth regression test around the new JWT claim

The new policy depends on the `dev_admin` claim emitted by `JwtService`. Without a direct test, a later token refactor could silently break Lab access while ordinary auth tests still pass.

**Risk:** authorization regression hidden behind otherwise-valid login tokens.  
**Fix:** add a focused test asserting the claim is present for privileged users.

## Notes

- Keeping indexed payload preservation out of this phase is the right boundary; the UI already names the temporary split between active config and indexed payload.
- The initial frontend is intentionally narrow. It is sufficient for Phase 16 because the deeper operational loop belongs to Phase 17.
