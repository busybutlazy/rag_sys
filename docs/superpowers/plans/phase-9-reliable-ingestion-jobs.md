# Phase 9 - Reliable Ingestion Jobs

## Goal

Replace source-upload fire-and-forget ingestion with durable, observable jobs that survive normal BE request lifecycles and expose useful status to the UI.

## Scope

- Add a SQL-backed `IngestionJob` entity and EF migration.
- Create ingestion jobs during source upload after the file is written successfully.
- Add a hosted BE worker that claims queued/retrying jobs, calls RAG `/ingest`, retries transient failures with backoff, and updates `sources.status`.
- Expose current job state through source list/upload responses and an optional per-source job endpoint.
- Record cleanup debt when RAG delete fails instead of only writing to stderr.
- Update the frontend source panel to show queued/running/succeeded/failed/cancelled state clearly.

## Non-goals

- No new external worker service or queue broker in this phase.
- No RAG ingestion API contract change unless needed for error handling.
- No parser or MIME hardening; that is Phase 10.

## Implementation Steps

1. Add BE persistence:
   - `Data/Entities/IngestionJob.cs`
   - `DbSet<IngestionJob>`
   - indexes for job picking and source lookup
   - migration `Phase9IngestionJobs`
2. Add job constants and worker:
   - status constants for `queued`, `running`, `succeeded`, `failed`, `retrying`, `cancelled`
   - `IngestionJobWorker : BackgroundService`
   - claim jobs with attempt limits and backoff
   - update `Source.Status` from job lifecycle
3. Update source upload:
   - persist source before file path assignment as today
   - if file write fails, mark source failed and create a failed job record
   - if file write succeeds, create a queued job
   - remove `Task.Run` ingestion
4. Update source delete:
   - cancel active ingestion jobs for the source
   - on RAG delete failure, create a failed cleanup job/debt record and keep logging structured context
5. Add API visibility:
   - source list includes current ingestion job fields
   - upload returns source plus job status
   - `GET /api/notebooks/{notebookId}/sources/{sourceId}/ingestion-job`
6. Update frontend:
   - source type includes ingestion job metadata
   - source list renders concise status labels and last error when present
7. Tests and verification:
   - add focused BE tests for upload job creation and worker success/failure behavior where practical
   - run BE tests in Docker SDK container
   - run frontend build
   - run `docker compose build be-server frontend`

## Risks

- Concurrent workers can double-process a job without careful claim conditions. Keep one BE hosted worker by default and use row update checks to avoid duplicate running claims.
- File write failures happen after the source row is created. Preserve visibility by marking the source/job as failed rather than leaving an orphaned `uploaded` source.
- Delete cleanup debt should not block users from deleting a source, but it must remain inspectable for operational follow-up.
