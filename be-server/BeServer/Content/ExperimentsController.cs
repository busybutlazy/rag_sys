using BeServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BeServer.Content;

[ApiController]
[Route("api/notebooks/{notebookId}/experiments")]
[Authorize]
[EnableRateLimiting("write")]
public class ExperimentsController(OwnershipService ownership, RagClient rag) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string notebookId, [FromQuery] int limit = 20)
    {
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");
        return Ok(await rag.ListExperimentsAsync(notebookId, Math.Clamp(limit, 1, 100)));
    }

    [HttpGet("{experimentId}")]
    public async Task<IActionResult> Get(string notebookId, string experimentId)
    {
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");
        return Ok(await rag.GetExperimentAsync(notebookId, experimentId));
    }

    [HttpPost]
    public async Task<IActionResult> Run(string notebookId, [FromBody] ExperimentRunRequest req)
    {
        if (!await ownership.NotebookExistsAsync(notebookId))
            return ApiErrors.NotFound(this, "notebook.not_found", "Notebook not found");
        if (req.Queries.Length == 0 || req.Queries.Length > 20)
            return ApiErrors.BadRequest(this, "experiment.invalid_query_count", "Provide 1 to 20 queries.");
        if (req.Queries.Any(q => string.IsNullOrWhiteSpace(q) || q.Length > 500))
            return ApiErrors.BadRequest(this, "experiment.invalid_query", "Queries must be non-empty and at most 500 characters.");

        var config = req.Config ?? new ExperimentConfig(["vector", "bm25", "hybrid"]);
        return Ok(await rag.RunExperimentAsync(notebookId, req with { Config = config }));
    }
}
