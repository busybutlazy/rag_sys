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

    [Fact]
    public async Task RunDetail_KeepsDuplicateQueryTextsDistinct()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db);
        var (notebook, a, b) = await SeedNotebookWithVersions(db, user.Id);
        var dataset = new EvaluationDataset { NotebookId = notebook.Id, UserId = user.Id, Name = "Core" };
        db.EvaluationDatasets.Add(dataset);
        db.EvaluationQueries.AddRange(
            new EvaluationQuery { DatasetId = dataset.Id, QueryText = "same", SortOrder = 0 },
            new EvaluationQuery { DatasetId = dataset.Id, QueryText = "same", SortOrder = 1 });
        await db.SaveChangesAsync();
        var controller = CreateController(db, user.Id);
        var created = Assert.IsType<CreatedAtActionResult>(await controller.RunDataset(
            notebook.Id,
            new EvaluationRunRequest(dataset.Id, a.Id, b.Id, ["hybrid"])));
        var runId = await db.EvaluationRuns.Select(r => r.Id).SingleAsync();

        var result = Assert.IsType<OkObjectResult>(await controller.GetRun(runId));
        var json = JsonSerializer.Serialize(result.Value);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(2, doc.RootElement.GetProperty("Comparisons").GetArrayLength());
        Assert.NotNull(created.Value);
    }

    [Fact]
    public async Task Compare_WithGraphHybridMode_ReturnsGraphMetrics()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db);
        var (notebook, a, b) = await SeedGraphTestVersions(db, user.Id);
        var controller = CreateController(db, user.Id);

        var result = Assert.IsType<OkObjectResult>(await controller.Compare(
            notebook.Id,
            new RetrievalCompareRequest("hello", a.Id, b.Id, ["graph_hybrid"])));
        var json = JsonSerializer.Serialize(result.Value);
        using var doc = JsonDocument.Parse(json);
        var metrics = doc.RootElement.GetProperty("Comparisons")[0].GetProperty("Metrics");

        // Version A's fake response has no fact_id; version B's has one
        // result backed by a fact out of two total.
        Assert.Equal(0.0, metrics.GetProperty("GraphHitRateA").GetDouble());
        Assert.Equal(0.5, metrics.GetProperty("GraphHitRateB").GetDouble());
        Assert.Equal(0, metrics.GetProperty("FactCoverageA").GetInt32());
        Assert.Equal(1, metrics.GetProperty("FactCoverageB").GetInt32());
    }

    [Fact]
    public async Task Compare_WithNonGraphMode_ReportsZeroGraphMetrics()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db);
        var (notebook, a, b) = await SeedGraphTestVersions(db, user.Id);
        var controller = CreateController(db, user.Id);

        var result = Assert.IsType<OkObjectResult>(await controller.Compare(
            notebook.Id,
            new RetrievalCompareRequest("hello", a.Id, b.Id, ["hybrid"])));
        var json = JsonSerializer.Serialize(result.Value);
        using var doc = JsonDocument.Parse(json);
        var metrics = doc.RootElement.GetProperty("Comparisons")[0].GetProperty("Metrics");

        Assert.Equal(0.0, metrics.GetProperty("GraphHitRateA").GetDouble());
        Assert.Equal(0.0, metrics.GetProperty("GraphHitRateB").GetDouble());
        Assert.Equal(0, metrics.GetProperty("FactCoverageA").GetInt32());
        Assert.Equal(0, metrics.GetProperty("FactCoverageB").GetInt32());
    }

    [Fact]
    public async Task RunDataset_WithGraphHybridMode_PersistsFactProvenanceAndReopensIdentically()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db);
        var (notebook, a, b) = await SeedGraphTestVersions(db, user.Id);
        var dataset = new EvaluationDataset { NotebookId = notebook.Id, UserId = user.Id, Name = "Core" };
        db.EvaluationDatasets.Add(dataset);
        db.EvaluationQueries.Add(new EvaluationQuery { DatasetId = dataset.Id, QueryText = "q1", SortOrder = 0 });
        await db.SaveChangesAsync();
        var controller = CreateController(db, user.Id);

        var created = Assert.IsType<CreatedAtActionResult>(await controller.RunDataset(
            notebook.Id, new EvaluationRunRequest(dataset.Id, a.Id, b.Id, ["graph_hybrid"])));
        var createdMetrics = JsonSerializer.Serialize(created.Value);
        var runId = await db.EvaluationRuns.Select(r => r.Id).SingleAsync();

        var reopened = Assert.IsType<OkObjectResult>(await controller.GetRun(runId));
        var reopenedJson = JsonSerializer.Serialize(reopened.Value);
        using var doc = JsonDocument.Parse(reopenedJson);
        var metrics = doc.RootElement.GetProperty("Comparisons")[0].GetProperty("Metrics");

        Assert.Contains("f1", createdMetrics, StringComparison.Ordinal);
        Assert.Equal(0.5, metrics.GetProperty("GraphHitRateB").GetDouble());
        Assert.Equal(1, metrics.GetProperty("FactCoverageB").GetInt32());
        var versionBResults = doc.RootElement.GetProperty("Comparisons")[0].GetProperty("VersionB").GetProperty("Results");
        Assert.Equal("f1", versionBResults[0].GetProperty("FactId").GetString());
        Assert.Equal("Beta supports Gamma", versionBResults[0].GetProperty("FactText").GetString());
    }

    private static async Task<(Notebook, NotebookRetrievalVersion, NotebookRetrievalVersion)> SeedGraphTestVersions(AppDbContext db, string userId)
    {
        // Explicit, deterministic ids -- FakeRagHandler keys its canned graph_hybrid
        // payload off "version-graph-b" specifically, unlike the generic
        // SeedNotebookWithVersions helper whose random-guid ids only happen to
        // satisfy the (content-insensitive) older tests.
        var notebook = new Notebook { UserId = userId, Name = "NB-graph" };
        db.Notebooks.Add(notebook);
        var a = new NotebookRetrievalVersion { Id = "version-graph-a", NotebookId = notebook.Id, CreatedByUserId = userId, ChunkSize = 800, ChunkOverlap = 100, EmbeddingModel = "m", EmbeddingDimensions = 3 };
        var b = new NotebookRetrievalVersion { Id = "version-graph-b", NotebookId = notebook.Id, CreatedByUserId = userId, ChunkSize = 900, ChunkOverlap = 100, EmbeddingModel = "m", EmbeddingDimensions = 3 };
        db.NotebookRetrievalVersions.AddRange(a, b);
        await db.SaveChangesAsync();
        return (notebook, a, b);
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
            var path = request.RequestUri?.AbsolutePath ?? "";
            var query = request.RequestUri?.Query ?? "";
            var version = query.Contains("retrieval_version_id=")
                ? query.Split("retrieval_version_id=")[1].Split('&')[0]
                : "none";
            var isGraphHybrid = path.EndsWith("/search/graph_hybrid");
            var isVersionB = version is "version-graph-b" || (version != "version-graph-a" && !version.EndsWith("1"));
            var payload = !isVersionB
                ? "{\"results\":[{\"source_id\":\"s1\",\"chunk_index\":0,\"retrieval_version_id\":\"a\",\"text\":\"alpha\"}]}"
                : (isGraphHybrid
                    ? "{\"results\":[{\"source_id\":\"s1\",\"chunk_index\":0,\"retrieval_version_id\":\"b\",\"text\":\"beta\",\"fact_id\":\"f1\",\"fact_text\":\"Beta supports Gamma\",\"participants\":[\"beta\",\"gamma\"]},{\"source_id\":\"s2\",\"chunk_index\":1,\"retrieval_version_id\":\"b\",\"text\":\"gamma\"}]}"
                    : "{\"results\":[{\"source_id\":\"s1\",\"chunk_index\":0,\"retrieval_version_id\":\"b\",\"text\":\"beta\"},{\"source_id\":\"s2\",\"chunk_index\":1,\"retrieval_version_id\":\"b\",\"text\":\"gamma\"}]}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload),
            });
        }
    }
}
