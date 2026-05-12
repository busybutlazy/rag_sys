# Phase 2 — Notebook & Content Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a notebook, upload a PDF/text source, write a note — all persisted and visible in the UI. No chunking or embedding yet; RAG server stores the raw document.

**Architecture:** BE server owns all relational state (MySQL). File uploads are saved to a Docker named volume (`uploads`) mounted to both be-server and rag-server. After a source file is saved by be-server, it calls `POST http://rag-server:8003/ingest` to store raw document metadata in ArangoDB. All BE endpoints require `Authorization: Bearer <token>`; the `user_id` is extracted from the JWT claim `sub`.

**Tech Stack:**
- BE: EF Core 8, Pomelo MySQL, System.IO (file write), HttpClient (call rag-server)
- RAG: python-arango, python-multipart (FastAPI file upload)
- Frontend: React, react-router-dom v6, fetch API, basic Tailwind layout

---

## File Map

```
be-server/BeServer/
├── BeServer.csproj                          (add HttpClient)
├── Data/
│   ├── AppDbContext.cs                      (add 4 new DbSets)
│   └── Entities/
│       ├── Notebook.cs                     (new)
│       ├── Source.cs                       (new)
│       ├── Note.cs                         (new)
│       └── ChatSession.cs                  (new)
├── Migrations/
│   └── 20260512010000_Phase2Content.cs     (new)
├── Content/
│   ├── NotebooksController.cs              (new)
│   ├── SourcesController.cs                (new — file upload)
│   └── NotesController.cs                  (new)
└── Services/
    └── RagClient.cs                        (new — HttpClient to rag-server)

rag-server/app/
├── main.py                                  (add /ingest route)
├── db.py                                    (unchanged)
└── models.py                               (new — Pydantic schemas)

frontend/src/
├── lib/
│   └── api.ts                              (new — fetch wrapper with auth header)
├── pages/
│   ├── NotebooksPage.tsx                   (new)
│   ├── NotebookDetailPage.tsx              (new)
│   └── DashboardPage.tsx                   (replace — nav to notebooks)
└── App.tsx                                  (add /notebooks routes)
```

---

## Task 1: BE Server — New Entities & Migration

**Files:**
- Create: `be-server/BeServer/Data/Entities/Notebook.cs`
- Create: `be-server/BeServer/Data/Entities/Source.cs`
- Create: `be-server/BeServer/Data/Entities/Note.cs`
- Create: `be-server/BeServer/Data/Entities/ChatSession.cs`
- Modify: `be-server/BeServer/Data/AppDbContext.cs`
- Create: `be-server/BeServer/Migrations/20260512010000_Phase2Content.cs`

- [ ] **Step 1: Write Notebook.cs**

```csharp
namespace BeServer.Data.Entities;

public class Notebook
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool Archived { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public ICollection<Source> Sources { get; set; } = [];
    public ICollection<Note> Notes { get; set; } = [];
    public ICollection<ChatSession> ChatSessions { get; set; } = [];
}
```

- [ ] **Step 2: Write Source.cs**

```csharp
namespace BeServer.Data.Entities;

public class Source
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = null!;
    public string NotebookId { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? FilePath { get; set; }    // path inside Docker volume
    public string? MimeType { get; set; }
    public long? FileSizeBytes { get; set; }
    public string Status { get; set; } = "uploaded"; // uploaded | ingested | error
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Notebook Notebook { get; set; } = null!;
}
```

- [ ] **Step 3: Write Note.cs**

```csharp
namespace BeServer.Data.Entities;

public class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = null!;
    public string NotebookId { get; set; } = null!;
    public string? Title { get; set; }
    public string Content { get; set; } = string.Empty;
    public string NoteType { get; set; } = "human"; // human | ai
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Notebook Notebook { get; set; } = null!;
}
```

- [ ] **Step 4: Write ChatSession.cs**

```csharp
namespace BeServer.Data.Entities;

public class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = null!;
    public string NotebookId { get; set; } = null!;
    public string? Title { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Notebook Notebook { get; set; } = null!;
}
```

- [ ] **Step 5: Update AppDbContext.cs**

