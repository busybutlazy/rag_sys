using System.Security.Claims;
using BeServer.Content;
using BeServer.Data;
using BeServer.Data.Entities;
using BeServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BeServer.Tests;

public class NotebookAndSearchControllerTests
{
    [Fact]
    public async Task NotebookGet_ReturnsNotFound_ForAnotherUsersNotebook()
    {
        await using var db = CreateDb();
        var owner = await SeedUser(db, "owner");
        var outsider = await SeedUser(db, "outsider");
        var notebook = await SeedNotebook(db, owner.Id);
        var controller = CreateNotebooksController(db, outsider.Id);

        var result = await controller.Get(notebook.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Search_RejectsBlankQuery()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, "user");
        var notebook = await SeedNotebook(db, user.Id);
        var controller = CreateSearchController(db, user.Id);

        var result = await controller.Search(notebook.Id, "   ");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Search_RejectsInvalidMode()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, "user");
        var notebook = await SeedNotebook(db, user.Id);
        var controller = CreateSearchController(db, user.Id);

        var result = await controller.Search(notebook.Id, "hello", "bogus");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<User> SeedUser(AppDbContext db, string username)
    {
        var user = new User { Username = username, PasswordHash = "hash" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<Notebook> SeedNotebook(AppDbContext db, string userId)
    {
        var notebook = new Notebook { UserId = userId, Name = "Notebook" };
        db.Notebooks.Add(notebook);
        await db.SaveChangesAsync();
        return notebook;
    }

    private static NotebooksController CreateNotebooksController(AppDbContext db, string userId)
    {
        var context = ControllerContextFor(userId);
        var accessor = new HttpContextAccessor { HttpContext = context.HttpContext };
        var controller = new NotebooksController(db, new CurrentUserAccessor(accessor));
        controller.ControllerContext = context;
        return controller;
    }

    private static SearchController CreateSearchController(AppDbContext db, string userId)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RAG_INTERNAL_SECRET"] = "this_is_a_long_test_secret_for_rag_boundary",
            })
            .Build();
        var context = ControllerContextFor(userId);
        var accessor = new HttpContextAccessor { HttpContext = context.HttpContext };
        var currentUser = new CurrentUserAccessor(accessor);
        var rag = new RagClient(new HttpClient(new FakeHandler()) { BaseAddress = new Uri("http://rag-server") }, config, accessor);
        var controller = new SearchController(new OwnershipService(db, currentUser), rag);
        controller.ControllerContext = context;
        return controller;
    }

    private static ControllerContext ControllerContextFor(string userId) =>
        new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId),
                ], "test")),
            },
        };

    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"results\":[]}"),
            });
    }
}
