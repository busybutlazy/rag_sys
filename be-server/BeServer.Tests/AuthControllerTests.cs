using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using AppDbContext = global::BeServer.Data.AppDbContext;
using AuthController = global::BeServer.Auth.AuthController;
using JwtConstants = global::BeServer.Auth.JwtConstants;
using JwtService = global::BeServer.Auth.JwtService;
using LoginRequest = global::BeServer.Auth.LoginRequest;
using User = global::BeServer.Data.Entities.User;

namespace BeServer.Tests;

public class AuthControllerTests
{
    private const string Secret = "test_secret_32_characters_minimum_value";

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAccessTokenAndRefreshCookie()
    {
        await using var db = CreateDb();
        await SeedUser(db);
        var controller = CreateController(db);

        var result = await controller.Login(new LoginRequest("admin", "password"));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.False(string.IsNullOrWhiteSpace(GetString(ok.Value, "accessToken")));
        Assert.NotNull(ReadRefreshCookie(controller));
        Assert.Equal(1, await db.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        await using var db = CreateDb();
        await SeedUser(db);
        var controller = CreateController(db);

        var result = await controller.Login(new LoginRequest("admin", "wrong"));

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(0, await db.RefreshTokens.CountAsync());
    }

    [Fact]
    public void ValidateAccessToken_WithExpiredToken_ReturnsNull()
    {
        var jwt = CreateJwtService();
        var expired = CreateExpiredToken();

        var principal = jwt.ValidateAccessToken(expired);

        Assert.Null(principal);
    }

    [Fact]
    public void GenerateAccessToken_IncludesDevAdminClaim()
    {
        var principal = CreateJwtService().ValidateAccessToken(
            CreateJwtService().GenerateAccessToken("user-1", "admin", isDevAdmin: true));

        Assert.Equal("true", principal?.FindFirst("dev_admin")?.Value);
    }

    [Fact]
    public async Task Refresh_RotatesTokenAndRevokesFamilyOnReuse()
    {
        await using var db = CreateDb();
        await SeedUser(db);
        var loginController = CreateController(db);
        var login = await loginController.Login(new LoginRequest("admin", "password"));
        Assert.IsType<OkObjectResult>(login);
        var firstRawToken = ReadRefreshCookie(loginController);
        Assert.NotNull(firstRawToken);

        var refreshController = CreateController(db, firstRawToken);
        var refresh = await refreshController.Refresh();

        var ok = Assert.IsType<OkObjectResult>(refresh);
        Assert.False(string.IsNullOrWhiteSpace(GetString(ok.Value, "accessToken")));
        var secondRawToken = ReadRefreshCookie(refreshController);
        Assert.NotNull(secondRawToken);
        Assert.NotEqual(firstRawToken, secondRawToken);
        Assert.Equal(2, await db.RefreshTokens.CountAsync());
        Assert.Equal(1, await db.RefreshTokens.CountAsync(t => t.RevokedAt != null));

        var reuseController = CreateController(db, firstRawToken);
        var reuse = await reuseController.Refresh();

        Assert.IsType<UnauthorizedObjectResult>(reuse);
        Assert.Equal(2, await db.RefreshTokens.CountAsync(t => t.RevokedAt != null));
    }

    [Fact]
    public async Task Logout_RevokesCurrentRefreshToken()
    {
        await using var db = CreateDb();
        await SeedUser(db);
        var loginController = CreateController(db);
        var login = await loginController.Login(new LoginRequest("admin", "password"));
        Assert.IsType<OkObjectResult>(login);
        var rawToken = ReadRefreshCookie(loginController);
        Assert.NotNull(rawToken);

        var logoutController = CreateController(db, rawToken);
        var logout = await logoutController.Logout();

        Assert.IsType<OkObjectResult>(logout);
        var token = await db.RefreshTokens.SingleAsync();
        Assert.NotNull(token.RevokedAt);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static async Task SeedUser(AppDbContext db)
    {
        db.Users.Add(new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password", workFactor: 4),
        });
        await db.SaveChangesAsync();
    }

    private static AuthController CreateController(AppDbContext db, string? refreshToken = null)
    {
        var context = new DefaultHttpContext();
        if (!string.IsNullOrWhiteSpace(refreshToken))
            context.Request.Headers.Cookie = $"refresh_token={Uri.EscapeDataString(refreshToken)}";

        return new AuthController(db, CreateJwtService(), new TestWebHostEnvironment())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = context,
            },
        };
    }

    private static JwtService CreateJwtService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT_SECRET"] = Secret,
            })
            .Build();
        return new JwtService(config);
    }

    private static string CreateExpiredToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: JwtConstants.Issuer,
            audience: JwtConstants.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, "admin"),
            ],
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string? ReadRefreshCookie(ControllerBase controller)
    {
        var setCookies = controller.Response.Headers.SetCookie;
        var cookie = setCookies.FirstOrDefault(value => value?.StartsWith("refresh_token=", StringComparison.Ordinal) == true);
        if (cookie is null) return null;
        var raw = cookie["refresh_token=".Length..].Split(';', 2)[0];
        return Uri.UnescapeDataString(raw);
    }

    private static string? GetString(object? value, string propertyName) =>
        value?.GetType().GetProperty(propertyName)?.GetValue(value)?.ToString();

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "BeServer.Tests";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