```csharp
using BeServer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Notebook> Notebooks { get; set; } = null!;
    public DbSet<Source> Sources { get; set; } = null!;
    public DbSet<Note> Notes { get; set; } = null!;
    public DbSet<ChatSession> ChatSessions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasMaxLength(36);
            e.Property(u => u.Username).HasMaxLength(64).IsRequired();
            e.Property(u => u.PasswordHash).HasMaxLength(255).IsRequired();
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.CreatedAt).HasColumnType("datetime");
            e.Property(u => u.UpdatedAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<Notebook>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Id).HasMaxLength(36);
            e.Property(n => n.UserId).HasMaxLength(36).IsRequired();
            e.Property(n => n.Name).HasMaxLength(255).IsRequired();
            e.Property(n => n.Description).HasMaxLength(1000);
            e.Property(n => n.CreatedAt).HasColumnType("datetime");
            e.Property(n => n.UpdatedAt).HasColumnType("datetime");
            e.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(n => n.UserId);
        });

        modelBuilder.Entity<Source>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasMaxLength(36);
            e.Property(s => s.UserId).HasMaxLength(36).IsRequired();
            e.Property(s => s.NotebookId).HasMaxLength(36).IsRequired();
            e.Property(s => s.Title).HasMaxLength(512).IsRequired();
            e.Property(s => s.FilePath).HasMaxLength(1024);
            e.Property(s => s.MimeType).HasMaxLength(128);
            e.Property(s => s.Status).HasMaxLength(32);
            e.Property(s => s.CreatedAt).HasColumnType("datetime");
            e.Property(s => s.UpdatedAt).HasColumnType("datetime");
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Notebook).WithMany(n => n.Sources).HasForeignKey(s => s.NotebookId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.NotebookId);
        });

        modelBuilder.Entity<Note>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Id).HasMaxLength(36);
            e.Property(n => n.UserId).HasMaxLength(36).IsRequired();
            e.Property(n => n.NotebookId).HasMaxLength(36).IsRequired();
            e.Property(n => n.Title).HasMaxLength(512);
            e.Property(n => n.Content).HasColumnType("text");
            e.Property(n => n.NoteType).HasMaxLength(16);
            e.Property(n => n.CreatedAt).HasColumnType("datetime");
            e.Property(n => n.UpdatedAt).HasColumnType("datetime");
            e.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(n => n.Notebook).WithMany(nb => nb.Notes).HasForeignKey(n => n.NotebookId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(n => n.NotebookId);
        });

        modelBuilder.Entity<ChatSession>(e =>
        {
            e.HasKey(cs => cs.Id);
            e.Property(cs => cs.Id).HasMaxLength(36);
            e.Property(cs => cs.UserId).HasMaxLength(36).IsRequired();
            e.Property(cs => cs.NotebookId).HasMaxLength(36).IsRequired();
            e.Property(cs => cs.Title).HasMaxLength(512);
            e.Property(cs => cs.CreatedAt).HasColumnType("datetime");
            e.Property(cs => cs.UpdatedAt).HasColumnType("datetime");
            e.HasOne(cs => cs.User).WithMany().HasForeignKey(cs => cs.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(cs => cs.Notebook).WithMany(n => n.ChatSessions).HasForeignKey(cs => cs.NotebookId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(cs => cs.NotebookId);
        });
    }
}
```

- [ ] **Step 6: Commit entities + context**

```bash
git add be-server/BeServer/Data/
git commit -m "feat(be): add Notebook, Source, Note, ChatSession entities"
```

---

## Task 2: BE Server — EF Migration

**Files:**
- Create: `be-server/BeServer/Migrations/20260512010000_Phase2Content.cs`
- Modify: `be-server/BeServer/Migrations/AppDbContextModelSnapshot.cs`

- [ ] **Step 1: Write migration Up/Down**

The migration creates 4 tables: `Notebooks`, `Sources`, `Notes`, `ChatSessions`.

