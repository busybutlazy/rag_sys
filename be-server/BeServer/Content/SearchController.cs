using BeServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeServer.Content;

[ApiController]
[Route("api/notebooks/{notebookId}/search")]
[Authorize]
public class SearchController(OwnershipService ownership, RagClient rag, CurrentUserAccessor currentUser) : ControllerBase
{
    private static readonly string[] ValidModes = ["vector", "bm25", "hybrid"];

    [HttpGet]
    public async Task<IActionResult> Search(
        string notebookId,
        [FromQuery] string q,
        [FromQuery] string mode = "hybrid",
        [FromQuery] int topK = 5)
    {
        if (string.IsNullOrWhiteSpace(q))
            return ApiErrors.BadRequest(this, "search.query_required", "q is required");
        if (!ValidModes.Contains(mode))
            return ApiErrors.BadRequest(this, "search.invalid_mode", "mode must be vector, bm25, or hybrid");
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");

        return Ok(await rag.SearchAsync(q, notebookId, currentUser.UserId, mode, topK));
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

        return Ok(await rag.BenchmarkAsync(q, notebookId, currentUser.UserId, topK));
    }
}
