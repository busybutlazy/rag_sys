using System.Security.Claims;
using BeServer.Data;
using BeServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Content;

[ApiController]
[Route("api/notebooks/{notebookId}/search")]
[Authorize]
public class SearchController(AppDbContext db, RagClient rag) : ControllerBase
{
    private string UserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "";

    private static readonly string[] ValidModes = ["vector", "bm25", "hybrid"];

    [HttpGet]
    public async Task<IActionResult> Search(
        string notebookId,
        [FromQuery] string q,
        [FromQuery] string mode = "hybrid",
        [FromQuery] int topK = 5)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "q is required" });
        if (!ValidModes.Contains(mode))
            return BadRequest(new { error = "mode must be vector, bm25, or hybrid" });

        var exists = await db.Notebooks.AnyAsync(n => n.Id == notebookId && n.UserId == UserId && !n.Archived);
        if (!exists) return NotFound();

        var json = await rag.SearchAsync(q, notebookId, mode, topK);
        return Content(json, "application/json");
    }

    [HttpGet("benchmark")]
    public async Task<IActionResult> Benchmark(
        string notebookId,
        [FromQuery] string q,
        [FromQuery] int topK = 5)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "q is required" });

        var exists = await db.Notebooks.AnyAsync(n => n.Id == notebookId && n.UserId == UserId && !n.Archived);
        if (!exists) return NotFound();

        var json = await rag.BenchmarkAsync(q, notebookId, topK);
        return Content(json, "application/json");
    }
}
