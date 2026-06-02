using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Sportive.API.Data;
using Sportive.API.Middleware;
using Sportive.API.Utils;
using Sportive.API.Hubs;
using Hangfire;
using Sportive.API.Extensions;
using Sportive.API.Interfaces;
using Sportive.API.Services;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" ? LogEventLevel.Debug : LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/sportive-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
});

// ── DATABASE & IDENTITY ───────────────────────────────
var connStr = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string is missing.");

try
{
    var connBuilder = new MySqlConnector.MySqlConnectionStringBuilder(connStr)
    {
        Pooling = true,
        MinimumPoolSize = 1,   // ✅ was 3 — only open connections when actually needed
        MaximumPoolSize = 10,  // ✅ hard cap: EF Core won't exceed 10 simultaneous DB connections
        ConnectionIdleTimeout = 30,  // ✅ release idle connections after 30s
        Keepalive = 60,
        AllowUserVariables = true,
        ConnectionTimeout = 30
    };
    connStr = connBuilder.ConnectionString;
}
catch (Exception ex)
{
    Log.Warning(ex, "Failed to parse connection string with MySqlConnectionStringBuilder. Using raw connection string.");
}

builder.Services.AddDatabaseAndIdentityServices(connStr);

// ── JWT & AUTHORIZATION ───────────────────────────────
builder.Services.AddJwtAuthentication(builder.Configuration);

// ── CORS & RATE LIMITING ──────────────────────────────
builder.Services.AddRateLimitingAndCors(builder.Configuration);

// ── CACHE & VALIDATION ────────────────────────────────
builder.Services.AddCacheAndValidationServices(builder.Configuration);

// ── APPLICATION SERVICES ──────────────────────────────
builder.Services.AddApplicationServices();

// ── HANGFIRE (Background Jobs) ────────────────────────
builder.Services.AddHangfireServices(connStr);

// ── RESPONSE COMPRESSION ──────────────────────────────
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(
    options => options.Level = System.IO.Compression.CompressionLevel.Fastest);

// ── HEALTH CHECKS ─────────────────────────────────────
builder.Services.AddHealthChecks();

// ── SWAGGER ───────────────────────────────────────────
builder.Services.AddSwaggerAndApiExplorer();

// ── CONTROLLERS ───────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(x =>
    {
        x.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        x.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        x.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        x.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
        x.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });

// ── BUILD ─────────────────────────────────────────────
var app = builder.Build();

// Wire TimeHelper to the DI-managed TimeService
TimeHelper.Initialize(app.Services.GetRequiredService<ITimeService>());

// ── AUTOMATIC MIGRATIONS (Production Sync) ────────────
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        if (context.Database.IsRelational())
        {
            Log.Information("Applying pending migrations...");
            context.Database.Migrate();
            Log.Information("Database is up to date.");
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "An error occurred while migrating the database.");
    }
}

app.UseResponseCompression();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Sportive API v1");
        c.RoutePrefix = string.Empty;
        c.DocumentTitle = "Sportive API";
    });
}

app.UseCors("AllowReactApp");
app.UseSerilogRequestLogging();

// ── SECURITY HEADERS ──────────────────────────────────
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"]    = "nosniff";
    ctx.Response.Headers["X-Frame-Options"]           = "DENY";
    ctx.Response.Headers["X-XSS-Protection"]          = "0"; // deprecated, disable
    ctx.Response.Headers["Referrer-Policy"]           = "strict-origin-when-cross-origin";
    ctx.Response.Headers["Permissions-Policy"]        = "camera=(), microphone=(), geolocation=()";
    ctx.Response.Headers["Content-Security-Policy"]   =
        "default-src 'self'; script-src 'self' https:; style-src 'self' https:; img-src 'self' data: https:; connect-src 'self' wss: https:; font-src 'self' data: https:;";
    if (ctx.Request.IsHttps)
        ctx.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    await next();
});

app.UseMiddleware<ExceptionMiddleware>();

app.UseStaticFiles();

var uploadsPath = Path.Combine(builder.Environment.ContentRootPath, "uploads");
if (!Directory.Exists(uploadsPath)) Directory.CreateDirectory(uploadsPath);
Log.Information("Photo uploads path: {Path}", uploadsPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
    ServeUnknownFileTypes = false
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard("/jobs", new DashboardOptions
{
    Authorization = new[] { new HangfireAdminFilter() }
});

app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok("Sportive API is running"));
app.MapHub<NotificationHub>("/notifications-hub");

// ── RECURRING JOBS ────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var backgroundJobs = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    backgroundJobs.AddOrUpdate("UpdateTodayStats", 
        () => scope.ServiceProvider.GetRequiredService<IStatisticsService>().UpdateDailyStatsAsync(TimeHelper.GetEgyptTime()), 
        "*/15 * * * *");
        
    backgroundJobs.AddOrUpdate("ProcessOutbox", 
        () => scope.ServiceProvider.GetRequiredService<IOutboxProcessor>().ProcessMessagesAsync(), 
        "*/5 * * * *"); // ✅ was every 1 min (60/hour) — now every 5 min (12/hour)
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");