```csharp
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeServer.Migrations
{
    public partial class Phase2Content : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notebooks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Archived = table.Column<bool>(nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notebooks", x => x.Id);
                    table.ForeignKey("FK_Notebooks_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Sources",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NotebookId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FilePath = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MimeType = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileSizeBytes = table.Column<long>(nullable: true),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, defaultValue: "uploaded")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sources", x => x.Id);
                    table.ForeignKey("FK_Sources_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_Sources_Notebooks_NotebookId", x => x.NotebookId, "Notebooks", "Id", onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Notes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NotebookId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Content = table.Column<string>(type: "text", nullable: false),
                    NoteType = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, defaultValue: "human")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => x.Id);
                    table.ForeignKey("FK_Notes_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_Notes_Notebooks_NotebookId", x => x.NotebookId, "Notebooks", "Id", onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ChatSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NotebookId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSessions", x => x.Id);
                    table.ForeignKey("FK_ChatSessions_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_ChatSessions_Notebooks_NotebookId", x => x.NotebookId, "Notebooks", "Id", onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex("IX_Notebooks_UserId", "Notebooks", "UserId");
            migrationBuilder.CreateIndex("IX_Sources_NotebookId", "Sources", "NotebookId");
            migrationBuilder.CreateIndex("IX_Notes_NotebookId", "Notes", "NotebookId");
            migrationBuilder.CreateIndex("IX_ChatSessions_NotebookId", "ChatSessions", "NotebookId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("ChatSessions");
            migrationBuilder.DropTable("Notes");
            migrationBuilder.DropTable("Sources");
            migrationBuilder.DropTable("Notebooks");
        }
    }
}
```

- [ ] **Step 2: Commit migration**

```bash
git add be-server/BeServer/Migrations/
git commit -m "feat(be): add Phase2Content migration (Notebooks, Sources, Notes, ChatSessions)"
```

---

## Task 3: BE Server — RagClient & Content Controllers

**Files:**
- Create: `be-server/BeServer/Services/RagClient.cs`
- Create: `be-server/BeServer/Content/NotebooksController.cs`
- Create: `be-server/BeServer/Content/SourcesController.cs`
- Create: `be-server/BeServer/Content/NotesController.cs`
- Modify: `be-server/BeServer/Program.cs` (register HttpClient + upload dir)

- [ ] **Step 1: Write RagClient.cs**

```csharp
using System.Net.Http.Json;

namespace BeServer.Services;

public class RagClient(HttpClient http)
{
    public async Task IngestAsync(string sourceId, string filePath, string mimeType)
    {
        var payload = new { source_id = sourceId, file_path = filePath, mime_type = mimeType };
        var response = await http.PostAsJsonAsync("/ingest", payload);
        response.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 2: Write NotebooksController.cs**

```csharp
using System.Security.Claims;
using BeServer.Data;
using BeServer.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Content;

