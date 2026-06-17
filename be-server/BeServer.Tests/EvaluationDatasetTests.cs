using System.Security.Claims;
using BeServer.Content;
using BeServer.Data;
using BeServer.Data.Entities;
using BeServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BeServer.Tests;

public class EvaluationDatasetTests
{
    [Fact]
    public async Task CreateDataset_ThenAddQuery_PreservesOrdering()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db);
        var notebook = await SeedNotebook(db, user.Id);
        var controller = CreateController(db, user.Id);

        var created = await controller.Create(notebook.Id, new CreateEvaluationDatasetRequest("Core", "seed set"));
        var dataset = await db.EvaluationDatasets.SingleAsync();
        await controller.AddQuery(dataset.Id, new UpsertEvaluationQueryRequest("first?", null, null));
        await controller.AddQuery(dataset.Id, new UpsertEvaluationQueryRequest("second?", null, null));

        Assert.IsType<CreatedAtActionResult>(created);
        Assert.Equal([0, 1], await db.EvaluationQueries.OrderBy(q => q.SortOrder).Select(q => q.SortOrder).ToListAsync());
    }

    [Fact]
    public async Task GetDataset_ReturnsQueriesInSortOrder()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db);
        var notebook = await SeedNotebook(db, user.Id);
        var dataset = new EvaluationDataset { NotebookId = notebook.Id, UserId = user.Id, Name = "Core" };
        db.EvaluationDatasets.Add(dataset);
        db.EvaluationQueries.AddRange(
            new EvaluationQuery { DatasetId = dataset.Id, QueryText = "later", SortOrder = 2 },
            new EvaluationQuery { DatasetId = dataset.Id, QueryText = "earlier", SortOrder = 1 });
        await db.SaveChangesAsync();
        var controller = CreateController(db, user.Id);

        var result = Assert.IsType<OkObjectResult>(await controller.Get(dataset.Id));
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);

        Assert.True(json.IndexOf("earlier", StringComparison.Ordinal) < json.IndexOf("later", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnotherUser_CannotReadDataset()
    {
        await using var db = CreateDb();
        var owner = await SeedUser(db);
        var outsider = await SeedUser(db);
        var notebook = await SeedNotebook(db, owner.Id);
        var dataset = new EvaluationDataset { NotebookId = notebook.Id, UserId = owner.Id, Name = "Core" };
        db.EvaluationDatasets.Add(dataset);
        await db.SaveChangesAsync();

        var controller = CreateController(db, outsider.Id);
        var result = await controller.Get(dataset.Id);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task AddQuery_RejectsBlankText()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db);
        var notebook = await SeedNotebook(db, user.Id);
        var dataset = new EvaluationDataset { NotebookId = notebook.Id, UserId = user.Id, Name = "Core" };
        db.EvaluationDatasets.Add(dataset);
        await db.SaveChangesAsync();
        var controller = CreateController(db, user.Id);

        var result = await controller.AddQuery(dataset.Id, new UpsertEvaluationQueryRequest("   ", null, null));

        Assert.IsType<BadRequestObjectResult>(result);
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

    private static async Task<Notebook> SeedNotebook(AppDbContext db, string userId)
    {
        var notebook = new Notebook { UserId = userId, Name = "NB" };
        db.Notebooks.Add(notebook);
        await db.SaveChangesAsync();
        return notebook;
    }

    private static LabEvaluationDatasetsController CreateController(AppDbContext db, string userId)
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
        return new LabEvaluationDatasetsController(db, currentUser, new OwnershipService(db, currentUser))
        {
            ControllerContext = context,
        };
    }
}
