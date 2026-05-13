using System.Text;
using BeServer.Auth;
using BeServer.Data;
using BeServer.Data.Entities;
using BeServer.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

// ── Database ─────────────────────────────────────
var connStr =
    $"Server={builder.Configuration["DB_HOST"] ?? "localhost"};" +
    $"Port={builder.Configuration["DB_PORT"] ?? "3306"};" +
    $"Database={builder.Configuration["DB_NAME"] ?? "rag_sys"};" +
    $"User={builder.Configuration["DB_USER"] ?? "raguser"};" +
    $"Password={builder.Configuration["DB_PASSWORD"] ?? "ragpass"};";

// Pinned version avoids a live DB call during DI registration (SEC-06)
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseMySql(
        connStr,
        new MySqlServerVersion(new Version(8, 0, 0)),
        mysql => mysql.EnableRetryOnFailure(
            maxRetryCount: 10,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null)));

// ── JWT ──────────────────────────────────────────
var jwtSecret = builder.Configuration["JWT_SECRET"]
    ?? throw new InvalidOperationException("JWT_SECRET is required");

if (jwtSecret.Length < JwtConstants.MinSecretLength)
    throw new InvalidOperationException(
        $"JWT_SECRET must be at least {JwtConstants.MinSecretLength} characters.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = JwtConstants.Issuer,
            ValidateAudience = true,
            ValidAudience = JwtConstants.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("write", limiter =>
    {
        limiter.PermitLimit = 60;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 5;
    });
});
builder.Services.AddScoped<JwtService>();
builder.Services.AddControllers();
builder.Services.AddHttpClient<RagClient>(client =>
    client.BaseAddress = new Uri(
        builder.Configuration["RAG_SERVER_URL"] ?? "http://rag-server:8003"));

builder.Services.AddCors(options =>
    options.AddPolicy("frontend", policy =>
        policy.WithOrigins("http://localhost:5987", "http://frontend:80")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

var app = builder.Build();

// ── Migrate & Seed ───────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("DatabaseStartup");
    await MigrateAndSeedWithRetry(db, builder.Configuration, logger);
}

app.UseCors("frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "be-server" }));
app.MapControllers();

app.Run();

static async Task MigrateAndSeedWithRetry(AppDbContext db, IConfiguration config, ILogger logger)
{
    const int maxAttempts = 12;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            await SeedAdminUser(db, config);
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogWarning(
                ex,
                "Database migration failed on attempt {Attempt}/{MaxAttempts}; retrying.",
                attempt,
                maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
}

// ── Seed helper ──────────────────────────────────
static async Task SeedAdminUser(AppDbContext db, IConfiguration config)
{
    var username = config["ADMIN_USERNAME"] ?? "admin";
    var password = config["ADMIN_PASSWORD"] ?? "changeme";

    if (!await db.Users.AnyAsync(u => u.Username == username))
    {
        db.Users.Add(new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
        });
        await db.SaveChangesAsync();
    }
}
