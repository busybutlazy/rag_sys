using System.Security.Cryptography;
using BeServer.Data;
using BeServer.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtService jwt, IWebHostEnvironment env) : ControllerBase
{
    private const string RefreshCookie = "refresh_token";
    private const int RefreshDays = 7;

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == req.Username);

        // Always verify against something to prevent username enumeration via timing (SEC-11)
        var hashToCheck = user?.PasswordHash ?? BCrypt.Net.BCrypt.HashPassword("dummy", workFactor: 12);
        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, hashToCheck))
            return Unauthorized(new { error = "Invalid credentials" });

        var accessToken = jwt.GenerateAccessToken(user.Id, user.Username, user.IsDevAdmin);
        var refreshToken = CreateRefreshToken(user.Id, Guid.NewGuid().ToString(), ClientIp());
        db.RefreshTokens.Add(refreshToken.Entity);
        await db.SaveChangesAsync();

        SetRefreshCookie(refreshToken.RawToken);
        return Ok(new { accessToken, expiresIn = 900 });
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Refresh()
    {
        if (!Request.Cookies.TryGetValue(RefreshCookie, out var rawToken) || string.IsNullOrWhiteSpace(rawToken))
            return Unauthorized(new { error = "Missing refresh token" });

        var tokenHash = HashRefreshToken(rawToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (stored is null)
        {
            DeleteRefreshCookie();
            return Unauthorized(new { error = "Invalid refresh token" });
        }

        if (stored.RevokedAt is not null)
        {
            await RevokeActiveFamily(stored.FamilyId, ClientIp());
            await db.SaveChangesAsync();
            DeleteRefreshCookie();
            return Unauthorized(new { error = "Refresh token reuse detected" });
        }

        if (stored.ExpiresAt <= DateTime.UtcNow)
        {
            stored.RevokedAt = DateTime.UtcNow;
            stored.RevokedByIp = ClientIp();
            await db.SaveChangesAsync();
            DeleteRefreshCookie();
            return Unauthorized(new { error = "Refresh token expired" });
        }

        var replacement = CreateRefreshToken(stored.UserId, stored.FamilyId, ClientIp());
        stored.RevokedAt = DateTime.UtcNow;
        stored.RevokedByIp = ClientIp();
        stored.ReplacedByTokenId = replacement.Entity.Id;
        db.RefreshTokens.Add(replacement.Entity);
        await db.SaveChangesAsync();

        SetRefreshCookie(replacement.RawToken);
        var accessToken = jwt.GenerateAccessToken(stored.UserId, stored.User.Username, stored.User.IsDevAdmin);
        return Ok(new { accessToken, expiresIn = 900 });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (Request.Cookies.TryGetValue(RefreshCookie, out var rawToken) && !string.IsNullOrWhiteSpace(rawToken))
        {
            var tokenHash = HashRefreshToken(rawToken);
            var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash);
            if (stored is not null && stored.RevokedAt is null)
            {
                stored.RevokedAt = DateTime.UtcNow;
                stored.RevokedByIp = ClientIp();
                await db.SaveChangesAsync();
            }
        }

        DeleteRefreshCookie();
        return Ok(new { message = "Logged out" });
    }

    private (RefreshToken Entity, string RawToken) CreateRefreshToken(string userId, string familyId, string? clientIp)
    {
        var rawToken = jwt.GenerateRefreshToken();
        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = HashRefreshToken(rawToken),
            FamilyId = familyId,
            ExpiresAt = DateTime.UtcNow.AddDays(RefreshDays),
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = clientIp,
        };
        return (entity, rawToken);
    }

    private async Task RevokeActiveFamily(string familyId, string? clientIp)
    {
        var active = await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var token in active)
        {
            token.RevokedAt = now;
            token.RevokedByIp = clientIp;
        }
    }

    private void SetRefreshCookie(string token) =>
        Response.Cookies.Append(RefreshCookie, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !env.IsDevelopment(), // SEC-02: only send over HTTPS in production
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(RefreshDays),
        });

    private void DeleteRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
        });

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    internal static string HashRefreshToken(string token)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }
}

public record LoginRequest(string Username, string Password);
