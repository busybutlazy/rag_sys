using System.Security.Claims;
using BeServer.Content;
using BeServer.Data;
using BeServer.Data.Entities;
using BeServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BeServer.Tests;

public class RetrievalVersionTests
{
    [Fact]
    public async Task CreateInitialVersion_ActivatesGeneralPresetSnapshot()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db);
        var notebook = new Notebook { UserId = user.Id, Name = "Notebook" };
        db.Notebooks.Add(notebook);
        db.RetrievalPresets.Add(GeneralPreset());
        await db.SaveChangesAsync();

        var version = await new RetrievalVersionService(db).CreateInitialVersionAsync(notebook, user.Id);
        await db.SaveChangesAsync();

        Assert.Equal(version.Id, notebook.ActiveRetrievalVersionId);
        Assert.Equal(800, version.ChunkSize);
        Assert.Equal("general-id", version.OriginPresetId);
    }

    [Fact]
    public async Task Activate_UpdatesNotebookAndSourceTargetVersions()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, isDevAdmin: true);
        var notebook = new Notebook { UserId = user.Id, Name = "Notebook" };
        db.Notebooks.Add(notebook);
        var first = new NotebookRetrievalVersion { NotebookId = notebook.Id, CreatedByUserId = user.Id, ChunkSize = 800, ChunkOverlap = 100, EmbeddingModel = "m", EmbeddingDimensions = 3 };
        var second = new NotebookRetrievalVersion { NotebookId = notebook.Id, CreatedByUserId = user.Id, ChunkSize = 900, ChunkOverlap = 100, EmbeddingModel = "m", EmbeddingDimensions = 3 };
        db.NotebookRetrievalVersions.AddRange(first, second);
        db.Sources.Add(new Source { UserId = user.Id, NotebookId = notebook.Id, Title = "doc", ActiveRetrievalVersionId = first.Id });
        notebook.ActiveRetrievalVersionId = first.Id;
        await db.SaveChangesAsync();

        var controller = CreateLabController(db, user.Id);
        var result = await controller.Activate(notebook.Id, second.Id);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(second.Id, notebook.ActiveRetrievalVersionId);
        Assert.Equal(second.Id, await db.Sources.Select(s => s.ActiveRetrievalVersionId).SingleAsync());
    }

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<User> SeedUser(AppDbContext db, bool isDevAdmin = false)
    {
        var user = new User { Username = Guid.NewGuid().ToString(), PasswordHash = "hash", IsDevAdmin = isDevAdmin };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static RetrievalPreset GeneralPreset() => new()
    {
        Id = "general-id",
        Key = "general",
        Name = "General",
        ChunkSize = 800,
        ChunkOverlap = 100,
        EmbeddingModel = "text-embedding-3-small",
        EmbeddingDimensions = 1536,
        DefaultSearchMode = "hybrid",
        DefaultTopK = 5,
        DefaultHybridAlpha = 0.5,
    };

    private static LabRetrievalVersionsController CreateLabController(AppDbContext db, string userId)
    {
        var context = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test")),
            },
        };
        var accessor = new HttpContextAccessor { HttpContext = context.HttpContext };
        var currentUser = new CurrentUserAccessor(accessor);
        return new LabRetrievalVersionsController(db, currentUser, new OwnershipService(db, currentUser))
        {
            ControllerContext = context,
        };
    }
}
