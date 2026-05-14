# Phase 9 Review - Reliable Ingestion Jobs

## Findings

No patch-required findings.

## Review Notes

- Security: upload authorization remains notebook/user scoped before source and job creation. Internal RAG calls still use the existing `X-Internal-Secret`; deeper internal API hardening remains Phase 10 scope.
- Reliability: source upload now persists a queued ingestion job instead of starting request-scoped background work. File-write failures are visible as failed source/job records.
- Maintainability: ingestion state is centralized through `IngestionJobStatuses` and `IngestionJobTypes`; the worker owns RAG retry transitions.
- Operations: RAG delete failures now create a failed `delete_cleanup` job record so cleanup debt is inspectable after source deletion.

## Residual Risk

- The hosted worker is suitable for the current single BE container deployment. If BE is horizontally scaled later, job claiming should be strengthened with database-level locking or compare-and-swap updates.
- Cleanup debt is recorded but not automatically retried in this phase.
- Parser and MIME hardening remains intentionally deferred to Phase 10.

## Verification

- `docker run --rm -v /home/jett/Documents/rag_sys/be-server:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test BeServer.Tests/BeServer.Tests.csproj --logger "console;verbosity=normal"`
- `npm run build` in `frontend/`
- `docker compose build be-server frontend`