[ApiController]
[Route("api/notebooks")]
[Authorize]
public class NotebooksController(AppDbContext db) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")!;

    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await db.Notebooks
            .Where(n => n.UserId == UserId && !n.Archived)
            .OrderByDescending(n => n.UpdatedAt)
            .Select(n => new { n.Id, n.Name, n.Description, n.CreatedAt, n.UpdatedAt })
            .ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] NotebookRequest req)
    {
        var nb = new Notebook { UserId = UserId, Name = req.Name, Description = req.Description };
        db.Notebooks.Add(nb);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = nb.Id }, nb);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var nb = await db.Notebooks
            .Where(n => n.Id == id && n.UserId == UserId)
            .Select(n => new
            {
                n.Id, n.Name, n.Description, n.Archived, n.CreatedAt, n.UpdatedAt,
                Sources = n.Sources.Select(s => new { s.Id, s.Title, s.MimeType, s.Status, s.CreatedAt }),
                Notes = n.Notes.Select(nt => new { nt.Id, nt.Title, nt.NoteType, nt.CreatedAt }),
            })
            .FirstOrDefaultAsync();
        return nb is null ? NotFound() : Ok(nb);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] NotebookRequest req)
    {
        var nb = await db.Notebooks.FirstOrDefaultAsync(n => n.Id == id && n.UserId == UserId);
        if (nb is null) return NotFound();
        nb.Name = req.Name;
        nb.Description = req.Description;
        nb.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(nb);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Archive(string id)
    {
        var nb = await db.Notebooks.FirstOrDefaultAsync(n => n.Id == id && n.UserId == UserId);
        if (nb is null) return NotFound();
        nb.Archived = true;
        nb.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record NotebookRequest(string Name, string? Description);
```

- [ ] **Step 3: Write SourcesController.cs**

```csharp
using System.Security.Claims;
using BeServer.Data;
using BeServer.Data.Entities;
using BeServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Content;

[ApiController]
[Route("api/notebooks/{notebookId}/sources")]
[Authorize]
public class SourcesController(AppDbContext db, RagClient rag, IConfiguration config) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")!;

    private string UploadDir => config["UPLOAD_DIR"] ?? "/app/uploads";

    [HttpGet]
    public async Task<IActionResult> List(string notebookId) =>
        Ok(await db.Sources
            .Where(s => s.NotebookId == notebookId && s.UserId == UserId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.Id, s.Title, s.MimeType, s.FileSizeBytes, s.Status, s.CreatedAt })
            .ToListAsync());

    [HttpPost]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB
    public async Task<IActionResult> Upload(string notebookId, IFormFile file)
    {
        var nb = await db.Notebooks.FirstOrDefaultAsync(n => n.Id == notebookId && n.UserId == UserId);
        if (nb is null) return NotFound(new { error = "Notebook not found" });

        var source = new Source
        {
            UserId = UserId,
            NotebookId = notebookId,
            Title = file.FileName,
            MimeType = file.ContentType,
            FileSizeBytes = file.Length,
            Status = "uploaded",
        };

        // Save file to shared volume
        var dir = Path.Combine(UploadDir, UserId);
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, $"{source.Id}_{file.FileName}");
        await using (var stream = System.IO.File.Create(filePath))
            await file.CopyToAsync(stream);

        source.FilePath = filePath;
        db.Sources.Add(source);
        await db.SaveChangesAsync();

        // Notify rag-server (fire-and-forget; status updated async)
        _ = Task.Run(async () =>
        {
            try
            {
                await rag.IngestAsync(source.Id, filePath, file.ContentType);
                source.Status = "ingested";
                source.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            catch
            {
                source.Status = "error";
                source.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        });

        return CreatedAtAction(nameof(List), new { notebookId }, new { source.Id, source.Title, source.Status });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string notebookId, string id)
    {
        var source = await db.Sources.FirstOrDefaultAsync(s => s.Id == id && s.NotebookId == notebookId && s.UserId == UserId);
        if (source is null) return NotFound();
        if (source.FilePath is not null && System.IO.File.Exists(source.FilePath))
            System.IO.File.Delete(source.FilePath);
        db.Sources.Remove(source);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
```

- [ ] **Step 4: Write NotesController.cs**

```csharp
using System.Security.Claims;
using BeServer.Data;
using BeServer.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Content;

[ApiController]
[Route("api/notebooks/{notebookId}/notes")]
[Authorize]
public class NotesController(AppDbContext db) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")!;

    [HttpGet]
    public async Task<IActionResult> List(string notebookId) =>
        Ok(await db.Notes
            .Where(n => n.NotebookId == notebookId && n.UserId == UserId)
            .OrderByDescending(n => n.UpdatedAt)
            .Select(n => new { n.Id, n.Title, n.NoteType, n.CreatedAt, n.UpdatedAt })
            .ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create(string notebookId, [FromBody] NoteRequest req)
    {
        var nb = await db.Notebooks.FirstOrDefaultAsync(n => n.Id == notebookId && n.UserId == UserId);
        if (nb is null) return NotFound(new { error = "Notebook not found" });

        var note = new Note
        {
            UserId = UserId,
            NotebookId = notebookId,
            Title = req.Title,
            Content = req.Content,
        };
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { notebookId, id = note.Id }, note);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string notebookId, string id)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.NotebookId == notebookId && n.UserId == UserId);
        return note is null ? NotFound() : Ok(note);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string notebookId, string id, [FromBody] NoteRequest req)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.NotebookId == notebookId && n.UserId == UserId);
        if (note is null) return NotFound();
        note.Title = req.Title;
        note.Content = req.Content;
        note.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(note);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string notebookId, string id)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.NotebookId == notebookId && n.UserId == UserId);
        if (note is null) return NotFound();
        db.Notes.Remove(note);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record NoteRequest(string? Title, string Content);
```

- [ ] **Step 5: Update Program.cs — add HttpClient, upload dir, form options**

Add after `builder.Services.AddControllers()`:

```csharp
builder.Services.AddHttpClient<RagClient>(client =>
    client.BaseAddress = new Uri(
        builder.Configuration["RAG_SERVER_URL"] ?? "http://rag-server:8003"));
