# Phase 2 Code Review

Reviewed files: BE controllers (Notebooks, Sources, Notes), RagClient, Entity models,
AppDbContext, Phase2Content migration, rag-server main.py + models.py + db.py,
frontend api.ts, NotebooksPage.tsx, NotebookDetailPage.tsx.

---

## Security

### SEC-01 — HIGH — Path traversal via user-supplied filename
**File:** `be-server/BeServer/Content/SourcesController.cs` line 48

```csharp
var filePath = Path.Combine(dir, $"{source.Id}_{Path.GetFileName(file.FileName)}");
```

`file.FileName` comes from the HTTP multipart header and is fully attacker-controlled.
`Path.GetFileName` strips directory separators on Windows but not on Linux/macOS where
forward slashes can survive inside a component. A crafted filename such as
`../../etc/passwd` or a null-byte injection could write outside `UploadDir`.

**Fix:** Sanitise the filename to only alphanumeric characters, dashes, underscores, and
the final extension, or drop the original name entirely and store only
`source.Id + extension_from_mime`. The `source.Id` (a GUID) alone is already unique and
safe; the original name belongs only in the `Title` column.

---

### SEC-02 — HIGH — Fire-and-forget task captures a scoped DbContext
**File:** `be-server/BeServer/Content/SourcesController.cs` lines 57–72

```csharp
_ = Task.Run(async () =>
{
    try
    {
        await rag.IngestAsync(source.Id, filePath, file.ContentType);
        source.Status = "ingested";
        source.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();   // <-- db is the request-scoped context
    }
    ...
});
```

`AppDbContext` is registered as a **scoped** service. The request scope is disposed when
`Upload` returns — which happens before the background task completes. Calling
`db.SaveChangesAsync()` on a disposed context causes `ObjectDisposedException` at best and
silent data corruption or connection-pool exhaustion at worst.

**Fix:** Resolve a **fresh** `IServiceScopeFactory` scope inside the Task.Run body and
retrieve a new `AppDbContext` from that scope.

```csharp
_ = Task.Run(async () =>
{
    await using var scope = scopeFactory.CreateAsyncScope();
    var bgDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var bgSource = await bgDb.Sources.FindAsync(source.Id);
    if (bgSource is null) return;
    try
    {
        await rag.IngestAsync(source.Id, filePath, file.ContentType);
        bgSource.Status = "ingested";
    }
    catch
    {
        bgSource.Status = "error";
    }
    bgSource.UpdatedAt = DateTime.UtcNow;
    await bgDb.SaveChangesAsync();
});
```

Inject `IServiceScopeFactory scopeFactory` via the primary constructor alongside `db`.

---

### SEC-03 — HIGH — MIME type accepted entirely from client header
**File:** `be-server/BeServer/Content/SourcesController.cs` lines 40–43

```csharp
MimeType = file.ContentType,   // attacker-controlled
```

An attacker can upload an executable while claiming it is `application/pdf`.
There is no server-side allowlist of acceptable types, no magic-byte check, and no
restriction on file extension beyond the 50 MB size cap.

**Fix:** Maintain a static allowlist of accepted MIME types (e.g. `application/pdf`,
`text/plain`, `application/vnd.openxmlformats-officedocument.*`). Reject early with
`400 Bad Request` if `file.ContentType` is not on the list. Optionally use a library such
as `MimeDetective` to verify the actual file magic bytes.

---

### SEC-04 — MEDIUM — rag-server /ingest is unauthenticated and network-exposed
**File:** `rag-server/app/main.py` lines 16–37

The `/ingest` endpoint accepts arbitrary `source_id` and `file_path` values with no
authentication token. Any service (or attacker who reaches the Docker network) can
overwrite documents or supply arbitrary paths. The `file_path` value is stored verbatim
and later used by a reader; a crafted path could cause the rag-server to read files
outside the upload volume.

