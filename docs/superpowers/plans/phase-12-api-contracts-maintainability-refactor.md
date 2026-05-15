# Phase 12 - API Contracts and Maintainability Refactor

## Goal

Turn the backend from a set of capable controllers into a clearer application surface: shared ownership checks, typed service contracts, centralized statuses, consistent errors, and safer logs.

## Scope

- Add reusable current-user and ownership services for notebooks, sessions, and sources.
- Extract chat-session orchestration concerns into services where they already have a natural boundary:
  - message persistence
  - AI streaming proxy
  - session-state projection
  - request logging helpers
- Replace raw JSON strings from `RagClient` with typed DTOs.
- Centralize status constants used by sources, ingestion, chat requests, and tasks.
- Normalize API errors with a shared envelope and correlation id.
- Add correlation-id middleware, propagate it to AI/RAG calls, redact sensitive request log payloads, and add a retention setting for logs.

## Implementation Steps

1. Add shared primitives:
   - `CurrentUserAccessor`
   - `OwnershipService`
   - `ApiErrors`
   - status constants
   - correlation-id middleware
2. Refactor BE controllers to use shared ownership/error helpers.
3. Introduce typed RAG DTOs and update search/experiment controllers.
4. Extract the most reusable chat responsibilities from `ChatSessionsController` into services without changing API behavior.
5. Add request-log redaction and retention handling.
6. Update tests, roadmap, and review notes.

## Non-goals

- No public API redesign beyond normalized errors.
- No full rewrite of chat orchestration into command handlers yet.
- No distributed tracing backend yet; this phase establishes correlation plumbing.
