using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BeServer.Auth;

public class JwtService(IConfiguration config)
{
    private static readonly JwtSecurityTokenHandler Handler = new();

    private readonly string _secret = ValidateSecret(
        config["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET not configured"));

    private static string ValidateSecret(string secret)
    {
        if (secret.Length < JwtConstants.MinSecretLength)
            throw new InvalidOperationException(
                $"JWT_SECRET must be at least {JwtConstants.MinSecretLength} characters.");
        return secret;
    }

    public string GenerateAccessToken(string userId, string username, bool isDevAdmin = false)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim("dev_admin", isDevAdmin ? "true" : "false"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: JwtConstants.Issuer,
            audience: JwtConstants.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);

        return Handler.WriteToken(token);
    }

    public string GenerateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        try
        {
            return Handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = JwtConstants.Issuer,
                ValidateAudience = true,
                ValidAudience = JwtConstants.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            }, out _);
        }
        catch
        {
            return null;
        }
    }
}