**Fix (short term):** Add a shared-secret header (e.g. `X-Internal-Token`) validated on
every internal call. `RagClient` sends the header; `/ingest` rejects requests that omit
it. Also validate that `file_path` is a child of the known upload directory.

---

### SEC-05 — MEDIUM — UserId claim sourced with null-forgiving operator
**File:** `be-server/BeServer/Content/NotebooksController.cs` lines 15–16
(same pattern in `SourcesController.cs` and `NotesController.cs`)

```csharp
private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
    ?? User.FindFirstValue("sub")!;    // null-forgiving !
```

If neither claim is present in the token (unusual but possible with a misconfigured
issuer), this returns `null` at runtime while telling the compiler it is non-null. All
`WHERE UserId == UserId` queries then silently filter on `null`, potentially matching rows
with a null `UserId` or returning empty results instead of a proper `401`.

**Fix:** Add a guard that returns `Unauthorized()` when the resolved value is null or
empty, or enforce that the claim is required in `TokenValidationParameters`.

---

### SEC-06 — LOW — Default credentials in Program.cs fallback
**File:** `be-server/BeServer/Program.cs` lines 84–85

```csharp
var username = config["ADMIN_USERNAME"] ?? "admin";
var password = config["ADMIN_PASSWORD"] ?? "changeme";
```

If the environment variables are absent, the seed creates an admin account with the
well-known password `changeme`. This is safe in development but dangerous if a staging
or production container is started without the variables set.

**Fix:** Throw `InvalidOperationException` when `ADMIN_PASSWORD` is absent (mirroring the
`JWT_SECRET` guard on line 26), or skip seeding and require an explicit first-run script.

---

### SEC-07 — LOW — Token stored in module-level mutable variable (frontend)
**File:** `frontend/src/lib/api.ts` lines 1–4

```typescript
let _getToken: (() => string | null) | null = null
export function registerTokenGetter(fn: () => string | null) {
  _getToken = fn
}
```

The getter can be re-registered at any time by any module that imports `api.ts`. If a
third-party library (or future code) accidentally calls `registerTokenGetter`, it silently
replaces the auth getter, causing all subsequent requests to either fail or send a wrong
token. This is low-severity in a controlled codebase but is an unusual pattern.

**Fix (nice-to-have):** Guard against double registration with an error, or pass the
token getter via React context / a thin auth hook that is injected at the fetch call site.

---

## Performance

### PERF-01 — MEDIUM — N+1 potential in NotebooksController.Get — unbounded collection loads
**File:** `be-server/BeServer/Content/NotebooksController.cs` lines 40–46

```csharp
Sources = n.Sources.Select(s => new { s.Id, s.Title, s.MimeType, s.Status, s.CreatedAt }),
Notes = n.Notes.Select(nt => new { nt.Id, nt.Title, nt.NoteType, nt.CreatedAt }),
```

The projection runs inside `FirstOrDefaultAsync` so EF Core typically generates a single
query with JOINs. However, there is no `Take` on `Sources` or `Notes`. A notebook with
thousands of sources/notes will return the entire collection in one response. This will
grow unbounded over time.

**Fix:** Add `Take(100)` (or a configurable limit) to each sub-collection, and add
pagination endpoints for sources and notes.

---

### PERF-02 — MEDIUM — Missing composite index on (UserId, NotebookId) for Sources and Notes
**File:** `be-server/BeServer/Data/AppDbContext.cs` lines 54, 70

Both `Sources` and `Notes` have an index only on `NotebookId`. Every `WHERE NotebookId =
? AND UserId = ?` query (used in every authorised lookup) must scan all rows for that
notebook and then filter by `UserId` in MySQL. Adding a composite index
`(NotebookId, UserId)` or `(UserId, NotebookId)` eliminates the secondary filter scan.

**Fix:**
```csharp
e.HasIndex(s => new { s.NotebookId, s.UserId });  // Sources
e.HasIndex(n => new { n.NotebookId, n.UserId });  // Notes
```
Add the corresponding index to the migration.

---

