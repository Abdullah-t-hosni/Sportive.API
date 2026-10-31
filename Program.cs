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
using Sportive.API.Filters;
using Sportive.API.Models;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

var isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(isDev ? LogEventLevel.Debug : LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", isDev ? LogEventLevel.Information : LogEventLevel.Error)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Infrastructure", LogEventLevel.Warning)
    .MinimumLevel.Override("Sportive.API.Controllers.Webhooks", LogEventLevel.Warning)
    .MinimumLevel.Override("Hangfire", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(restrictedToMinimumLevel: isDev ? LogEventLevel.Debug : LogEventLevel.Warning, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
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
    connBuilder.ConnectionIdleTimeout = 180;
    connBuilder.ConnectionLifeTime = 600;
    connBuilder.Keepalive = 30;
    connBuilder.ConnectionReset = true;
    connBuilder.ConnectionTimeout = 30;
    connBuilder.DefaultCommandTimeout = 90;
    connBuilder.MaximumPoolSize = 15;
    connBuilder.MinimumPoolSize = 0;

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
    masterBuilder.ConnectionIdleTimeout = 180;
    masterBuilder.ConnectionLifeTime = 600;
    masterBuilder.Keepalive = 30;
    masterBuilder.ConnectionReset = true;
    masterBuilder.ConnectionTimeout = 30;
    masterBuilder.DefaultCommandTimeout = 60;
    masterBuilder.MaximumPoolSize = 5;
    masterBuilder.MinimumPoolSize = 0;
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
builder.Services.AddHttpClient<IFacebookCapiService, FacebookCapiService>();
builder.Services.AddApplicationServices();
builder.Services.AddHostedService<TaskGenerationService>();

// ── OPENTELEMETRY ─────────────────────────────────────
var otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"];
var otelEnabled = builder.Configuration.GetValue<bool>("OpenTelemetry:Enabled", false)
    && !string.IsNullOrWhiteSpace(otelEndpoint)
    && !otelEndpoint.Contains(":5173");

if (otelEnabled)
{
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
            .AddHttpClientInstrumentation(opt =>
            {
                opt.FilterHttpRequestMessage = req => !req.RequestUri?.ToString().StartsWith(otelEndpoint!) ?? true;
            })
            .AddOtlpExporter(opt => opt.Endpoint = new Uri(otelEndpoint!)))
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter(opt => opt.Endpoint = new Uri(otelEndpoint!)));
}

// ── HANGFIRE (Background Jobs) ── minimal in-memory for schema extraction ──
builder.Services.AddHangfire((provider, config) => config
    .UseInMemoryStorage()
    .UseFilter(new TenantJobFilter(provider)));
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
        x.JsonSerializerOptions.Converters.Add(
            new Sportive.API.Utils.NullableDateTimeConverter());
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
                // Auto-patch new columns for StoreSettings safely
                try
                {
                    using var patchScope = services.CreateScope();
                    var db = patchScope.ServiceProvider.GetRequiredService<AppDbContext>();
                    try { db.Database.ExecuteSqlRaw("ALTER TABLE StoreSettings ADD COLUMN FacebookAdAccountId longtext NULL;"); } catch {}
                    try { db.Database.ExecuteSqlRaw("ALTER TABLE StoreSettings ADD COLUMN RealAdCampaignsJson longtext NULL;"); } catch {}
                }
                catch {}
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred while migrating the database.");
            }
        }
    }

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
var forwardedHeadersOptions = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                       Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseResponseCompression(); // ✅ single call — was duplicated previously
app.UseCors("AllowReactApp");
app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, elapsed, ex) =>
    {
        if (ex != null || httpContext.Response.StatusCode >= 500)
            return LogEventLevel.Error;
        if (httpContext.Response.StatusCode >= 400)
            return LogEventLevel.Warning;

        // Skip OPTIONS requests (CORS preflight)
        if (HttpMethods.IsOptions(httpContext.Request.Method))
            return LogEventLevel.Verbose;

        var path = httpContext.Request.Path.Value ?? "";
        // Suppress noisy health checks, root ping, signalr, webhooks, and polling endpoints from flooding console
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/uploads", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/notifications-hub", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/webhooks", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/webhooks", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/internal-chat/channels", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/whatsapp/status", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        {
            return LogEventLevel.Verbose;
        }

        return isDev ? LogEventLevel.Information : LogEventLevel.Verbose;
    };
});

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

var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".webm"] = "audio/webm";
contentTypeProvider.Mappings[".ogg"] = "audio/ogg";
contentTypeProvider.Mappings[".mp3"] = "audio/mpeg";
contentTypeProvider.Mappings[".wav"] = "audio/wav";
contentTypeProvider.Mappings[".m4a"] = "audio/mp4";

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
    ContentTypeProvider = contentTypeProvider,
    ServeUnknownFileTypes = true
});

