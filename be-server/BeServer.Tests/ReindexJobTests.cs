using System.Security.Claims;
using BeServer.Content;
using BeServer.Data;
using BeServer.Data.Entities;
using BeServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Fact]
    public async Task Worker_SourceScope_MarksGraphExtractionSucceeded_WhenTargetVersionEnablesGraph()
    {
        var handler = new FakeJsonHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/content"))
                return JsonResponse(new
                {
                    source_id = "s",
                    notebook_id = "n",
                    chunks = new[] { new { source_id = "s", chunk_index = 0, text = "hello world" } },
                    text = "hello world",
                    truncated = false,
                });
            if (path == "/ai/extract/graph")
                return JsonResponse(new[] { new { chunk_index = 0, mentions = Array.Empty<object>(), facts = Array.Empty<object>() } });
            if (path == "/graph/ingest")
                return JsonResponse(new { entities_written = 0, facts_written = 0, edges_written = 0, skipped_chunks = Array.Empty<int>() });
            return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        });
        await using var provider = CreateWorkerProvider(handler);
        var (notebook, target, source) = await SeedSourceScopeJob(provider, enableGraph: true);
        var worker = provider.GetRequiredService<ReindexJobWorker>();

        var processed = await worker.ProcessNextAsync();

        Assert.True(processed);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.ReindexJobs.SingleAsync();
        Assert.Equal(ReindexJobStatuses.Succeeded, job.Status);
        Assert.Equal(GraphExtractionStatuses.Succeeded, job.GraphExtractionStatus);
    }

    [Fact]
    public async Task Worker_SourceScope_SkipsGraphExtraction_WhenTargetVersionDoesNotEnableGraph()
    {
        var handler = new FakeJsonHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
        await using var provider = CreateWorkerProvider(handler);
        var (notebook, target, source) = await SeedSourceScopeJob(provider, enableGraph: false);
        var worker = provider.GetRequiredService<ReindexJobWorker>();

        await worker.ProcessNextAsync();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.ReindexJobs.SingleAsync();
        Assert.Equal(ReindexJobStatuses.Succeeded, job.Status);
        Assert.Equal(GraphExtractionStatuses.Skipped, job.GraphExtractionStatus);
    }

    private static HttpResponseMessage JsonResponse(object body) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
    };

    private static ServiceProvider CreateWorkerProvider(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["INTERNAL_SECRET"] = "test_internal_secret" })
            .Build();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddHttpContextAccessor();
        services.AddSingleton<IConfiguration>(config);
        services.AddScoped(_ => new RagClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://rag-server") },
            config,
            new HttpContextAccessor()));
        services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(handler));
        services.AddSingleton<ILogger<GraphExtractionService>>(NullLogger<GraphExtractionService>.Instance);
        services.AddScoped<GraphExtractionService>();
        services.AddSingleton<ILogger<ReindexJobWorker>>(NullLogger<ReindexJobWorker>.Instance);
        services.AddSingleton<ReindexJobWorker>();
        return services.BuildServiceProvider();
    }

    private static async Task<(Notebook notebook, NotebookRetrievalVersion target, Source source)> SeedSourceScopeJob(
        ServiceProvider provider, bool enableGraph)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedUser(db, isDevAdmin: true);
        var (notebook, previous) = await SeedNotebookWithVersion(db, user.Id);
        var target = new NotebookRetrievalVersion
        {
            NotebookId = notebook.Id,
            CreatedByUserId = user.Id,
            ChunkSize = 900,
            ChunkOverlap = 100,
            EmbeddingModel = "m",
            EmbeddingDimensions = 3,
            EnableGraph = enableGraph,
        };
        db.NotebookRetrievalVersions.Add(target);
        var source = new Source
        {
            UserId = user.Id,
            NotebookId = notebook.Id,
            Title = "doc.txt",
            FilePath = "/tmp/doc.txt",
            MimeType = "text/plain",
            LastIndexedRetrievalVersionId = previous.Id,
        };
        db.Sources.Add(source);
        db.ReindexJobs.Add(new ReindexJob
        {
            NotebookId = notebook.Id,
            UserId = user.Id,
            SourceId = source.Id,
            Scope = ReindexJobScopes.Source,
            TargetRetrievalVersionId = target.Id,
            PreviousRetrievalVersionId = previous.Id,
            Status = ReindexJobStatuses.Queued,
            AvailableAt = DateTime.UtcNow.AddSeconds(-1),
        });
        await db.SaveChangesAsync();
        return (notebook, target, source);
    }

    private sealed class FakeJsonHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("http://ai-server") };
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