### PERF-03 — LOW — Repeated full-detail reload on every mutation (frontend)
**File:** `frontend/src/pages/NotebookDetailPage.tsx` lines 19, 26–28, 34–36, 40–42

```typescript
const reload = () => apiGet<NotebookDetail>(`/api/notebooks/${id}`).then(setNb)
```

`reload()` is called after every upload, note create, and source delete. Each call
re-fetches the entire notebook detail including all sources and notes. For a notebook with
many items this is wasteful.

**Fix (nice-to-have):** Apply optimistic local state updates after mutations and only
re-fetch on error or mount.

---

### PERF-04 — LOW — ArangoDB connection not validated on startup
**File:** `rag-server/app/db.py` lines 4–16

The `_db` singleton is created lazily on first request. If ArangoDB is not ready, the
first real request fails with an exception rather than a clean startup error. The health
endpoint (`/health`) calls `db.version()`, which helps — but only if health is polled
before the first `/ingest`.

**Fix:** Eagerly call `get_db()` during FastAPI startup (`@app.on_event("startup")`) so
connectivity errors surface immediately and the container fails fast.

---

## Maintainability

### MAINT-01 — MEDIUM — `Status` is a free-form string with no enumeration or validation
**Files:**
- `be-server/BeServer/Data/Entities/Source.cs` line 12
- `be-server/BeServer/Content/SourcesController.cs` lines 43, 63, 69

The values `"uploaded"`, `"ingested"`, and `"error"` are scattered as magic strings
across the entity and controller. A typo in any location creates an undetected bad state
in the database.

**Fix:** Extract a `SourceStatus` static class (or an `enum` serialised as a varchar) and
reference only its constants. Add a `CHECK` constraint in a future migration if MySQL
version allows it.

---

### MAINT-02 — MEDIUM — Fire-and-forget with bare `catch` swallows all errors silently
**File:** `be-server/BeServer/Content/SourcesController.cs` lines 57–72

The `catch` block updates the status to `"error"` but logs nothing. In production there
is no observable signal (no log entry, no metric) when ingest fails, making diagnosis
nearly impossible.

**Fix:** Inject `ILogger<SourcesController>` and call `logger.LogError(ex, ...)` inside
the catch block before updating the status.

---

### MAINT-03 — LOW — `NoteType` is a free-form string with no validation
**File:** `be-server/BeServer/Data/Entities/Note.cs` line 11

`NoteType` defaults to `"human"` but callers can persist any string up to 16 characters.
No enum or allowlist is enforced.

**Fix:** Same approach as MAINT-01 — static constants or enum.

---

### MAINT-04 — LOW — `NotebooksController.Update` returns the full tracked entity
**File:** `be-server/BeServer/Content/NotebooksController.cs` line 59

```csharp
return Ok(nb);   // returns the full EF-tracked Notebook entity
```

