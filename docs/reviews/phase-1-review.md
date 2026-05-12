# Phase 1 Code Review

Reviewer: Claude Code (claude-sonnet-4-6)
Date: 2026-05-12
Scope: Phase 1 auth implementation — BE server (.NET 8 Minimal API) + React frontend

---

## Security

### CRITICAL

**SEC-01 — Refresh endpoint accepts any valid cookie and returns a token for the first DB user**
File: `be-server/BeServer/Auth/AuthController.cs`, lines 27–39

`/api/auth/refresh` reads the `refresh_token` cookie but never validates its value against anything stored in the database. Any client that possesses *any* non-empty string in the `refresh_token` cookie (including a manually crafted one) will receive a fresh access token for `db.Users.FirstOrDefaultAsync()`. This completely bypasses authentication intent. Until the Phase 1-patch `refresh_tokens` table exists, the endpoint should at minimum reject the request rather than silently issue real tokens. A temporary acceptable fix is to return `501 Not Implemented` or require a full re-login on expiry; the current code gives attackers a perpetual access token via any forge-able cookie value.

**SEC-02 — Refresh cookie `Secure = false` allows transmission over plain HTTP**
File: `be-server/BeServer/Auth/AuthController.cs`, line 52

`Secure = false` means the httpOnly cookie will be sent by the browser over unencrypted connections. Even in development this creates a risk of the cookie leaking if someone hits the non-TLS port accidentally. The fix is `Secure = true` when the app is not in a known-dev environment, or at minimum document that TLS termination at the reverse proxy must be enforced before this value is acceptable.

**SEC-03 — Seed admin credentials never reach the container — defaults ship to production**
Files: `be-server/BeServer/Program.cs` lines 75–76, `docker-compose.yml` lines 49–71

`ADMIN_USERNAME` and `ADMIN_PASSWORD` are defined in `.env.template` but are **never passed** to the `be-server` service in `docker-compose.yml`. The seeder therefore always falls back to `admin` / `changeme`. Any deployment that does `docker compose up` without manually injecting these variables via another mechanism will start with a well-known admin account. Add the two variables to the `be-server.environment` block in `docker-compose.yml`.

### HIGH

**SEC-04 — No brute-force protection on `/api/auth/login`**
File: `be-server/BeServer/Auth/AuthController.cs`, lines 14–24

There is no rate limiting, account lockout, or exponential back-off on the login endpoint. BCrypt helps slow individual attempts, but an attacker can fire parallel requests. This is particularly acute because this is a single-user system — there is exactly one account to target. At minimum, apply ASP.NET Core's `Microsoft.AspNetCore.RateLimiting` middleware with a sliding window on the login route.

**SEC-05 — `JWT_SECRET` minimum entropy is not enforced at runtime**
Files: `be-server/BeServer/Auth/JwtService.cs` line 11, `be-server/BeServer/Program.cs` line 23

Both locations accept any non-null string. The `.env.template` comments say "min 32 chars" but nothing enforces it. A short secret makes HMAC-SHA256 tokens susceptible to brute-force key recovery. Add a startup guard: `if (_secret.Length < 32) throw new InvalidOperationException(...)`.

**SEC-06 — `ServerVersion.AutoDetect` makes a live DB connection at startup with no timeout**
File: `be-server/BeServer/Program.cs`, line 20

`ServerVersion.AutoDetect(connStr)` opens a real connection during `AddDbContext` registration — before `db.Database.Migrate()` is even called. If MySQL is slow to start (race with Docker healthcheck), this can throw an unhandled exception that leaks the full connection string including `DB_PASSWORD` into the default ASP.NET exception page (which is enabled in Development). Replace with a pinned `ServerVersion` (e.g., `new MySqlServerVersion(new Version(8, 0, 0))`).

**SEC-07 — Logout does not invalidate the refresh token server-side**
File: `be-server/BeServer/Auth/AuthController.cs`, lines 42–47

`Logout()` deletes the cookie on the response, but since no refresh token is stored server-side, a copy of the cookie value captured before logout (e.g., in network logs, a proxy, or another tab) can still be used to get a new access token. Until the token table is added, this is an inherent limitation, but it must be tracked as a known gap — add a TODO comment and document it explicitly.

