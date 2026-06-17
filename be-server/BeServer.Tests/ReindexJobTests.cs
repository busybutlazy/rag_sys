using System.Security.Claims;
using BeServer.Content;
using BeServer.Data;
using BeServer.Data.Entities;
using BeServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BeServer.Tests;

public class ReindexJobTests
{
    [Fact]
    public async Task QueueNotebookReindex_CreatesJobInDb()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, isDevAdmin: true);
        var (notebook, version) = await SeedNotebookWithVersion(db, user.Id);
        var target = new NotebookRetrievalVersion { NotebookId = notebook.Id, CreatedByUserId = user.Id, ChunkSize = 900, ChunkOverlap = 100, EmbeddingModel = "m", EmbeddingDimensions = 3 };
        db.NotebookRetrievalVersions.Add(target);
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id);
        var result = await controller.QueueNotebookReindex(notebook.Id, new QueueReindexRequest(target.Id));

        Assert.IsType<CreatedAtActionResult>(result);
        var job = await db.ReindexJobs.SingleAsync();
        Assert.Equal(ReindexJobScopes.Notebook, job.Scope);
        Assert.Equal(target.Id, job.TargetRetrievalVersionId);
        Assert.Equal(notebook.ActiveRetrievalVersionId, job.PreviousRetrievalVersionId);
        Assert.Equal(ReindexJobStatuses.Queued, job.Status);
    }

    [Fact]
    public async Task QueueSourceReindex_CreatesJobInDb()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, isDevAdmin: true);
        var (notebook, version) = await SeedNotebookWithVersion(db, user.Id);
        var source = new Source { UserId = user.Id, NotebookId = notebook.Id, Title = "doc", LastIndexedRetrievalVersionId = version.Id };
        db.Sources.Add(source);
        var target = new NotebookRetrievalVersion { NotebookId = notebook.Id, CreatedByUserId = user.Id, ChunkSize = 900, ChunkOverlap = 100, EmbeddingModel = "m", EmbeddingDimensions = 3 };
        db.NotebookRetrievalVersions.Add(target);
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id);
        var result = await controller.QueueSourceReindex(notebook.Id, source.Id, new QueueReindexRequest(target.Id));

        Assert.IsType<CreatedAtActionResult>(result);
        var job = await db.ReindexJobs.SingleAsync();
        Assert.Equal(ReindexJobScopes.Source, job.Scope);
        Assert.Equal(source.Id, job.SourceId);
        Assert.Equal(version.Id, job.PreviousRetrievalVersionId);
    }

    [Fact]
    public async Task Promote_SucceededJob_UpdatesNotebookAndSources()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, isDevAdmin: true);
        var (notebook, version) = await SeedNotebookWithVersion(db, user.Id);
        var target = new NotebookRetrievalVersion { NotebookId = notebook.Id, CreatedByUserId = user.Id, ChunkSize = 900, ChunkOverlap = 100, EmbeddingModel = "m", EmbeddingDimensions = 3 };
        db.NotebookRetrievalVersions.Add(target);
        var source = new Source { UserId = user.Id, NotebookId = notebook.Id, Title = "doc" };
        db.Sources.Add(source);
        var job = new ReindexJob
        {
            NotebookId = notebook.Id,
            UserId = user.Id,
            Scope = ReindexJobScopes.Notebook,
            TargetRetrievalVersionId = target.Id,
            Status = ReindexJobStatuses.Succeeded,
        };
        db.ReindexJobs.Add(job);
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id);
        var result = await controller.Promote(job.Id);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(target.Id, notebook.ActiveRetrievalVersionId);
        Assert.Equal(target.Id, await db.Sources.Select(s => s.ActiveRetrievalVersionId).SingleAsync());
    }

    [Fact]
    public async Task Promote_NonSucceededJob_ReturnsBadRequest()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, isDevAdmin: true);
        var (notebook, _) = await SeedNotebookWithVersion(db, user.Id);
        var job = new ReindexJob
        {
            NotebookId = notebook.Id,
            UserId = user.Id,
            Scope = ReindexJobScopes.Notebook,
            TargetRetrievalVersionId = "v",
            Status = ReindexJobStatuses.Running,
        };
        db.ReindexJobs.Add(job);
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id);
        var result = await controller.Promote(job.Id);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Cancel_QueuedJob_SetsCancelled()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, isDevAdmin: true);
        var (notebook, _) = await SeedNotebookWithVersion(db, user.Id);
        var job = new ReindexJob
        {
            NotebookId = notebook.Id,
            UserId = user.Id,
            Scope = ReindexJobScopes.Notebook,
            TargetRetrievalVersionId = "v",
            Status = ReindexJobStatuses.Queued,
        };
        db.ReindexJobs.Add(job);
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id);
        var result = await controller.Cancel(job.Id);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(ReindexJobStatuses.Cancelled, job.Status);
    }

    [Fact]
    public async Task Cancel_RunningJob_ReturnsBadRequest()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, isDevAdmin: true);
        var (notebook, _) = await SeedNotebookWithVersion(db, user.Id);
        var job = new ReindexJob
        {
            NotebookId = notebook.Id,
            UserId = user.Id,
            Scope = ReindexJobScopes.Notebook,
            TargetRetrievalVersionId = "v",
            Status = ReindexJobStatuses.Running,
        };
        db.ReindexJobs.Add(job);
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id);
        var result = await controller.Cancel(job.Id);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task PruneVersionPayload_RejectsActiveVersion()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, isDevAdmin: true);
        var (notebook, version) = await SeedNotebookWithVersion(db, user.Id);
        var controller = CreateController(db, user.Id);
        var rag = CreateRagClient(controller.ControllerContext.HttpContext);

        var result = await controller.PruneVersionPayload(notebook.Id, version.Id, rag);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task PruneVersionPayload_AllowsInactiveVersion()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, isDevAdmin: true);
        var (notebook, _) = await SeedNotebookWithVersion(db, user.Id);
        var inactive = new NotebookRetrievalVersion
        {
            NotebookId = notebook.Id,
            CreatedByUserId = user.Id,
            ChunkSize = 900,
            ChunkOverlap = 100,
            EmbeddingModel = "m",
            EmbeddingDimensions = 3,
        };
        db.NotebookRetrievalVersions.Add(inactive);
        await db.SaveChangesAsync();
        var controller = CreateController(db, user.Id);
        var rag = CreateRagClient(controller.ControllerContext.HttpContext);

        var result = await controller.PruneVersionPayload(notebook.Id, inactive.Id, rag);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task QueueReindex_WrongNotebook_ReturnsNotFound()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, isDevAdmin: true);

        var controller = CreateController(db, user.Id);
        var result = await controller.QueueNotebookReindex("no-such-notebook", new QueueReindexRequest("v"));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task QueueReindex_WrongVersion_ReturnsBadRequest()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, isDevAdmin: true);
        var (notebook, _) = await SeedNotebookWithVersion(db, user.Id);

        var controller = CreateController(db, user.Id);
        var result = await controller.QueueNotebookReindex(notebook.Id, new QueueReindexRequest("no-such-version"));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── Helpers ───────────────────────────────────

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<User> SeedUser(AppDbContext db, bool isDevAdmin = false)
    {
        var user = new User { Username = Guid.NewGuid().ToString(), PasswordHash = "hash", IsDevAdmin = isDevAdmin };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<(Notebook, NotebookRetrievalVersion)> SeedNotebookWithVersion(AppDbContext db, string userId)
    {
        var version = new NotebookRetrievalVersion
        {
            CreatedByUserId = userId,
            NotebookId = "",
            ChunkSize = 800,
            ChunkOverlap = 100,
            EmbeddingModel = "m",
            EmbeddingDimensions = 3,
        };
        var notebook = new Notebook { UserId = userId, Name = "NB", ActiveRetrievalVersionId = version.Id };
        version.NotebookId = notebook.Id;
        db.Notebooks.Add(notebook);
        db.NotebookRetrievalVersions.Add(version);
        await db.SaveChangesAsync();
        return (notebook, version);
    }

    private static LabReindexController CreateController(AppDbContext db, string userId)
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
        return new LabReindexController(db, currentUser, new OwnershipService(db, currentUser))
        {
            ControllerContext = context,
        };
    }

    private static RagClient CreateRagClient(HttpContext httpContext)
    {
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        return new RagClient(
            new HttpClient(new FakeHandler()) { BaseAddress = new Uri("http://rag-server") },
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RAG_INTERNAL_SECRET"] = "this_is_a_long_test_secret_for_rag_boundary",
                })
                .Build(),
            accessor);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
    }
}
