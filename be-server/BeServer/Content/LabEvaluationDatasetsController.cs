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
public class LabEvaluationDatasetsController(
    AppDbContext db,
    CurrentUserAccessor currentUser,
    OwnershipService ownership) : ControllerBase
{
    private string UserId => currentUser.UserId;

    [HttpGet("notebooks/{notebookId}/evaluation-datasets")]
    public async Task<IActionResult> List(string notebookId)
    {
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");

        var datasets = await db.EvaluationDatasets
            .Where(d => d.NotebookId == notebookId && d.UserId == UserId)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync();
        return Ok(datasets.Select(ToSummaryDto));
    }

    [HttpPost("notebooks/{notebookId}/evaluation-datasets")]
    public async Task<IActionResult> Create(string notebookId, [FromBody] CreateEvaluationDatasetRequest req)
    {
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 160)
            return ApiErrors.BadRequest(this, "evaluation_dataset.invalid_name", "Name is required and must be at most 160 characters.");

        var dataset = new EvaluationDataset
        {
            NotebookId = notebookId,
            UserId = UserId,
            Name = req.Name.Trim(),
            Description = req.Description?.Trim(),
        };
        db.EvaluationDatasets.Add(dataset);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { datasetId = dataset.Id }, ToDetailDto(dataset, []));
    }

    [HttpGet("evaluation-datasets/{datasetId}")]
    public async Task<IActionResult> Get(string datasetId)
    {
        var dataset = await db.EvaluationDatasets
            .Include(d => d.Queries)
            .SingleOrDefaultAsync(d => d.Id == datasetId && d.UserId == UserId);
        if (dataset is null)
            return ApiErrors.NotFound(this, "evaluation_dataset.not_found", "Evaluation dataset not found");
        return Ok(ToDetailDto(dataset, dataset.Queries.OrderBy(q => q.SortOrder)));
    }

    [HttpPost("evaluation-datasets/{datasetId}/queries")]
    public async Task<IActionResult> AddQuery(string datasetId, [FromBody] UpsertEvaluationQueryRequest req)
    {
        var dataset = await db.EvaluationDatasets
            .Include(d => d.Queries)
            .SingleOrDefaultAsync(d => d.Id == datasetId && d.UserId == UserId);
        if (dataset is null)
            return ApiErrors.NotFound(this, "evaluation_dataset.not_found", "Evaluation dataset not found");
        if (!ValidQueryText(req.QueryText))
            return ApiErrors.BadRequest(this, "evaluation_query.invalid_text", "Query text is required and must be at most 500 characters.");

        var query = new EvaluationQuery
        {
            DatasetId = dataset.Id,
            QueryText = req.QueryText.Trim(),
            ExpectedAnswerNotes = req.ExpectedAnswerNotes?.Trim(),
            GoldSourceNotes = req.GoldSourceNotes?.Trim(),
            SortOrder = dataset.Queries.Count == 0 ? 0 : dataset.Queries.Max(q => q.SortOrder) + 1,
        };
        dataset.Queries.Add(query);
        dataset.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { datasetId = dataset.Id }, ToQueryDto(query));
    }

    [HttpPut("evaluation-queries/{queryId}")]
    public async Task<IActionResult> UpdateQuery(string queryId, [FromBody] UpsertEvaluationQueryRequest req)
    {
        if (!ValidQueryText(req.QueryText))
            return ApiErrors.BadRequest(this, "evaluation_query.invalid_text", "Query text is required and must be at most 500 characters.");

        var query = await db.EvaluationQueries
            .Include(q => q.Dataset)
            .SingleOrDefaultAsync(q => q.Id == queryId && q.Dataset.UserId == UserId);
        if (query is null)
            return ApiErrors.NotFound(this, "evaluation_query.not_found", "Evaluation query not found");

        query.QueryText = req.QueryText.Trim();
        query.ExpectedAnswerNotes = req.ExpectedAnswerNotes?.Trim();
        query.GoldSourceNotes = req.GoldSourceNotes?.Trim();
        query.UpdatedAt = DateTime.UtcNow;
        query.Dataset.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToQueryDto(query));
    }

    [HttpDelete("evaluation-queries/{queryId}")]
    public async Task<IActionResult> DeleteQuery(string queryId)
    {
        var query = await db.EvaluationQueries
            .Include(q => q.Dataset)
            .SingleOrDefaultAsync(q => q.Id == queryId && q.Dataset.UserId == UserId);
        if (query is null)
            return ApiErrors.NotFound(this, "evaluation_query.not_found", "Evaluation query not found");

        query.Dataset.UpdatedAt = DateTime.UtcNow;
        db.EvaluationQueries.Remove(query);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static bool ValidQueryText(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= 500;

    private static object ToSummaryDto(EvaluationDataset d) => new
    {
        d.Id,
        d.NotebookId,
        d.Name,
        d.Description,
        d.CreatedAt,
        d.UpdatedAt,
    };

    private static object ToDetailDto(EvaluationDataset d, IEnumerable<EvaluationQuery> queries) => new
    {
        d.Id,
        d.NotebookId,
        d.Name,
        d.Description,
        d.CreatedAt,
        d.UpdatedAt,
        Queries = queries.Select(ToQueryDto),
    };

    private static object ToQueryDto(EvaluationQuery q) => new
    {
        q.Id,
        q.DatasetId,
        q.QueryText,
        q.ExpectedAnswerNotes,
        q.GoldSourceNotes,
        q.SortOrder,
        q.CreatedAt,
        q.UpdatedAt,
    };
}

public record CreateEvaluationDatasetRequest(string Name, string? Description);
public record UpsertEvaluationQueryRequest(string QueryText, string? ExpectedAnswerNotes, string? GoldSourceNotes);
