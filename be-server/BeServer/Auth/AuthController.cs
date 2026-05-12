using BeServer.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtService jwt) : ControllerBase
{
    private const string RefreshCookie = "refresh_token";
    private const int RefreshDays = 7;

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == req.Username);
        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { error = "Invalid credentials" });

        var accessToken = jwt.GenerateAccessToken(user.Id, user.Username);
        SetRefreshCookie(jwt.GenerateRefreshToken());
        return Ok(new { accessToken, expiresIn = 900 });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        if (!Request.Cookies.TryGetValue(RefreshCookie, out var token) || string.IsNullOrEmpty(token))
            return Unauthorized(new { error = "No refresh token" });

        // Phase 1: single-user — re-issue for the first (only) user.
        // Phase 1-patch will add a refresh_tokens table with expiry & rotation.
        var user = await db.Users.FirstOrDefaultAsync();
        if (user is null) return Unauthorized();

        var accessToken = jwt.GenerateAccessToken(user.Id, user.Username);
        SetRefreshCookie(jwt.GenerateRefreshToken());
        return Ok(new { accessToken, expiresIn = 900 });
    }

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
            Secure = false,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(RefreshDays),
        });
}

public record LoginRequest(string Username, string Password);