### MEDIUM

**SEC-08 — JWT decode in the frontend uses `atob` without input validation**
File: `frontend/src/contexts/AuthContext.tsx`, line 33

```ts
const payload = JSON.parse(atob(data.accessToken.split('.')[1]))
```

If the token is malformed, `split('.')[1]` is `undefined`, `atob(undefined)` throws, and the catch block silently returns `false`. The real risk is that this pattern is often copy-pasted and extended; future callers may not wrap it. Introduce a typed `decodeJwtPayload(token: string): JwtPayload | null` utility function with proper guards.

**SEC-09 — `ProtectedRoute` only checks token presence, not expiry**
File: `frontend/src/components/ProtectedRoute.tsx`, lines 5–6

The in-memory `accessToken` is truthy for the full 15-minute lifetime regardless of whether the token has actually expired (e.g., if the user's clock is ahead). The UI will appear authenticated but API calls will return 401. For a better UX and defence-in-depth, parse the `exp` claim in `AuthContext` and treat an expired token the same as null.

**SEC-10 — `SameSite = Strict` blocks refresh cookie on cross-origin navigations from external links**
File: `be-server/BeServer/Auth/AuthController.cs`, line 54

`SameSite.Strict` is correct for CSRF protection but means that if a user opens the app from a link in an email or Slack, the browser will not send the refresh cookie on the initial navigation, forcing a re-login. For a RAG tool this is minor UX friction, but the intent should be documented. `SameSite.Lax` is the common tradeoff for SPAs that use httpOnly cookies for refresh tokens.

### LOW

**SEC-11 — Error response on login leaks timing differences**
File: `be-server/BeServer/Auth/AuthController.cs`, lines 17–19

`BCrypt.Verify` is called only when the user is found. If the username does not exist, the response is returned immediately without the BCrypt delay. A timing-aware attacker can enumerate valid usernames by measuring response latency. Fix by always calling `BCrypt.Verify` against a dummy hash when the user is not found.

---

## Performance

**PERF-01 — `ServerVersion.AutoDetect` blocks the DI registration thread**
File: `be-server/BeServer/Program.cs`, line 20
(Also flagged as SEC-06.) This is a synchronous network call during startup. Pinning the version is the right fix for both security and performance.

**PERF-02 — BCrypt work factor is at its library default (10)**
File: `be-server/BeServer/Program.cs`, line 83; `be-server/BeServer/Auth/AuthController.cs`, line 18

The BCrypt.Net-Next default work factor is 11. This is not a bug, but the value is implicit. For a system that will eventually serve multiple users and where login latency matters, the work factor should be explicit in a configuration constant so it can be tuned without code changes (e.g., `BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12)`).

**PERF-03 — `db.Users.FirstOrDefaultAsync()` in `/refresh` performs a full table scan**
File: `be-server/BeServer/Auth/AuthController.cs`, line 34

MySQL has no `LIMIT 1` pushed down from `FirstOrDefaultAsync` unless EF Core generates it — it does, but the intent of "give me any user" with no predicate is semantically fragile. When the refresh token table is added this query will be replaced, but as written it is a smell that should not outlive Phase 1.

**PERF-04 — `new JwtSecurityTokenHandler()` allocated per request**
File: `be-server/BeServer/Auth/JwtService.cs`, lines 41, 15 (implicit in `GenerateAccessToken`)

`JwtSecurityTokenHandler` is thread-safe and expensive to construct. It should be a static readonly field or a lazily-initialized singleton on `JwtService`, not a new allocation per call.

---

## Maintainability

**MAINT-01 — `LoginRequest` record is in the controller file**
File: `be-server/BeServer/Auth/AuthController.cs`, line 59

Placing the DTO record at the bottom of the controller file is unconventional and makes it harder to find when adding validation attributes or Swagger annotations later. Move to a dedicated `AuthRequests.cs` or `Dtos/` folder.

**MAINT-02 — Issuer and audience strings are hardcoded in two places**
Files: `be-server/BeServer/Auth/JwtService.cs` lines 27–28, 46–47; `be-server/BeServer/Program.cs` lines 32–34

The strings `"rag-sys"` and `"rag-sys-frontend"` appear in both `JwtService` and `Program.cs`. If one is updated without the other, validation will silently fail. Define them as `internal const string` on a static `JwtConstants` class and reference it in both places.

**MAINT-03 — `User.UpdatedAt` is never updated**
File: `be-server/BeServer/Data/Entities/User.cs`, line 9

`UpdatedAt` is set to `DateTime.UtcNow` at construction and never changed. EF Core will not auto-update it. Either override `SaveChangesAsync` in `AppDbContext` to set `UpdatedAt` on modified entities, or remove the column until it is needed to avoid misleading audit data.

**MAINT-04 — `useAuth` re-export layer adds indirection with no benefit yet**
File: `frontend/src/hooks/useAuth.ts`

The file is a single re-export line: `export { useAuthContext as useAuth } from '../contexts/AuthContext'`. This is fine as an alias layer for future abstraction, but it increases import-resolution hops. Acceptable as-is; only note this if the team adds more hooks here.

**MAINT-05 — `AppDbContext.Users` is not initialized with `null!` convention**
File: `be-server/BeServer/Data/AppDbContext.cs`, line 8

`public DbSet<User> Users { get; set; }` will produce a nullable-reference-type warning in strict mode because the property setter is `set` (not `init`) and EF Core sets it via reflection. The idiomatic EF Core 8 pattern is `public DbSet<User> Users { get; set; } = null!;`.

**MAINT-06 — `AuthController` and `JwtService` share no interface contract**
File: `be-server/BeServer/Auth/JwtService.cs`

`JwtService` is registered as a concrete type and injected directly. Extracting an `IJwtService` interface costs two minutes and makes unit-testing the controller trivial with a mock. Worth doing before Phase 2.

**MAINT-07 — Silent `catch {}` in `AuthContext.refresh` hides all error categories**
File: `frontend/src/contexts/AuthContext.tsx`, line 36

The empty catch swallows network errors, JSON parse errors, and JWT decode errors identically. For observability, at minimum `console.warn` the error in non-production builds, or add an error-boundary-compatible error state.

---

## Logic

**LOGIC-01 — Refresh endpoint issues tokens for `FirstOrDefaultAsync()` with no token validation**
File: `be-server/BeServer/Auth/AuthController.cs`, lines 27–39
(Also flagged as SEC-01.) From a pure logic perspective: the refresh endpoint's contract is to exchange a *valid, unexpired refresh token* for a new access token. The current implementation exchanges *the presence of any non-empty cookie string* for a token. This is a logical inversion — the endpoint does the opposite of what its name implies.

**LOGIC-02 — Logout clears the cookie but does not clear in-memory state consistently**
Files: `frontend/src/contexts/AuthContext.tsx` lines 23–26; `be-server/BeServer/Auth/AuthController.cs` lines 42–47

`logout()` on the frontend calls `POST /api/auth/logout` and then clears `auth` state. However, if the network call fails (e.g., the server is unreachable), the local state is **not** cleared — `return` exits the `useCallback` only after `await`. Because the `await fetch(...)` can throw, the `setAuth({ accessToken: null, ... })` line may never execute. Wrap in try/finally:
```ts
const logout = useCallback(async () => {
  try {
    await fetch('/api/auth/logout', { method: 'POST', credentials: 'include' })
  } finally {
    setAuth({ accessToken: null, username: null })
  }
}, [])
```

**LOGIC-03 — Silent refresh on `App` load returns null, causing a render flash**
File: `frontend/src/App.tsx`, lines 12–17

`if (!checked) return null` prevents the flash of the login page, which is correct. However, if the network request hangs indefinitely (no timeout set on `fetch`), the app renders nothing forever. The `refresh()` fetch in `AuthContext` has no `AbortSignal` or timeout. Add a timeout (e.g., 5 seconds) or an `AbortController` to the refresh call so the app can fall back to the login page gracefully.

**LOGIC-04 — `User.Id` is generated client-side before DB insert with no server-side uniqueness guard beyond the PK**
File: `be-server/BeServer/Data/Entities/User.cs`, line 5

`Guid.NewGuid().ToString()` runs in the C# constructor. EF Core will use this value as the PK. While UUID4 collision probability is negligible, the Id column type is `varchar(36)` which is compared case-insensitively in MySQL's default collation (`utf8mb4_0900_ai_ci`). A UUID like `A1B2...` and `a1b2...` would be treated as identical at the DB level but are distinct strings in C#. Use `CHAR(36)` with a binary collation (`utf8mb4_bin`) or switch to `BINARY(16)` for correctness and index performance.

**LOGIC-05 — `ASPNETCORE_ENVIRONMENT: Development` is hardcoded in docker-compose**
File: `docker-compose.yml`, line 55

Running `Development` environment in docker-compose means ASP.NET's developer exception page (with full stack traces and connection strings) is served to anyone who can reach port 8001. The compose file should parameterize this via `${ASPNETCORE_ENVIRONMENT:-Development}` so it defaults to `Production` in a real deploy.

**LOGIC-06 — `db.Database.Migrate()` runs on every startup without a migration lock**
File: `be-server/BeServer/Program.cs`, lines 56–61

If multiple replicas of `be-server` start simultaneously (possible even now with `docker compose up --scale`), each will call `Migrate()` concurrently. EF Core's migration runner is not atomic across processes. Add a distributed lock (e.g., a MySQL `GET_LOCK`) or run migrations as a separate init container/job.

---

## Summary

### Patch Branch — Must Fix (ordered by priority)

| # | ID | File(s) | Change |
|---|-----|---------|--------|
| 1 | SEC-03 | `docker-compose.yml` | Add `ADMIN_USERNAME` and `ADMIN_PASSWORD` to `be-server.environment` block so the seeder uses the `.env` values instead of hardcoded defaults. |
| 2 | SEC-01 | `AuthController.cs` | Disable or gate the `/refresh` endpoint until the token table is implemented. Return `501` or require re-login. Do not issue tokens for an unvalidated cookie string. |
| 3 | SEC-02 | `AuthController.cs` | Set `Secure = true` on the refresh cookie, or bind it to `IWebHostEnvironment.IsProduction()`. Document the HTTP-only dev exception explicitly. |
| 4 | SEC-06 | `Program.cs` | Replace `ServerVersion.AutoDetect(connStr)` with a pinned `MySqlServerVersion`. |
| 5 | LOGIC-02 | `AuthContext.tsx` | Wrap logout `fetch` in try/finally so `setAuth` is always called. |
| 6 | SEC-05 | `JwtService.cs` / `Program.cs` | Add a startup assertion that `JWT_SECRET.Length >= 32`. |
| 7 | LOGIC-05 | `docker-compose.yml` | Parameterize `ASPNETCORE_ENVIRONMENT` so it is not hardcoded to `Development`. |
| 8 | MAINT-02 | `JwtService.cs` / `Program.cs` | Extract issuer/audience to a shared `JwtConstants` class. |

### Nice-to-Have (not blocking merge)

| ID | Change |
|----|--------|
| SEC-04 | Add rate limiting middleware to `/api/auth/login`. |
| SEC-08 | Extract a typed `decodeJwtPayload` utility on the frontend. |
| SEC-09 | Parse `exp` claim in `AuthContext` and treat expired tokens as null. |
| SEC-10 | Document the `SameSite.Strict` vs. `SameSite.Lax` tradeoff; consider `Lax`. |
| SEC-11 | Always run BCrypt verify (against a dummy hash) to prevent username enumeration via timing. |
| PERF-04 | Make `JwtSecurityTokenHandler` a static readonly field in `JwtService`. |
| MAINT-01 | Move `LoginRequest` record to a dedicated `Dtos/` file. |
| MAINT-03 | Override `SaveChangesAsync` to auto-update `UpdatedAt`, or drop the column until used. |
| MAINT-06 | Extract `IJwtService` interface for testability. |
| MAINT-07 | Add `console.warn` (dev only) in the AuthContext refresh catch block. |
| LOGIC-03 | Add an `AbortController` / timeout to the silent refresh fetch in `AuthContext`. |
| LOGIC-04 | Switch `Users.Id` column to `CHAR(36) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin`. |
| LOGIC-06 | Guard `db.Database.Migrate()` with a distributed lock or move to an init container. |
| PERF-02 | Make BCrypt work factor an explicit, configurable constant. |
