using System.Security.Claims;
using BeServer.Data;
using BeServer.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Content;

[ApiController]
[Route("api/notebooks")]
[Authorize]
public class NotebooksController(AppDbContext db) : ControllerBase
{
    private string UserId
    {
        get
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(id))
                throw new InvalidOperationException("JWT is missing user identity claim.");
            return id;
        }
    }

    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await db.Notebooks
            .Where(n => n.UserId == UserId && !n.Archived)
            .OrderByDescending(n => n.UpdatedAt)
            .Select(n => new { n.Id, n.Name, n.Description, n.CreatedAt, n.UpdatedAt })
            .ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] NotebookRequest req)
    {
        var nb = new Notebook { UserId = UserId, Name = req.Name, Description = req.Description };
        db.Notebooks.Add(nb);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = nb.Id }, nb);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var nb = await db.Notebooks
            .Where(n => n.Id == id && n.UserId == UserId && !n.Archived)
            .Select(n => new
            {
                n.Id, n.Name, n.Description, n.Archived, n.CreatedAt, n.UpdatedAt,
                Sources = n.Sources.Select(s => new { s.Id, s.Title, s.MimeType, s.Status, s.CreatedAt }),
                Notes = n.Notes.Select(nt => new { nt.Id, nt.Title, nt.NoteType, nt.CreatedAt }),
            })
            .FirstOrDefaultAsync();
        return nb is null ? NotFound() : Ok(nb);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] NotebookRequest req)
    {
        var nb = await db.Notebooks.FirstOrDefaultAsync(n => n.Id == id && n.UserId == UserId && !n.Archived);
        if (nb is null) return NotFound();
        nb.Name = req.Name;
        nb.Description = req.Description;
        nb.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(nb);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Archive(string id)
    {
        var nb = await db.Notebooks.FirstOrDefaultAsync(n => n.Id == id && n.UserId == UserId);
        if (nb is null) return NotFound();
        nb.Archived = true;
        nb.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record NotebookRequest(string Name, string? Description);