```

Also add `RAG_SERVER_URL` to the env block in docker-compose.yml be-server section.

- [ ] **Step 6: Commit all BE content controllers**

```bash
git add be-server/
git commit -m "feat(be): add content controllers (Notebooks, Sources, Notes) + RagClient"
```

---

## Task 4: RAG Server — /ingest endpoint

**Files:**
- Create: `rag-server/app/models.py`
- Modify: `rag-server/app/main.py`

- [ ] **Step 1: Write models.py**

```python
from pydantic import BaseModel


class IngestRequest(BaseModel):
    source_id: str
    file_path: str
    mime_type: str
```

- [ ] **Step 2: Update main.py**

```python
import os
from fastapi import FastAPI, HTTPException
from app.db import get_db
from app.models import IngestRequest

app = FastAPI(title="RAG Server", version="0.1.0")


@app.get("/health")
async def health():
    db = get_db()
    db.version()
    return {"status": "ok", "service": "rag-server"}


@app.post("/ingest", status_code=202)
async def ingest(req: IngestRequest):
    if not os.path.exists(req.file_path):
        raise HTTPException(status_code=404, detail=f"File not found: {req.file_path}")

    db = get_db()
    documents = db.collection("documents")

    # Store raw document metadata (no chunking yet — Phase 4)
    doc = {
        "_key": req.source_id,
        "source_id": req.source_id,
        "file_path": req.file_path,
        "mime_type": req.mime_type,
        "status": "stored",
    }

    if documents.has(req.source_id):
        documents.update(doc)
    else:
        documents.insert(doc)

    return {"source_id": req.source_id, "status": "stored"}
```

- [ ] **Step 3: Commit**

```bash
git add rag-server/
git commit -m "feat(rag): add /ingest endpoint — stores raw document metadata in ArangoDB"
```

---

## Task 5: Frontend — Notebooks & Note Editor

**Files:**
- Create: `frontend/src/lib/api.ts`
- Create: `frontend/src/pages/NotebooksPage.tsx`
- Create: `frontend/src/pages/NotebookDetailPage.tsx`
- Modify: `frontend/src/pages/DashboardPage.tsx`
- Modify: `frontend/src/App.tsx`

- [ ] **Step 1: Write api.ts — fetch wrapper that injects Authorization header**

```typescript
import { useAuthContext } from '../contexts/AuthContext'

// Non-hook version for use outside React components
let _getToken: (() => string | null) | null = null
export function registerTokenGetter(fn: () => string | null) { _getToken = fn }

async function apiFetch(path: string, init?: RequestInit): Promise<Response> {
  const token = _getToken?.()
  return fetch(path, {
    ...init,
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init?.headers,
    },
  })
}

export async function apiGet<T>(path: string): Promise<T> {
  const res = await apiFetch(path)
  if (!res.ok) throw new Error(`GET ${path} → ${res.status}`)
  return res.json()
}

export async function apiPost<T>(path: string, body: unknown): Promise<T> {
  const res = await apiFetch(path, { method: 'POST', body: JSON.stringify(body) })
  if (!res.ok) throw new Error(`POST ${path} → ${res.status}`)
  return res.json()
}

export async function apiPut<T>(path: string, body: unknown): Promise<T> {
  const res = await apiFetch(path, { method: 'PUT', body: JSON.stringify(body) })
  if (!res.ok) throw new Error(`PUT ${path} → ${res.status}`)
  return res.json()
}

export async function apiDelete(path: string): Promise<void> {
  const res = await apiFetch(path, { method: 'DELETE' })
  if (!res.ok) throw new Error(`DELETE ${path} → ${res.status}`)
}

export async function apiUpload<T>(path: string, file: File): Promise<T> {
  const token = _getToken?.()
  const form = new FormData()
  form.append('file', file)
  const res = await fetch(path, {
    method: 'POST',
    credentials: 'include',
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    body: form,
  })
  if (!res.ok) throw new Error(`UPLOAD ${path} → ${res.status}`)
  return res.json()
}
```

- [ ] **Step 2: Register token getter in main.tsx**

Add after `ReactDOM.createRoot(...)`:
```tsx
// Wire up api.ts token getter so non-hook fetch calls include the JWT
import { registerTokenGetter } from './lib/api'
// inside AuthProvider via a small bridge component (see App.tsx update)
```

Actually, the cleanest approach is to add a `<ApiTokenBridge />` component inside `AuthProvider` that calls `registerTokenGetter` on mount:

```tsx
// In main.tsx, add ApiTokenBridge just inside AuthProvider:
function ApiTokenBridge() {
  const { accessToken } = useAuthContext()
  useEffect(() => { registerTokenGetter(() => accessToken) }, [accessToken])
  return null
}
```

- [ ] **Step 3: Write NotebooksPage.tsx**

```tsx
import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { apiGet, apiPost } from '../lib/api'

