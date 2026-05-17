using System.Text;
using BeServer.Auth;
using BeServer.Data;
using BeServer.Data.Entities;
using BeServer.Middleware;
using BeServer.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

ValidateProductionSecrets(builder.Configuration, builder.Environment);
ValidateInternalSecrets(builder.Configuration);

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

builder.Services.AddAuthorization(options =>
    options.AddPolicy("DevAdminOnly", policy => policy.RequireClaim("dev_admin", "true")));
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
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUserAccessor>();
builder.Services.AddScoped<OwnershipService>();
builder.Services.AddScoped<ChatMessageService>();
builder.Services.AddScoped<RetrievalVersionService>();
builder.Services.AddSingleton<ModelRegistry>();
builder.Services.AddSingleton<OperationalMetrics>();
builder.Services.AddControllers();
builder.Services.AddHttpClient<RagClient>(client =>
    client.BaseAddress = new Uri(
        builder.Configuration["RAG_SERVER_URL"] ?? "http://rag-server:8003"));
builder.Services.AddHostedService<IngestionJobWorker>();
builder.Services.AddHttpClient("ai-server", client =>
    client.BaseAddress = new Uri(
        builder.Configuration["AI_SERVER_URL"] ?? "http://ai-server:8002"));

builder.Services.AddCors(options =>
    options.AddPolicy("frontend", policy =>
        policy.WithOrigins("http://localhost:5987", "http://frontend:80")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.Use(async (context, next) =>
{
    var sw = Stopwatch.StartNew();
    var metrics = context.RequestServices.GetRequiredService<OperationalMetrics>();
    var isError = false;
    try
    {
        await next();
        isError = context.Response.StatusCode >= 500;
    }
    catch
    {
        isError = true;
        throw;
    }
    finally
    {
        metrics.ObserveRequest(sw.ElapsedMilliseconds, isError);
    }
});

// ── Migrate & Seed ───────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("DatabaseStartup");
    await MigrateAndSeedWithRetry(db, builder.Configuration, logger);
    await PruneRequestLogs(db, builder.Configuration);
}

static async Task PruneRequestLogs(AppDbContext db, IConfiguration config)
{
    var retentionDays = config.GetValue<int?>("REQUEST_LOG_RETENTION_DAYS") ?? 30;
    var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
    await db.RequestLogs.Where(log => log.CreatedAt < cutoff).ExecuteDeleteAsync();
}

app.UseCors("frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "be-server" }));
app.MapGet("/ready", async (AppDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok(new { status = "ready", service = "be-server" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
app.MapGet("/metrics", async (AppDbContext db, OperationalMetrics operationalMetrics) =>
{
    var since = DateTime.UtcNow.AddMinutes(-5);
    var requestCount = await db.RequestLogs.CountAsync(log => log.CreatedAt >= since);
    var errorCount = await db.RequestLogs.CountAsync(log => log.CreatedAt >= since && log.Error != null);
    var averageRequestDuration = await db.RequestLogs
        .Where(log => log.CreatedAt >= since && log.DurationMs != null)
        .AverageAsync(log => (double?)log.DurationMs) ?? 0;
    var queueDepth = await db.IngestionJobs.CountAsync(job =>
        job.JobType == IngestionJobTypes.Ingest &&
        (job.Status == IngestionJobStatuses.Queued || job.Status == IngestionJobStatuses.Retrying));
    var ingestionDurations = await db.IngestionJobs
        .Where(job =>
            job.JobType == IngestionJobTypes.Ingest &&
            job.StartedAt != null &&
            job.CompletedAt != null &&
            job.CompletedAt >= since)
        .Select(job => new { job.StartedAt, job.CompletedAt })
        .ToListAsync();
    var averageIngestionDuration = ingestionDurations.Count == 0
        ? 0
        : ingestionDurations.Average(job => (job.CompletedAt!.Value - job.StartedAt!.Value).TotalMilliseconds);
    return Results.Ok(new
    {
        window_minutes = 5,
        http = operationalMetrics.Snapshot(),
        outbound_requests = new { count = requestCount, errors = errorCount, average_duration_ms = averageRequestDuration },
        ingestion = new { queue_depth = queueDepth, average_duration_ms = averageIngestionDuration },
    });
});
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
            await SeedRetrievalPresets(db);
            await BackfillNotebookRetrievalVersions(db);
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
            IsDevAdmin = true,
        });
        await db.SaveChangesAsync();
    }
}

static async Task SeedRetrievalPresets(AppDbContext db)
{
    if (await db.RetrievalPresets.AnyAsync()) return;
    db.RetrievalPresets.AddRange(
        new RetrievalPreset { Key = "general", Name = "General", Description = "Balanced default", ChunkSize = 800, ChunkOverlap = 100, EmbeddingModel = "text-embedding-3-small", EmbeddingDimensions = 1536, DefaultSearchMode = "hybrid", DefaultTopK = 5, DefaultHybridAlpha = 0.5 },
        new RetrievalPreset { Key = "longform", Name = "Longform", Description = "Larger chunks for prose", ChunkSize = 1200, ChunkOverlap = 160, EmbeddingModel = "text-embedding-3-small", EmbeddingDimensions = 1536, DefaultSearchMode = "hybrid", DefaultTopK = 5, DefaultHybridAlpha = 0.55 },
        new RetrievalPreset { Key = "keyword-heavy", Name = "Keyword Heavy", Description = "Leans toward sparse retrieval", ChunkSize = 700, ChunkOverlap = 80, EmbeddingModel = "text-embedding-3-small", EmbeddingDimensions = 1536, DefaultSearchMode = "hybrid", DefaultTopK = 8, DefaultHybridAlpha = 0.3 },
        new RetrievalPreset { Key = "transcript", Name = "Transcript", Description = "Shorter chunks for conversational text", ChunkSize = 500, ChunkOverlap = 80, EmbeddingModel = "text-embedding-3-small", EmbeddingDimensions = 1536, DefaultSearchMode = "hybrid", DefaultTopK = 6, DefaultHybridAlpha = 0.5 });
    await db.SaveChangesAsync();
}

static async Task BackfillNotebookRetrievalVersions(AppDbContext db)
{
    var preset = await db.RetrievalPresets.SingleAsync(p => p.Key == "general");
    var notebooks = await db.Notebooks.Where(n => n.ActiveRetrievalVersionId == null).ToListAsync();
    foreach (var notebook in notebooks)
    {
        var version = RetrievalVersionService.FromPreset(notebook.Id, notebook.UserId, preset, "Backfilled initial notebook version");
        db.NotebookRetrievalVersions.Add(version);
        notebook.ActiveRetrievalVersionId = version.Id;
        foreach (var source in await db.Sources.Where(s => s.NotebookId == notebook.Id && s.ActiveRetrievalVersionId == null).ToListAsync())
            source.ActiveRetrievalVersionId = version.Id;
    }
    if (notebooks.Count > 0)
        await db.SaveChangesAsync();
}

static void ValidateProductionSecrets(IConfiguration config, IWebHostEnvironment env)
{
    if (env.IsDevelopment()) return;

    RejectDefault("JWT_SECRET", config["JWT_SECRET"], "changeme_32_char_minimum_secret_here");
    RejectDefault("INTERNAL_SECRET", config["INTERNAL_SECRET"], "changeme_internal_secret_here");
    RejectDefault("ADMIN_PASSWORD", config["ADMIN_PASSWORD"], "changeme", "changeme_strong_password");
    RejectDefault("DB_PASSWORD", config["DB_PASSWORD"], "ragpass", "password", "changeme");
}

static void ValidateInternalSecrets(IConfiguration config)
{
    ValidateMinLength("RAG_INTERNAL_SECRET", FirstConfigured(config["RAG_INTERNAL_SECRET"], config["INTERNAL_SECRET"]));
    ValidateMinLength("AI_INTERNAL_SECRET", FirstConfigured(config["AI_INTERNAL_SECRET"], config["INTERNAL_SECRET"]));
}

static void ValidateMinLength(string name, string? value)
{
    if (string.IsNullOrWhiteSpace(value) || value.Length < JwtConstants.MinSecretLength)
        throw new InvalidOperationException($"{name} must be at least {JwtConstants.MinSecretLength} characters.");
}

static string? FirstConfigured(params string?[] values) =>
    values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

static void RejectDefault(string name, string? value, params string[] defaults)
{
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"{name} is required outside development.");

    if (defaults.Any(defaultValue => string.Equals(value, defaultValue, StringComparison.Ordinal)))
        throw new InvalidOperationException($"{name} must not use a development default outside development.");
}

public partial class Program;
