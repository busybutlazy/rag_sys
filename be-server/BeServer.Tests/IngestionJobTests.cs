using System.Security.Claims;
using AppDbContext = global::BeServer.Data.AppDbContext;
using IngestionJob = global::BeServer.Data.Entities.IngestionJob;
using IngestionJobStatuses = global::BeServer.Data.Entities.IngestionJobStatuses;
using IngestionJobTypes = global::BeServer.Data.Entities.IngestionJobTypes;
using Notebook = global::BeServer.Data.Entities.Notebook;
using Source = global::BeServer.Data.Entities.Source;
using SourcesController = global::BeServer.Content.SourcesController;
using User = global::BeServer.Data.Entities.User;
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

public class IngestionJobTests
{
    [Fact]
    public async Task Upload_CreatesQueuedIngestionJob()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db);
        var notebook = await SeedNotebook(db, user.Id);
        var uploadDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var controller = CreateSourcesController(db, user.Id, uploadDir, new FakeHandler());
        var file = new FormFile(new MemoryStream("hello"u8.ToArray()), 0, 5, "file", "doc.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain",
        };

        try
        {
            var result = await controller.Upload(notebook.Id, file);

            Assert.IsType<CreatedAtActionResult>(result);
            var job = await db.IngestionJobs.SingleAsync();
            var source = await db.Sources.SingleAsync();
            Assert.Equal(source.Id, job.SourceId);
            Assert.Equal(IngestionJobStatuses.Queued, job.Status);
            Assert.Equal(IngestionJobStatuses.Queued, source.Status);
        }
        finally
        {
            if (Directory.Exists(uploadDir))
                Directory.Delete(uploadDir, recursive: true);
        }
    }

    [Fact]
    public async Task Worker_ProcessesQueuedJobAndMarksSourceIngested()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        await using var provider = CreateWorkerProvider(handler);
        await SeedSourceAndJob(provider);
        var worker = provider.GetRequiredService<IngestionJobWorker>();

        var processed = await worker.ProcessNextAsync();

        Assert.True(processed);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(IngestionJobStatuses.Succeeded, (await db.IngestionJobs.SingleAsync()).Status);
        Assert.Equal("ingested", (await db.Sources.SingleAsync()).Status);
    }

    [Fact]
    public async Task Worker_RetriesThenFailsWhenAttemptsAreExhausted()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway));
        await using var provider = CreateWorkerProvider(handler);
        await SeedSourceAndJob(provider, maxAttempts: 1);
        var worker = provider.GetRequiredService<IngestionJobWorker>();

        var processed = await worker.ProcessNextAsync();

        Assert.True(processed);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.IngestionJobs.SingleAsync();
        var source = await db.Sources.SingleAsync();
        Assert.Equal(IngestionJobStatuses.Failed, job.Status);
        Assert.Equal(IngestionJobStatuses.Failed, source.Status);
        Assert.NotNull(job.LastError);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<User> SeedUser(AppDbContext db)
    {
        var user = new User
        {
            Username = $"user-{Guid.NewGuid()}",
            PasswordHash = "hash",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<Notebook> SeedNotebook(AppDbContext db, string userId)
    {
        var notebook = new Notebook
        {
            UserId = userId,
            Name = "Notebook",
        };
        db.Notebooks.Add(notebook);
        await db.SaveChangesAsync();
        return notebook;
    }

    private static SourcesController CreateSourcesController(AppDbContext db, string userId, string uploadDir, HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UPLOAD_DIR"] = uploadDir,
                ["INTERNAL_SECRET"] = "test_internal_secret",
            })
            .Build();
        var rag = new RagClient(new HttpClient(handler) { BaseAddress = new Uri("http://rag-server") }, config);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
            ], "test")),
        };

        return new SourcesController(db, rag, config)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = context,
            },
        };
    }

    private static ServiceProvider CreateWorkerProvider(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["INTERNAL_SECRET"] = "test_internal_secret",
            })
            .Build();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped(_ => new RagClient(new HttpClient(handler) { BaseAddress = new Uri("http://rag-server") }, config));
        services.AddSingleton<ILogger<IngestionJobWorker>>(NullLogger<IngestionJobWorker>.Instance);
        services.AddSingleton<IngestionJobWorker>();
        return services.BuildServiceProvider();
    }

    private static async Task SeedSourceAndJob(ServiceProvider provider, int maxAttempts = 3)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedUser(db);
        var notebook = await SeedNotebook(db, user.Id);
        var source = new Source
        {
            UserId = user.Id,
            NotebookId = notebook.Id,
            Title = "doc.txt",
            FilePath = "/tmp/doc.txt",
            MimeType = "text/plain",
            Status = IngestionJobStatuses.Queued,
        };
        db.Sources.Add(source);
        db.IngestionJobs.Add(new IngestionJob
        {
            SourceId = source.Id,
            NotebookId = notebook.Id,
            UserId = user.Id,
            JobType = IngestionJobTypes.Ingest,
            Status = IngestionJobStatuses.Queued,
            MaxAttempts = maxAttempts,
            AvailableAt = DateTime.UtcNow.AddSeconds(-1),
        });
        await db.SaveChangesAsync();
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage>? respond = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond?.Invoke(request) ?? new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