interface Notebook { id: string; name: string; description?: string; updatedAt: string }

export default function NotebooksPage() {
  const [notebooks, setNotebooks] = useState<Notebook[]>([])
  const [name, setName] = useState('')
  const navigate = useNavigate()

  useEffect(() => {
    apiGet<Notebook[]>('/api/notebooks').then(setNotebooks)
  }, [])

  async function create(e: React.FormEvent) {
    e.preventDefault()
    const nb = await apiPost<Notebook>('/api/notebooks', { name })
    navigate(`/notebooks/${nb.id}`)
  }

  return (
    <div className="max-w-2xl mx-auto p-6">
      <h1 className="text-2xl font-bold mb-6">Notebooks</h1>
      <form onSubmit={create} className="flex gap-2 mb-6">
        <input
          value={name}
          onChange={e => setName(e.target.value)}
          placeholder="New notebook name…"
          required
          className="flex-1 border rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
        />
        <button type="submit" className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700">
          Create
        </button>
      </form>
      <ul className="space-y-2">
        {notebooks.map(nb => (
          <li key={nb.id}>
            <Link
              to={`/notebooks/${nb.id}`}
              className="block p-4 border rounded-lg hover:bg-gray-50 transition"
            >
              <p className="font-semibold">{nb.name}</p>
              {nb.description && <p className="text-sm text-gray-500">{nb.description}</p>}
            </Link>
          </li>
        ))}
      </ul>
    </div>
  )
}
```

- [ ] **Step 4: Write NotebookDetailPage.tsx**

```tsx
import { useEffect, useState, useRef } from 'react'
import { useParams, Link } from 'react-router-dom'
import { apiGet, apiPost, apiUpload, apiDelete } from '../lib/api'

interface Source { id: string; title: string; mimeType: string; status: string }
interface Note { id: string; title?: string; noteType: string; createdAt: string }
interface NotebookDetail {
  id: string; name: string; description?: string
  sources: Source[]; notes: Note[]
}

export default function NotebookDetailPage() {
  const { id } = useParams<{ id: string }>()
  const [nb, setNb] = useState<NotebookDetail | null>(null)
  const [noteContent, setNoteContent] = useState('')
  const [noteTitle, setNoteTitle] = useState('')
  const fileRef = useRef<HTMLInputElement>(null)

  const reload = () => apiGet<NotebookDetail>(`/api/notebooks/${id}`).then(setNb)

  useEffect(() => { reload() }, [id])

  async function uploadFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    await apiUpload(`/api/notebooks/${id}/sources`, file)
    reload()
  }

  async function createNote(e: React.FormEvent) {
    e.preventDefault()
    await apiPost(`/api/notebooks/${id}/notes`, { title: noteTitle, content: noteContent })
    setNoteContent('')
    setNoteTitle('')
    reload()
  }

  async function deleteSource(sourceId: string) {
    await apiDelete(`/api/notebooks/${id}/sources/${sourceId}`)
    reload()
  }

  if (!nb) return <div className="p-6">Loading…</div>

  return (
    <div className="max-w-3xl mx-auto p-6 space-y-8">
      <div className="flex items-center gap-4">
        <Link to="/notebooks" className="text-blue-600 hover:underline">← Notebooks</Link>
        <h1 className="text-2xl font-bold">{nb.name}</h1>
      </div>

      {/* Sources */}
      <section>
        <h2 className="text-lg font-semibold mb-2">Sources</h2>
        <input ref={fileRef} type="file" className="hidden" onChange={uploadFile} />
        <button
          onClick={() => fileRef.current?.click()}
          className="mb-3 px-3 py-1.5 bg-green-600 text-white rounded-lg text-sm hover:bg-green-700"
        >
          Upload File
        </button>
        <ul className="space-y-1">
          {nb.sources.map(s => (
            <li key={s.id} className="flex items-center justify-between border rounded px-3 py-2">
              <span className="text-sm">{s.title} <span className="text-gray-400">({s.status})</span></span>
              <button onClick={() => deleteSource(s.id)} className="text-red-500 text-xs hover:underline">Delete</button>
            </li>
          ))}
        </ul>
      </section>

      {/* New Note */}
      <section>
        <h2 className="text-lg font-semibold mb-2">Add Note</h2>
        <form onSubmit={createNote} className="space-y-2">
          <input
            value={noteTitle}
            onChange={e => setNoteTitle(e.target.value)}
            placeholder="Title (optional)"
            className="w-full border rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
          <textarea
            value={noteContent}
            onChange={e => setNoteContent(e.target.value)}
            placeholder="Write a note… (Markdown supported)"
            required
            rows={5}
            className="w-full border rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 font-mono"
          />
          <button type="submit" className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700">
            Save Note
          </button>
        </form>
      </section>

      {/* Notes list */}
      <section>
        <h2 className="text-lg font-semibold mb-2">Notes ({nb.notes.length})</h2>
        <ul className="space-y-1">
          {nb.notes.map(n => (
            <li key={n.id} className="border rounded px-3 py-2 text-sm">
              {n.title ?? '(untitled)'} <span className="text-gray-400">{n.noteType}</span>
            </li>
          ))}
        </ul>
      </section>
    </div>
  )
}
```

- [ ] **Step 5: Update DashboardPage.tsx to navigate to notebooks**

```tsx
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'

