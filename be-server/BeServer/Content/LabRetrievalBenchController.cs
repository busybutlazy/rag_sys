using System.Diagnostics;
using System.Text.Json;
using BeServer.Data;
using BeServer.Data.Entities;
using BeServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Content;

[ApiController]
[Route("api/lab")]
[Authorize(Policy = "DevAdminOnly")]
public class LabRetrievalBenchController(
    AppDbContext db,
    CurrentUserAccessor currentUser,
    OwnershipService ownership,
    RagClient rag,
    RetrievalComparisonService comparison) : ControllerBase
{
    private static readonly string[] ValidModes = ["vector", "bm25", "hybrid"];
    private string UserId => currentUser.UserId;

    [HttpPost("notebooks/{notebookId}/retrieval-bench/compare")]
    public async Task<IActionResult> Compare(string notebookId, [FromBody] RetrievalCompareRequest req)
    {
        var validation = await ValidateComparisonRequest(notebookId, req.RetrievalVersionAId, req.RetrievalVersionBId, req.Modes, req.TopK);
        if (validation is not null)
            return validation;
        if (string.IsNullOrWhiteSpace(req.Query) || req.Query.Trim().Length > 500)
            return ApiErrors.BadRequest(this, "retrieval_bench.invalid_query", "Query is required and must be at most 500 characters.");

        var pairs = new List<object>();
        foreach (var mode in req.Modes)
        {
            var left = await SearchAsync(req.Query.Trim(), notebookId, mode, req.TopK, req.RetrievalVersionAId, req.Alpha);
            var right = await SearchAsync(req.Query.Trim(), notebookId, mode, req.TopK, req.RetrievalVersionBId, req.Alpha);
            pairs.Add(ToComparisonDto(mode, left, right));
        }
        return Ok(new
        {
            Query = req.Query.Trim(),
            req.RetrievalVersionAId,
            req.RetrievalVersionBId,
            Comparisons = pairs,
        });
    }

    [HttpPost("notebooks/{notebookId}/retrieval-bench/runs")]
    public async Task<IActionResult> RunDataset(string notebookId, [FromBody] EvaluationRunRequest req)
    {
        var validation = await ValidateComparisonRequest(notebookId, req.RetrievalVersionAId, req.RetrievalVersionBId, req.Modes, req.TopK);
        if (validation is not null)
            return validation;

        var dataset = await db.EvaluationDatasets
            .Include(d => d.Queries)
            .SingleOrDefaultAsync(d => d.Id == req.DatasetId && d.NotebookId == notebookId && d.UserId == UserId);
        if (dataset is null)
            return ApiErrors.NotFound(this, "evaluation_dataset.not_found", "Evaluation dataset not found");
        var queries = dataset.Queries.OrderBy(q => q.SortOrder).ToList();
        if (queries.Count == 0 || queries.Count > 50)
            return ApiErrors.BadRequest(this, "evaluation_run.invalid_query_count", "Dataset must contain 1 to 50 queries.");

        var run = new EvaluationRun
        {
            NotebookId = notebookId,
            DatasetId = dataset.Id,
            UserId = UserId,
            RetrievalVersionAId = req.RetrievalVersionAId,
            RetrievalVersionBId = req.RetrievalVersionBId,
            SearchModesJson = JsonSerializer.Serialize(req.Modes),
            TopK = req.TopK,
            HybridAlpha = req.Alpha,
            Status = EvaluationRunStatuses.Running,
            StartedAt = DateTime.UtcNow,
        };
        db.EvaluationRuns.Add(run);
        await db.SaveChangesAsync();

        try
        {
            foreach (var query in queries)
            {
                foreach (var mode in req.Modes)
                {
                    var left = await SearchAsync(query.QueryText, notebookId, mode, req.TopK, req.RetrievalVersionAId, req.Alpha);
                    var right = await SearchAsync(query.QueryText, notebookId, mode, req.TopK, req.RetrievalVersionBId, req.Alpha);
                    db.EvaluationResults.AddRange(
                        ToResult(run.Id, query, req.RetrievalVersionAId, mode, left),
                        ToResult(run.Id, query, req.RetrievalVersionBId, mode, right));
                }
            }

            run.Status = EvaluationRunStatuses.Succeeded;
            run.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetRun), new { runId = run.Id }, await BuildRunDto(run.Id));
        }
        catch
        {
            run.Status = EvaluationRunStatuses.Failed;
            run.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            throw;
        }
    }

    [HttpGet("notebooks/{notebookId}/retrieval-bench/runs")]
    public async Task<IActionResult> ListRuns(string notebookId)
    {
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");
        var runs = await db.EvaluationRuns
            .Where(r => r.NotebookId == notebookId && r.UserId == UserId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                r.DatasetId,
                r.RetrievalVersionAId,
                r.RetrievalVersionBId,
                r.Status,
                r.TopK,
                r.HybridAlpha,
                r.CreatedAt,
                r.CompletedAt,
            })
            .ToListAsync();
        return Ok(runs);
    }

    [HttpGet("retrieval-bench/runs/{runId}")]
    public async Task<IActionResult> GetRun(string runId)
    {
        var run = await db.EvaluationRuns.SingleOrDefaultAsync(r => r.Id == runId && r.UserId == UserId);
        if (run is null)
            return ApiErrors.NotFound(this, "evaluation_run.not_found", "Evaluation run not found");
        return Ok(await BuildRunDto(run.Id));
    }

    private async Task<IActionResult?> ValidateComparisonRequest(
        string notebookId,
        string retrievalVersionAId,
        string retrievalVersionBId,
        string[] modes,
        int topK)
    {
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");
        if (topK < 1 || topK > 20)
            return ApiErrors.BadRequest(this, "retrieval_bench.invalid_top_k", "top_k must be between 1 and 20.");
        if (modes.Length == 0 || modes.Length > 3 || modes.Any(m => !ValidModes.Contains(m)))
            return ApiErrors.BadRequest(this, "retrieval_bench.invalid_modes", "Modes must be vector, bm25, or hybrid.");
        var versionIds = await db.NotebookRetrievalVersions
            .Where(v => v.NotebookId == notebookId && (v.Id == retrievalVersionAId || v.Id == retrievalVersionBId))
            .Select(v => v.Id)
            .ToListAsync();
        if (versionIds.Count != 2)
            return ApiErrors.BadRequest(this, "retrieval_bench.version_not_found", "Both retrieval versions must belong to the notebook.");
        return null;
    }

    private async Task<TimedSearch> SearchAsync(string query, string notebookId, string mode, int topK, string retrievalVersionId, double alpha)
    {
        var sw = Stopwatch.StartNew();
        var response = await rag.SearchAsync(query, notebookId, UserId, mode, topK, retrievalVersionId, alpha);
        sw.Stop();
        return new TimedSearch(response.Results, (int)sw.ElapsedMilliseconds);
    }

    private object ToComparisonDto(string mode, TimedSearch left, TimedSearch right) => new
    {
        Mode = mode,
        VersionA = new { left.LatencyMs, Results = left.Results },
        VersionB = new { right.LatencyMs, Results = right.Results },
        Metrics = comparison.Compare(left.Results, right.Results, left.LatencyMs, right.LatencyMs),
    };

    private EvaluationResult ToResult(string runId, EvaluationQuery query, string versionId, string mode, TimedSearch search) =>
        new()
        {
            RunId = runId,
            QueryId = query.Id,
            QueryTextSnapshot = query.QueryText,
            RetrievalVersionId = versionId,
            Mode = mode,
            LatencyMs = search.LatencyMs,
            ResultCount = search.Results.Count,
            ResultsJson = comparison.Snapshot(search.Results),
        };

    private async Task<object> BuildRunDto(string runId)
    {
        var run = await db.EvaluationRuns.SingleAsync(r => r.Id == runId);
        var results = await db.EvaluationResults
            .Where(r => r.RunId == runId)
            .OrderBy(r => r.QueryTextSnapshot)
            .ThenBy(r => r.Mode)
            .ThenBy(r => r.RetrievalVersionId)
            .ToListAsync();

        var comparisons = results
            .GroupBy(r => new { r.QueryId, r.QueryTextSnapshot, r.Mode })
            .Select(g =>
            {
                var left = g.Single(r => r.RetrievalVersionId == run.RetrievalVersionAId);
                var right = g.Single(r => r.RetrievalVersionId == run.RetrievalVersionBId);
                var leftRows = ParseSnapshots(left.ResultsJson);
                var rightRows = ParseSnapshots(right.ResultsJson);
                return new
                {
                    g.Key.QueryId,
                    g.Key.QueryTextSnapshot,
                    g.Key.Mode,
                    VersionA = ToStoredResultDto(left),
                    VersionB = ToStoredResultDto(right),
                    Metrics = comparison.Compare(
                        leftRows.Select(ToChunk).ToList(),
                        rightRows.Select(ToChunk).ToList(),
                        left.LatencyMs,
                        right.LatencyMs),
                };
            })
            .ToList();

        return new
        {
            run.Id,
            run.NotebookId,
            run.DatasetId,
            run.RetrievalVersionAId,
            run.RetrievalVersionBId,
            run.SearchModesJson,
            run.TopK,
            run.HybridAlpha,
            run.Status,
            run.StartedAt,
            run.CompletedAt,
            run.CreatedAt,
            Comparisons = comparisons,
        };
    }

    private static List<RetrievalResultSnapshot> ParseSnapshots(string json) =>
        JsonSerializer.Deserialize<List<RetrievalResultSnapshot>>(json) ?? [];

    private static RagChunkResult ToChunk(RetrievalResultSnapshot s) =>
        new(s.SourceId, s.ChunkIndex, s.RetrievalVersionId, s.TextPreview);

    private static object ToStoredResultDto(EvaluationResult r) => new
    {
        r.Id,
        r.QueryId,
        r.QueryTextSnapshot,
        r.RetrievalVersionId,
        r.Mode,
        r.LatencyMs,
        r.ResultCount,
        Results = ParseSnapshots(r.ResultsJson),
        r.CreatedAt,
    };
}

public record RetrievalCompareRequest(
    string Query,
    string RetrievalVersionAId,
    string RetrievalVersionBId,
    string[] Modes,
    int TopK = 5,
    double Alpha = 0.5);
public record EvaluationRunRequest(
    string DatasetId,
    string RetrievalVersionAId,
    string RetrievalVersionBId,
    string[] Modes,
    int TopK = 5,
    double Alpha = 0.5);
public record TimedSearch(List<RagChunkResult> Results, int LatencyMs);
