using BeServer.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

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

        var accessToken = jwt.GenerateAccessToken(user.Id, user.Username);
        SetRefreshCookie(jwt.GenerateRefreshToken());
        return Ok(new { accessToken, expiresIn = 900 });
    }

    // SEC-01: /refresh is not implemented until Phase 1-patch adds a refresh_tokens table.
    // Returning 501 prevents any non-empty cookie from being exchanged for a real token.
    [HttpPost("refresh")]
    public IActionResult Refresh() =>
        StatusCode(StatusCodes.Status501NotImplemented,
            new { error = "Token refresh not yet implemented. Please log in again." });

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete(RefreshCookie);
        return Ok(new { message = "Logged out" });
    }

    private void SetRefreshCookie(string token) =>
        Response.Cookies.Append(RefreshCookie, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !env.IsDevelopment(), // SEC-02: only send over HTTPS in production
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(RefreshDays),
        });
}

public record LoginRequest(string Username, string Password);
