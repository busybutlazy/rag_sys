var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
        policy.WithOrigins("http://localhost:5987")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();
app.UseCors("frontend");

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "be-server" }));

app.Run();
