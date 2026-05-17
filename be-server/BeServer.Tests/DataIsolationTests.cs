using System.Security.Claims;
using BeServer.Content;
using BeServer.Data;
using BeServer.Data.Entities;
using BeServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BeServer.Tests;

public class DataIsolationTests
{
    [Fact]
    public async Task SourcesList_ReturnsNotFound_ForAnotherUsersNotebook()
    {
        await using var db = CreateDb();
        var owner = await SeedUser(db, "owner");
        var outsider = await SeedUser(db, "outsider");
        var notebook = await SeedNotebook(db, owner.Id);
        var controller = CreateSourcesController(db, outsider.Id);

        var result = await controller.List(notebook.Id);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task NotesList_ReturnsNoRows_ForAnotherUsersNotebook()
    {
        await using var db = CreateDb();
        var owner = await SeedUser(db, "owner");
        var outsider = await SeedUser(db, "outsider");
        var notebook = await SeedNotebook(db, owner.Id);
        db.Notes.Add(new Note { UserId = owner.Id, NotebookId = notebook.Id, Content = "secret" });
        await db.SaveChangesAsync();
        var controller = new NotesController(db) { ControllerContext = ControllerContextFor(outsider.Id) };

        var result = Assert.IsType<OkObjectResult>(await controller.List(notebook.Id));
        var rows = Assert.IsAssignableFrom<IEnumerable<object>>(result.Value);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task Search_ReturnsNotFound_ForAnotherUsersNotebook()
    {
        await using var db = CreateDb();
        var owner = await SeedUser(db, "owner");
        var outsider = await SeedUser(db, "outsider");
        var notebook = await SeedNotebook(db, owner.Id);
        var controller = CreateSearchController(db, outsider.Id);

        var result = await controller.Search(notebook.Id, "secret");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task ChatSessionsList_ReturnsNotFound_ForAnotherUsersNotebook()
    {
        await using var db = CreateDb();
        var owner = await SeedUser(db, "owner");
        var outsider = await SeedUser(db, "outsider");
        var notebook = await SeedNotebook(db, owner.Id);
        var controller = CreateChatSessionsController(db, outsider.Id);

        var result = await controller.List(notebook.Id);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task ExperimentsList_ReturnsNotFound_ForAnotherUsersNotebook()
    {
        await using var db = CreateDb();
        var owner = await SeedUser(db, "owner");
        var outsider = await SeedUser(db, "outsider");
        var notebook = await SeedNotebook(db, owner.Id);
        var context = ControllerContextFor(outsider.Id);
        var accessor = AccessorFor(context);
        var currentUser = new CurrentUserAccessor(accessor);
        var controller = new ExperimentsController(
            new OwnershipService(db, currentUser),
            RagFor(accessor),
            currentUser)
        { ControllerContext = context };

        var result = await controller.List(notebook.Id);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    private static SourcesController CreateSourcesController(AppDbContext db, string userId)
    {
        var context = ControllerContextFor(userId);
        var accessor = AccessorFor(context);
        var currentUser = new CurrentUserAccessor(accessor);
        var config = TestConfig();
        return new SourcesController(
            db,
            RagFor(accessor),
            config,
            currentUser,
            new OwnershipService(db, currentUser))
        { ControllerContext = context };
    }

    private static SearchController CreateSearchController(AppDbContext db, string userId)
    {
        var context = ControllerContextFor(userId);
        var accessor = AccessorFor(context);
        var currentUser = new CurrentUserAccessor(accessor);
        return new SearchController(new OwnershipService(db, currentUser), RagFor(accessor), currentUser)
        { ControllerContext = context };
    }

    private static ChatSessionsController CreateChatSessionsController(AppDbContext db, string userId)
    {
        var context = ControllerContextFor(userId);
        var accessor = AccessorFor(context);
        var currentUser = new CurrentUserAccessor(accessor);
        return new ChatSessionsController(
            db,
            new FakeHttpClientFactory(),
            TestConfig(),
            currentUser,
            new OwnershipService(db, currentUser),
            new ChatMessageService(db),
            new ModelRegistry(TestConfig()),
            NullLogger<ChatSessionsController>.Instance)
        { ControllerContext = context };
    }

    private static RagClient RagFor(IHttpContextAccessor accessor) =>
        new(new HttpClient(new FakeHandler()) { BaseAddress = new Uri("http://rag-server") }, TestConfig(), accessor);

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

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

    private static HttpContextAccessor AccessorFor(ControllerContext context) =>
        new() { HttpContext = context.HttpContext };

    private static ControllerContext ControllerContextFor(string userId) =>
        new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test")),
            },
        };

    private static IConfiguration TestConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RAG_INTERNAL_SECRET"] = "this_is_a_long_test_secret_for_rag_boundary",
            })
            .Build();

    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"results\":[]}"),
            });
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FakeHandler()) { BaseAddress = new Uri("http://ai-server") };
    }
}