export default function DashboardPage() {
  const { username, logout } = useAuth()
  const navigate = useNavigate()
  return (
    <div className="min-h-screen bg-gray-50 flex flex-col items-center justify-center gap-4">
      <h1 className="text-3xl font-bold">RAG System</h1>
      <p className="text-gray-600">Logged in as <span className="font-semibold">{username}</span></p>
      <div className="flex gap-3">
        <button
          onClick={() => navigate('/notebooks')}
          className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition"
        >
          My Notebooks
        </button>
        <button
          onClick={logout}
          className="px-4 py-2 bg-red-500 text-white rounded-lg hover:bg-red-600 transition"
        >
          Sign Out
        </button>
      </div>
    </div>
  )
}
```

- [ ] **Step 6: Update App.tsx — add /notebooks routes**

```tsx
import { Routes, Route, Navigate } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { useAuth } from './hooks/useAuth'
import { useAuthContext } from './contexts/AuthContext'
import { registerTokenGetter } from './lib/api'
import ProtectedRoute from './components/ProtectedRoute'
import LoginPage from './pages/LoginPage'
import DashboardPage from './pages/DashboardPage'
import NotebooksPage from './pages/NotebooksPage'
import NotebookDetailPage from './pages/NotebookDetailPage'

function ApiTokenBridge() {
  const { accessToken } = useAuthContext()
  useEffect(() => { registerTokenGetter(() => accessToken) }, [accessToken])
  return null
}

function AppRoutes() {
  const { refresh, accessToken } = useAuth()
  const [checked, setChecked] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    const id = setTimeout(() => controller.abort(), 5000)
    refresh().finally(() => { clearTimeout(id); setChecked(true) })
  }, [refresh])

  if (!checked) return null

  return (
    <Routes>
      <Route path="/login" element={accessToken ? <Navigate to="/dashboard" replace /> : <LoginPage />} />
      <Route path="/dashboard" element={<ProtectedRoute><DashboardPage /></ProtectedRoute>} />
      <Route path="/notebooks" element={<ProtectedRoute><NotebooksPage /></ProtectedRoute>} />
      <Route path="/notebooks/:id" element={<ProtectedRoute><NotebookDetailPage /></ProtectedRoute>} />
      <Route path="*" element={<Navigate to={accessToken ? '/dashboard' : '/login'} replace />} />
    </Routes>
  )
}

export default function App() {
  return (
    <>
      <ApiTokenBridge />
      <AppRoutes />
    </>
  )
}
```

- [ ] **Step 7: Commit frontend**

```bash
git add frontend/src/
git commit -m "feat(frontend): notebooks list, detail, source upload, note editor"
```
