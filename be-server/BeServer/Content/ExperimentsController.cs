using System.Security.Claims;
using BeServer.Data;
using BeServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace BeServer.Content;

[ApiController]
[Route("api/notebooks/{notebookId}/experiments")]
[Authorize]
[EnableRateLimiting("write")]
public class ExperimentsController(AppDbContext db, RagClient rag) : ControllerBase
{
    private string UserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "";

    [HttpGet]
    public async Task<IActionResult> List(string notebookId, [FromQuery] int limit = 20)
    {
        if (!await OwnsNotebook(notebookId)) return NotFound();
        var json = await rag.ListExperimentsAsync(notebookId, Math.Clamp(limit, 1, 100));
        return Content(json, "application/json");
    }

    [HttpGet("{experimentId}")]
    public async Task<IActionResult> Get(string notebookId, string experimentId)
    {
        if (!await OwnsNotebook(notebookId)) return NotFound();
        var json = await rag.GetExperimentAsync(notebookId, experimentId);
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> Run(string notebookId, [FromBody] ExperimentRunRequest req)
    {
        if (!await OwnsNotebook(notebookId)) return NotFound();
        if (req.Queries.Length == 0 || req.Queries.Length > 20)
            return BadRequest(new { error = "Provide 1 to 20 queries." });
        if (req.Queries.Any(q => string.IsNullOrWhiteSpace(q) || q.Length > 500))
            return BadRequest(new { error = "Queries must be non-empty and at most 500 characters." });

        var json = await rag.RunExperimentAsync(notebookId, req);
        return Content(json, "application/json");
    }

    private Task<bool> OwnsNotebook(string notebookId) =>
        db.Notebooks.AnyAsync(n => n.Id == notebookId && n.UserId == UserId && !n.Archived);
}
