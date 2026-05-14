# Phase 8 Plan - Auth and Session Hardening

Date: 2026-05-14
Branch: `phase-8-auth-session-hardening`

## Goal

Make auth session behavior explicit, correct, and secure. The chosen strategy is Option A from `ROADMAP2.md`: implement refresh tokens fully, because the frontend already contains refresh behavior and the BE currently exposes a stubbed endpoint.

## Scope

1. Add durable refresh token storage.
2. Implement refresh token rotation on every refresh.
3. Revoke refresh tokens on logout.
4. Detect refresh token reuse and revoke the active family.
5. Add production startup guards for default secrets/passwords.
6. Add focused BE auth tests.
7. Update docs and roadmap state.

## Implementation Tasks

### 1. Data Model

- Add `RefreshToken` entity.
- Add `RefreshTokens` DbSet and EF model configuration.
- Add migration with:
  - `Id`
  - `UserId`
  - `TokenHash`
  - `FamilyId`
  - `ExpiresAt`
  - `RevokedAt`
  - `ReplacedByTokenId`
  - `CreatedAt`
  - `CreatedByIp`
  - `RevokedByIp`
  - indexes for user, hash, family, and expiry.

### 2. Auth Flow

- On login:
  - Generate a cryptographically random refresh token.
  - Hash it before storage.
  - Store it with a new family id.
  - Set the existing `HttpOnly`, `SameSite=Strict`, secure-outside-development cookie.
- On refresh:
  - Require refresh cookie.
  - Hash incoming token and find matching token.
  - Reject missing, expired, or revoked tokens.
  - If a revoked token is reused, revoke the active token family.
  - Rotate by revoking the current token and issuing a new token in the same family.
  - Return a fresh access token.
- On logout:
  - If a refresh cookie is present, revoke the matching token.
  - Delete the cookie regardless of DB result.

### 3. Startup Guards

- Keep existing JWT minimum length guard.
- In production-like environments, reject known development defaults for:
  - `JWT_SECRET`
  - `INTERNAL_SECRET`
  - `ADMIN_PASSWORD`
  - `DB_PASSWORD`
- Keep local development usable.

### 4. Tests

- Add a BE test project if none exists.
- Cover:
  - successful login
  - invalid login
  - protected endpoint rejects expired access token
  - refresh rotation succeeds and old token cannot be reused
  - logout revokes refresh token

### 5. Documentation

- Mark Phase 8 tasks complete in `ROADMAP2.md`.
- Add concise learning notes after implementation/review.

## Verification

- `dotnet test`
- `dotnet build be-server/BeServer/BeServer.csproj`
- Frontend type/build only if frontend auth code changes.

## Risks

- Cookie testing can be brittle; prefer integration tests using `WebApplicationFactory`.
- EF migrations should be hand-checked against existing manual migrations and snapshot state.
- Refresh token reuse detection must fail closed without logging raw tokens.