This leaks navigation properties and internal fields (e.g. `UserId`) that are not
returned by `List` or `Get`. Using a consistent DTO (or an anonymous projection matching
`Get`'s shape) keeps the API surface predictable.

---

### MAINT-05 — LOW — `IngestRequest.mime_type` is required but rag-server never uses it
**File:** `rag-server/app/models.py` line 7 and `rag-server/app/main.py` lines 25–29

`mime_type` is stored in ArangoDB but is not used for any routing logic. If it will be
used by a future parser, the field is correct. If not, it is dead data. Document the
intent or remove the field to avoid confusion.

---

### MAINT-06 — LOW — `get_db()` global singleton is not thread-safe under async concurrency
**File:** `rag-server/app/db.py` lines 4–16

Under high concurrency two coroutines can both see `_db is None` simultaneously and each
create a client, with only the second assignment surviving. This is unlikely to cause data
loss (both clients are valid) but wastes connections and is a code smell.

**Fix:** Initialise the singleton in the FastAPI startup hook (see PERF-04) rather than
lazily inside a check-then-set pattern.

---

## Logic

### LOGIC-01 — HIGH — Delete source does not notify rag-server to remove the document
**File:** `be-server/BeServer/Content/SourcesController.cs` lines 79–88

```csharp
System.IO.File.Delete(source.FilePath);
db.Sources.Remove(source);
await db.SaveChangesAsync();
```

The file is deleted from the volume and the MySQL row is removed, but the corresponding
document record in ArangoDB is left in place. Future retrieval or search will reference a
source that no longer exists in MySQL, producing dangling results and potential
information leakage if the document's chunks are returned to a different user.

**Fix:** Call a new `RagClient.DeleteAsync(sourceId)` before removing the MySQL row.
Define a `DELETE /documents/{source_id}` endpoint on the rag-server. Wrap the file
deletion, rag-server call, and DB removal in a compensating transaction pattern (or at
minimum perform the DB removal last so MySQL remains the source of truth on partial
failure).

---

### LOGIC-02 — HIGH — File written to disk before DB row is committed; orphan on DB failure
**File:** `be-server/BeServer/Content/SourcesController.cs` lines 46–54

```csharp
await using (var stream = System.IO.File.Create(filePath))
    await file.CopyToAsync(stream);

source.FilePath = filePath;
db.Sources.Add(source);
await db.SaveChangesAsync();   // if this throws, the file is already on disk
```

If `SaveChangesAsync` fails (e.g. duplicate key, DB timeout), the uploaded file remains
on the shared volume with no associated record, leaking disk space indefinitely. There is
no cleanup path.

**Fix:** Write to a temp path first, commit the DB row, then rename to final path. On DB
failure, delete the temp file. Alternatively, accept the orphan risk but add a periodic
janitor job that removes files in the upload volume with no matching `Sources` row.

---

### LOGIC-03 — MEDIUM — `NotebooksController.List` hides archived notebooks but `Get` does not
**File:** `be-server/BeServer/Content/NotebooksController.cs`
- List (line 21): `Where(n => n.UserId == UserId && !n.Archived)`
- Get (line 39): `Where(n => n.Id == id && n.UserId == UserId)` — no `!n.Archived` check

A client that knows the `id` of an archived notebook can still `GET`, `PUT`, or delete it
successfully. `PUT` will also de-archive it implicitly if not careful.

**Fix:** Add `&& !n.Archived` to the `Get`, `Update`, and `Archive` lookups, or treat
archived as a soft-delete that returns `404` for all non-admin access.

---

### LOGIC-04 — MEDIUM — rag-server `/ingest` returns `202 Accepted` but processes synchronously
**File:** `rag-server/app/main.py` line 16

HTTP 202 signals that work has been queued and will be processed asynchronously. The
current handler performs all work synchronously within the request and only then returns.
This is semantically incorrect and will confuse future consumers who implement async
polling based on the 202 status.

**Fix:** Either change the status to `200 OK` (correct for current synchronous behaviour)
or introduce a real background task and a status polling endpoint to honour the 202
contract.

---

### LOGIC-05 — MEDIUM — `SourcesController.List` does not verify the notebook belongs to the user
**File:** `be-server/BeServer/Content/SourcesController.cs` lines 22–27

```csharp
.Where(s => s.NotebookId == notebookId && s.UserId == UserId)
```

The `UserId` check on `Sources` is correct, but there is no check that `notebookId`
itself belongs to `UserId`. A user who guesses another user's notebook ID gets an empty
list (not a `404`), leaking the fact that the notebook exists. The correct response to a
foreign notebook ID is `404` (matching the behaviour of `Get`).

**Fix:** Check notebook ownership before the source query, identically to how `Upload`
and `NotesController.Create` do it.

---

### LOGIC-06 — LOW — `NotebookDetailPage` does not handle upload or note-create errors
**File:** `frontend/src/pages/NotebookDetailPage.tsx` lines 23–28, 31–37

```typescript
async function uploadFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    await apiUpload(`/api/notebooks/${id}/sources`, file)
    reload()
    // no catch — unhandled rejection, silently fails
}
```

Both `uploadFile` and `createNote` lack `try/catch`. A failed upload or note save
produces an unhandled promise rejection with no user-visible feedback.

**Fix:** Wrap both in `try/catch` and set a local error state, matching the pattern used
in `NotebooksPage.tsx`.

---

### LOGIC-07 — LOW — Migration `Down()` drops tables without checking for data
**File:** `be-server/BeServer/Migrations/20260512010000_Phase2Content.cs` lines 155–161

```csharp
protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropTable("ChatSessions");
    migrationBuilder.DropTable("Notes");
    migrationBuilder.DropTable("Sources");
    migrationBuilder.DropTable("Notebooks");
}
```

Rolling back this migration in production silently deletes all user content. This is
standard EF Core behaviour, but in a production context the `Down` method should at
minimum be documented with a prominent warning, and a backup step should be required
before running `database update <prev-migration>`.

---

## Summary

### Patch Branch — Must Fix (ordered by priority)

| # | ID | File(s) | Change |
|---|----|---------|----|
| 1 | SEC-02 | `SourcesController.cs:57–72` | Fix fire-and-forget to use a new `IServiceScope`; current code calls `SaveChangesAsync` on a disposed DbContext |
| 2 | LOGIC-01 | `SourcesController.cs:79–88` | Notify rag-server to delete the document on source delete; prevents dangling ArangoDB records |
| 3 | SEC-01 | `SourcesController.cs:48` | Sanitise or replace the user-supplied filename to prevent path traversal |
| 4 | SEC-03 | `SourcesController.cs:40–43` | Add server-side MIME type allowlist; reject disallowed types before writing to disk |
| 5 | LOGIC-02 | `SourcesController.cs:46–54` | Write file to temp path; only keep on successful DB commit |
| 6 | SEC-04 | `rag-server/app/main.py:16–37` | Add shared-secret authentication to `/ingest`; validate `file_path` is inside the upload volume |
| 7 | LOGIC-03 | `NotebooksController.cs:39, 51, 62` | Filter `!n.Archived` in `Get`, `Update`, and `Archive` lookups |
| 8 | SEC-05 | All controllers lines 15–16 | Guard against null `UserId` claim; return `401` instead of crashing |
| 9 | MAINT-02 | `SourcesController.cs:67` | Log the exception in the fire-and-forget catch block |
| 10 | LOGIC-06 | `NotebookDetailPage.tsx:23–37` | Add `try/catch` to `uploadFile` and `createNote`; display errors to the user |

### Nice-to-Have

| ID | Change |
|----|--------|
| SEC-06 | Throw on absent `ADMIN_PASSWORD` env var in `Program.cs` |
| SEC-07 | Guard `registerTokenGetter` against double registration in `api.ts` |
| PERF-01 | Add `Take()` limit on `Sources` and `Notes` in `NotebooksController.Get` |
| PERF-02 | Add composite `(NotebookId, UserId)` index for Sources and Notes in `AppDbContext` |
| PERF-03 | Apply optimistic local state updates in `NotebookDetailPage` instead of full reloads |
| PERF-04 | Initialise ArangoDB connection eagerly at FastAPI startup in `db.py` |
| MAINT-01 | Replace `Status` magic strings with a `SourceStatus` constants class |
| MAINT-03 | Replace `NoteType` magic strings with constants or an enum |
| MAINT-04 | Return a consistent DTO from `NotebooksController.Update` instead of the tracked entity |
| MAINT-05 | Document intended use of `mime_type` in `IngestRequest` or remove if unused |
| MAINT-06 | Initialise `_db` in startup hook to fix lazy singleton race in `db.py` |
| LOGIC-04 | Change `/ingest` HTTP status to `200 OK` or implement real async processing to honour `202` |
| LOGIC-05 | Validate notebook ownership in `SourcesController.List` before querying sources |
| LOGIC-07 | Add a prominent warning comment to `Phase2Content.Down()` about production data loss |
