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
using Sportive.API.Models;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

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

var masterConnStr = Environment.GetEnvironmentVariable("MASTER_DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("MasterConnection")
    ?? throw new InvalidOperationException("Master connection string is missing.");

try
{
    var connBuilder = new MySqlConnector.MySqlConnectionStringBuilder(connStr);
    
    // Set baseline options
    connBuilder.Pooling = true;
    connBuilder.AllowUserVariables = true;
    connBuilder.ConvertZeroDateTime = true;
    connBuilder.ConnectionIdleTimeout = 30; // release idle connections after 30s
    connBuilder.Keepalive = 60;
    connBuilder.ConnectionTimeout = 60; // increased from 30 to allow DB to recover under heavy load
    connBuilder.DefaultCommandTimeout = 120; // allow up to 120s command timeout to prevent transient DB timeouts

    // Dynamically resolve Maximum Pool Size to avoid hardcoding limits (helpful for multi-user/multi-tenant production VPS)
    if (!connStr.Contains("Max Pool Size", StringComparison.OrdinalIgnoreCase) && 
        !connStr.Contains("MaximumPoolSize", StringComparison.OrdinalIgnoreCase))
    {
        var envMax = Environment.GetEnvironmentVariable("DATABASE_MAX_POOL_SIZE");
        if (uint.TryParse(envMax, out var maxPoolSize))
        {
            connBuilder.MaximumPoolSize = maxPoolSize;
        }
        else
        {
            var configMax = builder.Configuration.GetValue<uint?>("Database:MaximumPoolSize");
            if (configMax.HasValue)
            {
                connBuilder.MaximumPoolSize = configMax.Value;
            }
            else
            {
                // Increased default for Hangfire background processing and concurrency
                connBuilder.MaximumPoolSize = 250;
            }
        }
    }

    // Dynamically resolve Minimum Pool Size
    if (!connStr.Contains("Min Pool Size", StringComparison.OrdinalIgnoreCase) && 
        !connStr.Contains("MinimumPoolSize", StringComparison.OrdinalIgnoreCase))
    {
        var envMin = Environment.GetEnvironmentVariable("DATABASE_MIN_POOL_SIZE");
        if (uint.TryParse(envMin, out var minPoolSize))
        {
            connBuilder.MinimumPoolSize = minPoolSize;
        }
        else
        {
            connBuilder.MinimumPoolSize = 1; // Only open connections when actually needed to save resources
        }
    }

    connStr = connBuilder.ConnectionString;
}
catch (Exception ex)
{
    Log.Warning(ex, "Failed to parse connection string with MySqlConnectionStringBuilder. Using raw connection string.");
}

try
{
    var masterBuilder = new MySqlConnector.MySqlConnectionStringBuilder(masterConnStr);
    masterBuilder.Pooling = true;
    masterBuilder.AllowUserVariables = true;
    masterBuilder.ConvertZeroDateTime = true;
    masterBuilder.ConnectionIdleTimeout = 30;
    masterBuilder.Keepalive = 60;
    masterBuilder.ConnectionTimeout = 30;
    masterBuilder.DefaultCommandTimeout = 60;
    masterBuilder.MaximumPoolSize = 50; // master database doesn't need as large of a pool
    masterBuilder.MinimumPoolSize = 1;
    masterConnStr = masterBuilder.ConnectionString;
}
catch (Exception ex)
{
    Log.Warning(ex, "Failed to parse master connection string with MySqlConnectionStringBuilder. Using raw connection string.");
}

builder.Services.AddDatabaseAndIdentityServices(connStr, masterConnStr);

// ── JWT & AUTHORIZATION ───────────────────────────────
builder.Services.AddJwtAuthentication(builder.Configuration);

// ── CORS & RATE LIMITING ──────────────────────────────
builder.Services.AddRateLimitingAndCors(builder.Configuration);

// ── CACHE & VALIDATION ────────────────────────────────
builder.Services.AddCacheAndValidationServices(builder.Configuration);

// ── APPLICATION SERVICES ──────────────────────────────
builder.Services.AddApplicationServices();
builder.Services.AddHostedService<TaskGenerationService>();

// ── OPENTELEMETRY ─────────────────────────────────────
var otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "Sportive.API", serviceVersion: "1.0.0")
        .AddAttributes(new Dictionary<string, object>
        {
            { "deployment.environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production" }
        }))
    .WithTracing(tracing => tracing
        .AddSource("Sportive.API")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(opt => opt.Endpoint = new Uri(otelEndpoint)))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(opt => opt.Endpoint = new Uri(otelEndpoint)));

// ── HANGFIRE (Background Jobs) ── minimal in-memory for schema extraction ──
builder.Services.AddHangfire(config => config.UseInMemoryStorage());
builder.Services.AddHangfireServer();

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

builder.Services.AddHttpContextAccessor();

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