app.UseMiddleware<Sportive.API.Middleware.TenantValidationMiddleware>();
app.UseAuthentication();
app.UseRateLimiter();
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
// ✅ Re-enabled safe recurring jobs (low DB pressure).
// Heavy nightly jobs (SyncOrderAccounting, SyncPurchaseAccounting) remain
// commented out to stay within Hostinger's MySQL 500 connections/hour limit.
try
{
    Log.Information("Registering Hangfire recurring jobs...");
    var backgroundJobs = app.Services.GetRequiredService<IRecurringJobManager>();

    // ✅ Outbox processor — picks up any stats/notification messages missed by the
    //    in-process DashboardEventService trigger. Runs every 5 min (12/hour).
    backgroundJobs.AddOrUpdate<IOutboxProcessor>(
        "ProcessOutbox",
        processor => processor.ProcessMessagesAsync(),
        "*/5 * * * *");

    // ✅ Daily stats refresh — keeps dashboard KPIs current.
    //    Runs every 15 min (4/hour) which is safe on Hostinger.
    backgroundJobs.AddOrUpdate<IStatisticsService>(
        "UpdateTodayStats",
        service => service.UpdateDailyStatsAsync(TimeHelper.GetEgyptTime()),
        "*/15 * * * *");

    // ✅ Daily Database Backup — runs every night at 2:00 AM Egypt Time (23:00 UTC)
    backgroundJobs.AddOrUpdate<IBackupService>(
        "DailyDatabaseBackup",
        service => service.RunBackupAsync("Scheduled", System.Threading.CancellationToken.None),
        "0 23 * * *");

    // ✅ Daily Partners & Online Store Report to "الشركاء" channel — runs every day at 8:00 AM Egypt Time (5:00 AM UTC)
    backgroundJobs.AddOrUpdate<IInternalChatBotService>(
        "DailyPartnersAndStoreReport",
        bot => bot.PostDailyPartnersAndStoreReportAsync(null),
        "0 5 * * *");

    // ── Nightly jobs (commented out — heavy DB load, re-enable on dedicated server) ──
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
}
catch (Exception ex)
{
    Log.Warning(ex, "Could not register Hangfire recurring jobs — app will still start.");
} 

if (args.Contains("--migrate-isactive"))
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            Log.Information("Adding IsActive column to ProductVariants table if missing...");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `ProductVariants` ADD COLUMN `IsActive` tinyint(1) NOT NULL DEFAULT 1;");
            Log.Information("SUCCESS: Added IsActive column to ProductVariants table.");
        }
        catch (Exception ex)
        {
            Log.Information("Migration Note: {Message}", ex.Message);
        }

        try
        {
            Log.Information("Adding MaxOnlineStock column to ProductVariants table if missing...");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `ProductVariants` ADD COLUMN `MaxOnlineStock` int NULL DEFAULT NULL;");
            Log.Information("SUCCESS: Added MaxOnlineStock column to ProductVariants table.");
        }
        catch (Exception ex)
        {
            Log.Information("Migration Note: {Message}", ex.Message);
        }
    }
    return;
}

if (args.Contains("--recalculate-stock"))
{
    using (var scope = app.Services.CreateScope())
    {
        var maintenance = scope.ServiceProvider.GetRequiredService<IDataMaintenanceService>();
        Log.Information("Executing RecalculateStockAsync via CLI...");
        var (success, message) = await maintenance.RecalculateStockAsync();
        Log.Information("RecalculateStock result: Success={Success}, Message={Message}", success, message);
    }
    return;
}

if (args.Contains("--test-report"))
{
    using (var scope = app.Services.CreateScope())
    {
        var chatBot = scope.ServiceProvider.GetRequiredService<IInternalChatBotService>();
        var dateArg = args.SkipWhile(a => a != "--test-report").Skip(1).FirstOrDefault();
        DateTime targetDate = DateTime.TryParse(dateArg, out var d) ? d : TimeHelper.GetEgyptTime().Date.AddDays(-1);
        Log.Information("Executing PostDailyPartnersAndStoreReportAsync via CLI for date {Date}...", targetDate.ToString("yyyy-MM-dd"));
        await chatBot.PostDailyPartnersAndStoreReportAsync(targetDate);
        Log.Information("Done executing PostDailyPartnersAndStoreReportAsync via CLI.");
    }
    return;
}


// 🔄 Auto-Fix Returned Order Statuses & Legacy Settlements on startup (non-blocking)
_ = Task.Run(async () =>
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE `PayrollRuns` ADD COLUMN `PeriodType` int NOT NULL DEFAULT 1;"); } catch {}
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE `PayrollRuns` ADD COLUMN `FromDate` datetime NULL;"); } catch {}
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE `PayrollRuns` ADD COLUMN `ToDate` datetime NULL;"); } catch {}

        var chatBot = scope.ServiceProvider.GetRequiredService<IInternalChatBotService>();
        await chatBot.EnsureSystemChannelsExistAsync();

        var egyptNow = TimeHelper.GetEgyptTime();
        if (egyptNow.Hour >= 8)
        {
            var targetD = egyptNow.Date.AddDays(-1);
            var reportRef = $"PARTNERS-{targetD:yyyy-MM-dd}";
            var partnersChan = await db.InternalChatChannels
                .FirstOrDefaultAsync(c => c.DirectKey == InternalChatBotService.ChannelKeyPartners);
            if (partnersChan != null)
            {
                var hasReport = await db.InternalChatMessages.AnyAsync(m => m.ChannelId == partnersChan.Id && m.LinkedEntityRef == reportRef);
                if (!hasReport)
                {
                    await chatBot.PostDailyPartnersAndStoreReportAsync(targetD);
                }
            }
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error running startup maintenance tasks");
    }
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");
