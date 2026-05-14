# Phase 8 Code Review - Auth and Session Hardening

**Branch:** phase-8-auth-session-hardening  
**Date:** 2026-05-14  
**Reviewer:** Codex

## Summary

Phase 8 implements the full refresh token strategy from `ROADMAP2.md`. Login now stores a hashed refresh token, `/api/auth/refresh` rotates refresh tokens, logout revokes the current token, and reuse of a revoked token revokes the active token family. BE startup now rejects known development defaults outside `Development`.

## Findings

No patch-required findings.

## Security Notes

- Refresh tokens are generated with `RandomNumberGenerator`, stored only as SHA-256 hashes, and sent only through an `HttpOnly`, `SameSite=Strict` cookie.
- Cookie `Secure` remains environment-aware: disabled in local `Development`, enabled outside development.
- Token reuse detection is fail-closed for known revoked tokens and revokes the remaining active family.
- Raw refresh tokens are not logged or returned in JSON payloads.
- Production-like environments reject default `JWT_SECRET`, `INTERNAL_SECRET`, `ADMIN_PASSWORD`, and `DB_PASSWORD` values.

## Maintainability Notes

- Auth tests are controller-level rather than full `WebApplicationFactory` tests. This avoids coupling test startup to the current MySQL migration path while still covering the refresh token behavior directly.
- The BE Dockerfile now restores the runtime project directly instead of restoring the full solution, so test projects do not affect production image restore/publish.

## Verification

- `docker run --rm -v /home/jett/Documents/rag_sys/be-server:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test BeServer.Tests/BeServer.Tests.csproj --logger "console;verbosity=normal"`
  - Passed: 5
- `docker compose build be-server`
  - Passed

## Residual Risk

- Full middleware/integration tests for auth-protected endpoints should be added in Phase 11 when broader BE test infrastructure is expanded.
- Refresh token cleanup for expired/revoked historical rows is not implemented; this is operational cleanup, not required for correctness of Phase 8.
