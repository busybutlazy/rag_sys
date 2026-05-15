# Internal Secret Rotation

Phase 10 separates internal trust boundaries:

- `RAG_INTERNAL_SECRET` protects calls into `rag-server`
- `AI_INTERNAL_SECRET` protects BE↔AI internal calls and AI→BE request logging

## Rotation procedure

1. Generate replacement secrets with at least 32 characters each.
2. Deploy the new values to all services that use that trust boundary in one rollout.
3. Restart the affected services together so callers and receivers agree.
4. Verify:
   - BE can enqueue and complete ingestion jobs
   - AI chat updates session state
   - AI request logs still reach BE
5. Remove the old values from secret storage after verification.

`INTERNAL_SECRET` remains a temporary compatibility fallback for local development only. Before multi-host deployment, replace raw shared secrets with service JWTs or mTLS so identity, expiry, and rotation become first-class protocol properties rather than deployment convention.
