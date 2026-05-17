using BeServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Content;

[ApiController]
[Route("api/notebooks/{notebookId}/search")]
[Authorize]
public class SearchController(OwnershipService ownership, RagClient rag, CurrentUserAccessor currentUser, BeServer.Data.AppDbContext db) : ControllerBase
{
    private static readonly string[] ValidModes = ["vector", "bm25", "hybrid"];

    [HttpGet]
    public async Task<IActionResult> Search(
        string notebookId,
        [FromQuery] string q,
        [FromQuery] string? mode = null,
        [FromQuery] int? topK = null)
    {
        if (string.IsNullOrWhiteSpace(q))
            return ApiErrors.BadRequest(this, "search.query_required", "q is required");
        var activeVersionId = await db.Notebooks
            .Where(n => n.Id == notebookId && n.UserId == currentUser.UserId)
            .Select(n => n.ActiveRetrievalVersionId)
            .SingleOrDefaultAsync();
        var version = activeVersionId is null ? null : await db.NotebookRetrievalVersions.SingleOrDefaultAsync(v => v.Id == activeVersionId);
        var effectiveMode = mode ?? version?.DefaultSearchMode ?? "hybrid";
        var effectiveTopK = topK ?? version?.DefaultTopK ?? 5;
        if (!ValidModes.Contains(effectiveMode))
            return ApiErrors.BadRequest(this, "search.invalid_mode", "mode must be vector, bm25, or hybrid");
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");

        return Ok(await rag.SearchAsync(q, notebookId, currentUser.UserId, effectiveMode, effectiveTopK, activeVersionId));
    }

    [HttpGet("benchmark")]
    public async Task<IActionResult> Benchmark(
        string notebookId,
        [FromQuery] string q,
        [FromQuery] int topK = 5)
    {
        if (string.IsNullOrWhiteSpace(q))
            return ApiErrors.BadRequest(this, "search.query_required", "q is required");
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");

        var activeVersionId = await db.Notebooks
            .Where(n => n.Id == notebookId && n.UserId == currentUser.UserId)
            .Select(n => n.ActiveRetrievalVersionId)
            .SingleOrDefaultAsync();

        return Ok(await rag.BenchmarkAsync(q, notebookId, currentUser.UserId, topK, activeVersionId));
    }
}
