using K6LoadTestEngine.Models;
using K6LoadTestEngine.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<K6ScriptGenerator>();
builder.Services.AddSingleton<K6ProcessRunner>();
builder.Services.AddSingleton<K6ResultParser>();

// ── CORS (allow any origin for local dev) ─────────────────────────────────────
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod()));

// Increase default timeout limits for long-running tests
builder.WebHost.ConfigureKestrel(opt =>
{
    opt.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(60);
    opt.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(60);
});

var app = builder.Build();
app.UseCors();

// ── Health ────────────────────────────────────────────────────────────────────
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));

// ── Run Test ──────────────────────────────────────────────────────────────────
app.MapPost("/api/run-test", async (
    HttpRequest request,
    K6ScriptGenerator generator,
    K6ProcessRunner runner,
    K6ResultParser parser,
    CancellationToken ct) =>
{
    TestConfig config;
    try
    {
        config = await request.ReadFromJsonAsync<TestConfig>(ct)
                 ?? throw new InvalidOperationException("Empty request body.");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"Invalid request: {ex.Message}" });
    }

    // Normalise (migrates legacy Url → Endpoints, clamps pct values, etc.)
    config.Normalise();

    // Basic validation
    if (config.Endpoints.Count == 0)
        return Results.BadRequest(new { error = "At least one endpoint is required." });
    if (config.Endpoints.Any(e => string.IsNullOrWhiteSpace(e.Url)))
        return Results.BadRequest(new { error = "All endpoints must have a non-empty URL." });

    // ── Generate k6 script ─────────────────────────────────────────────────────
    string scriptPath;
    try
    {
        scriptPath = generator.GenerateScript(config);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Script generation failed: {ex.Message}");
    }

    string resultJsonPath = generator.GetResultJsonPath();

    // ── Run k6 ────────────────────────────────────────────────────────────────
    var (exitCode, logs) = await runner.RunAsync(scriptPath, resultJsonPath, ct);

    if (exitCode == -1)
    {
        // k6 not found
        return Results.Problem(logs, statusCode: 500);
    }

    // ── Parse results ─────────────────────────────────────────────────────────
    var result = parser.Parse(resultJsonPath, config, logs);

    // Keep a debug copy of the last generated script before cleanup
    try
    {
        string debugCopyPath = Path.Combine(Path.GetTempPath(), "k6-load-engine", "last_generated_debug.js");
        File.Copy(scriptPath, debugCopyPath, overwrite: true);
    }
    catch { /* ignore */ }

    // Clean up temp script
    try { File.Delete(scriptPath); } catch { /* ignore */ }

    return Results.Ok(result);
})
.WithName("RunTest")
.DisableAntiforgery();

// ── Serve frontend (optional - only if wwwroot exists) ────────────────────────
app.UseStaticFiles();

app.Run();