// Wire Customer to the DI-managed EncryptionHelper
Sportive.API.Models.Customer.EncryptionHelper = app.Services.GetRequiredService<Sportive.API.Utils.EncryptionHelper>();

// ── AUTOMATIC MIGRATIONS (Production Sync) ────────────
using (var scope = app.Services.CreateScope())
{
        var services = scope.ServiceProvider;
        
        var entryAssemblyName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
        bool isEfTool = true;
        
        if (!isEfTool)
        {
            try
            {
                // 1. Initialize Master Registry Database
                try
                {
                    var masterContext = services.GetRequiredService<MasterDbContext>();
                    Log.Information("Applying migrations for Master database schema...");
                    masterContext.Database.Migrate();

                    if (!masterContext.Tenants.Any(t => t.Slug == "sportive"))
                    {
                        var baselineBuilder = new MySqlConnector.MySqlConnectionStringBuilder(connStr);
                        baselineBuilder.ConvertZeroDateTime = true;
                        
                        masterContext.Tenants.Add(new Tenant
                        {
                            TenantGuid = Guid.NewGuid(),
                            Slug = "sportive",
                            Name = "Sportive",
                            Subdomain = "sportive",
                            DatabaseName = baselineBuilder.Database,
                            DatabaseUser = baselineBuilder.UserID,
                            DatabasePassword = baselineBuilder.Password,
                            Status = TenantStatus.Active,
                            CreatedAt = TimeHelper.GetEgyptTime()
                        });
                        masterContext.SaveChanges();
                        Log.Information("Seeded initial 'sportive' tenant in Master registry.");
                    }

                    var sportive = masterContext.Tenants.FirstOrDefault(t => t.Slug == "sportive");
                    if (sportive != null && !masterContext.TenantSubscriptions.Any(ts => ts.TenantGuid == sportive.TenantGuid))
                    {
                        masterContext.TenantSubscriptions.Add(new TenantSubscription
                        {
                            TenantGuid = sportive.TenantGuid,
                            PlanId = 4, // Enterprise
                            StartsAt = TimeHelper.GetEgyptTime(),
                            ExpiresAt = TimeHelper.GetEgyptTime().AddYears(10),
                            IsActive = true,
                            AutoRenew = true
                        });
                        masterContext.SaveChanges();
                        Log.Information("Seeded Enterprise subscription for 'sportive' tenant.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to initialize or seed Master database. Ensure 'u282618987_raakiza_master' database exists on server with proper user privileges.");
                }

                // 2. We no longer run automatic migrations for all active tenants sequentially at startup.
                // Migrations should be handled via a dedicated administrative endpoint on-demand to avoid scaling bottlenecks.
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred while migrating the database.");
            }
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
app.UseMiddleware<Sportive.API.Middleware.TenantValidationMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<Sportive.API.Middleware.SessionLastSeenMiddleware>();

// app.UseHangfireDashboard("/jobs", new DashboardOptions
// {
//     Authorization = new[] { new HangfireAdminFilter() }
// });

app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok("Sportive API is running"));
app.MapSitemapEndpoints();
app.MapHub<NotificationHub>("/notifications-hub");

// ── RECURRING JOBS ────────────────────────────────────
// TEMPORARY SUSPENSION: Disabled to unblock production deployment
// try
// {
//     Log.Information("Registering Hangfire recurring jobs...");
//     var backgroundJobs = app.Services.GetRequiredService<IRecurringJobManager>();
//     backgroundJobs.AddOrUpdate<IStatisticsService>(
//         "UpdateTodayStats", 
//         service => service.UpdateDailyStatsAsync(TimeHelper.GetEgyptTime()), 
//         "*/15 * * * *");
//         
//     backgroundJobs.AddOrUpdate<IOutboxProcessor>(
//         "ProcessOutbox", 
//         processor => processor.ProcessMessagesAsync(), 
//         "*/5 * * * *"); // ✅ was every 1 min (60/hour) — now every 5 min (12/hour)
// 
//     backgroundJobs.AddOrUpdate<IOrderService>(
//         "SyncOrderAccounting",
//         service => service.SyncAllOrderAccountingAsync(30),
//         "0 3 * * *"); // Every day at 3:00 AM
// 
//     backgroundJobs.AddOrUpdate<IAccountingService>(
//         "SyncPurchaseAccounting",
//         service => service.SyncAllPurchaseAccountingAsync(30),
//         "15 3 * * *"); // Every day at 3:15 AM
// 
//     backgroundJobs.AddOrUpdate<IAuditService>(
//         "CleanupOldAuditLogs",
//         service => service.CleanupOldLogsAsync(3),
//         "30 3 * * *"); // Every day at 3:30 AM
// 
//     Log.Information("Hangfire recurring jobs registered successfully.");
// }
// catch (Exception ex)
// {
//     Log.Error(ex, "Failed to register or update Hangfire recurring jobs. The application will continue starting up.");
// }

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");
