using System.Security.Claims;
using BeServer.Data;
using BeServer.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Content;

[ApiController]
[Route("api/notebooks/{notebookId}/notes")]
[Authorize]
public class NotesController(AppDbContext db) : ControllerBase
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
    public async Task<IActionResult> List(string notebookId) =>
        Ok(await db.Notes
            .Where(n => n.NotebookId == notebookId && n.UserId == UserId)
            .OrderByDescending(n => n.UpdatedAt)
            .Select(n => new { n.Id, n.Title, n.NoteType, n.CreatedAt, n.UpdatedAt })
            .ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create(string notebookId, [FromBody] NoteRequest req)
    {
        var nb = await db.Notebooks.FirstOrDefaultAsync(n => n.Id == notebookId && n.UserId == UserId);
        if (nb is null) return NotFound(new { error = "Notebook not found" });

        var note = new Note
        {
            UserId = UserId,
            NotebookId = notebookId,
            Title = req.Title,
            Content = req.Content,
        };
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { notebookId, id = note.Id }, note);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string notebookId, string id)
    {
        var note = await db.Notes.FirstOrDefaultAsync(
            n => n.Id == id && n.NotebookId == notebookId && n.UserId == UserId);
        return note is null ? NotFound() : Ok(note);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string notebookId, string id, [FromBody] NoteRequest req)
    {
        var note = await db.Notes.FirstOrDefaultAsync(
            n => n.Id == id && n.NotebookId == notebookId && n.UserId == UserId);
        if (note is null) return NotFound();
        note.Title = req.Title;
        note.Content = req.Content;
        note.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(note);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string notebookId, string id)
    {
        var note = await db.Notes.FirstOrDefaultAsync(
            n => n.Id == id && n.NotebookId == notebookId && n.UserId == UserId);
        if (note is null) return NotFound();
        db.Notes.Remove(note);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record NoteRequest(string? Title, string Content);
