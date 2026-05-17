using System.Net;
using System.Security.Claims;
using System.Text.Json;
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

public class RetrievalBenchTests
{
    [Fact]
    public async Task Compare_ReturnsPairedMetrics()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db);
        var (notebook, a, b) = await SeedNotebookWithVersions(db, user.Id);
        var controller = CreateController(db, user.Id);

        var result = Assert.IsType<OkObjectResult>(await controller.Compare(
            notebook.Id,
            new RetrievalCompareRequest("hello", a.Id, b.Id, ["hybrid"])));
        var json = JsonSerializer.Serialize(result.Value);

        Assert.Contains("OverlapAtK", json);
        Assert.Contains(a.Id, json);
        Assert.Contains(b.Id, json);
    }

    [Fact]
    public async Task Compare_RejectsCrossNotebookVersion()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db);
        var (notebook, a, _) = await SeedNotebookWithVersions(db, user.Id);
        var other = new Notebook { UserId = user.Id, Name = "Other" };
        db.Notebooks.Add(other);
        var foreign = new NotebookRetrievalVersion { NotebookId = other.Id, CreatedByUserId = user.Id, ChunkSize = 1, ChunkOverlap = 0, EmbeddingModel = "m", EmbeddingDimensions = 3 };
        db.NotebookRetrievalVersions.Add(foreign);
        await db.SaveChangesAsync();
        var controller = CreateController(db, user.Id);

        var result = await controller.Compare(notebook.Id, new RetrievalCompareRequest("hello", a.Id, foreign.Id, ["hybrid"]));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task RunDataset_PersistsResultsForEachQueryVersionAndMode()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db);
        var (notebook, a, b) = await SeedNotebookWithVersions(db, user.Id);
        var dataset = new EvaluationDataset { NotebookId = notebook.Id, UserId = user.Id, Name = "Core" };
        db.EvaluationDatasets.Add(dataset);
        db.EvaluationQueries.AddRange(
            new EvaluationQuery { DatasetId = dataset.Id, QueryText = "q1", SortOrder = 0 },
            new EvaluationQuery { DatasetId = dataset.Id, QueryText = "q2", SortOrder = 1 });
        await db.SaveChangesAsync();
        var controller = CreateController(db, user.Id);

        var result = await controller.RunDataset(notebook.Id, new EvaluationRunRequest(dataset.Id, a.Id, b.Id, ["vector", "hybrid"]));

        Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(8, await db.EvaluationResults.CountAsync());
        Assert.Equal(EvaluationRunStatuses.Succeeded, await db.EvaluationRuns.Select(r => r.Status).SingleAsync());
    }

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<User> SeedUser(AppDbContext db)
    {
        var user = new User { Username = Guid.NewGuid().ToString(), PasswordHash = "hash", IsDevAdmin = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<(Notebook, NotebookRetrievalVersion, NotebookRetrievalVersion)> SeedNotebookWithVersions(AppDbContext db, string userId)
    {
        var notebook = new Notebook { UserId = userId, Name = "NB" };
        db.Notebooks.Add(notebook);
        var a = new NotebookRetrievalVersion { NotebookId = notebook.Id, CreatedByUserId = userId, ChunkSize = 800, ChunkOverlap = 100, EmbeddingModel = "m", EmbeddingDimensions = 3 };
        var b = new NotebookRetrievalVersion { NotebookId = notebook.Id, CreatedByUserId = userId, ChunkSize = 900, ChunkOverlap = 100, EmbeddingModel = "m", EmbeddingDimensions = 3 };
        db.NotebookRetrievalVersions.AddRange(a, b);
        await db.SaveChangesAsync();
        return (notebook, a, b);
    }

    private static LabRetrievalBenchController CreateController(AppDbContext db, string userId)
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
        var rag = new RagClient(
            new HttpClient(new FakeRagHandler()) { BaseAddress = new Uri("http://rag-server") },
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RAG_INTERNAL_SECRET"] = "this_is_a_long_test_secret_for_rag_boundary",
            }).Build(),
            accessor);
        return new LabRetrievalBenchController(
            db,
            currentUser,
            new OwnershipService(db, currentUser),
            rag,
            new RetrievalComparisonService())
        {
            ControllerContext = context,
        };
    }

    private sealed class FakeRagHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri?.Query ?? "";
            var version = query.Contains("retrieval_version_id=")
                ? query.Split("retrieval_version_id=")[1].Split('&')[0]
                : "none";
            var payload = version.EndsWith("1")
                ? "{\"results\":[{\"source_id\":\"s1\",\"chunk_index\":0,\"retrieval_version_id\":\"a\",\"text\":\"alpha\"}]}"
                : "{\"results\":[{\"source_id\":\"s1\",\"chunk_index\":0,\"retrieval_version_id\":\"b\",\"text\":\"beta\"},{\"source_id\":\"s2\",\"chunk_index\":1,\"retrieval_version_id\":\"b\",\"text\":\"gamma\"}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload),
            });
        }
    }
}
