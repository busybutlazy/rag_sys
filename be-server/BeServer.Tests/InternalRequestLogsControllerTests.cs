using BeServer.Content;
using BeServer.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BeServer.Tests;

public class InternalRequestLogsControllerTests
{
    [Fact]
    public async Task Create_RejectsMissingInternalSecret()
    {
        await using var db = CreateDb();
        var controller = CreateController(db);

        var result = await controller.Create(new InternalRequestLogRequest(null, null, null, null, null, null, null, null, null, null, null, null));

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Create_RejectsInvalidInternalSecret()
    {
        await using var db = CreateDb();
        var controller = CreateController(db);
        controller.Request.Headers["X-Internal-Secret"] = "wrong-secret";

        var result = await controller.Create(new InternalRequestLogRequest(null, null, null, null, null, null, null, null, null, null, null, null));

        Assert.IsType<UnauthorizedResult>(result);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static InternalRequestLogsController CreateController(AppDbContext db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI_INTERNAL_SECRET"] = "this_is_a_long_test_secret_for_ai_boundary",
            })
            .Build();
        return new InternalRequestLogsController(db, config)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
    }
}
