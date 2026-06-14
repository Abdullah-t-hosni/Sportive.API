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
        ConnectionTimeout = 30,
        DefaultCommandTimeout = 120, // ✅ allow up to 120s command timeout to prevent transient DB timeouts
        ConvertZeroDateTime = true  // ✅ convert '0000-00-00 00:00:00' to DateTime.MinValue
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

// Wire Customer to the DI-managed EncryptionHelper
Sportive.API.Models.Customer.EncryptionHelper = app.Services.GetRequiredService<Sportive.API.Utils.EncryptionHelper>();

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

            try
            {
                Log.Information("Syncing UpdatedAt for existing returned orders...");
                var rowsAffected = await context.Database.ExecuteSqlRawAsync(@"
                    UPDATE Orders
                    SET UpdatedAt = (
                        SELECT MAX(CreatedAt)
                        FROM OrderStatusHistories
                        WHERE OrderId = Orders.Id
                          AND (Status = 'Returned' OR Status = 'PartiallyReturned')
                    )
                    WHERE (Status = 'Returned' OR Status = 'PartiallyReturned') AND UpdatedAt IS NULL;
                ");
                Log.Information("Synced UpdatedAt for {Count} returned orders.", rowsAffected);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to sync UpdatedAt for returned orders on startup.");
            }

            try
            {
                Log.Information("Fixing existing branch accounts tree structure...");
                var accounts = await context.Accounts.ToListAsync();
                
                var currentAssetsAcc = accounts.FirstOrDefault(a => a.Code == "11");
                if (currentAssetsAcc != null)
                {
                    var cashParent = accounts.FirstOrDefault(a => a.NameAr.Contains("النقدية والصناديق"))
                        ?? accounts.FirstOrDefault(a => a.Code == "1101");
                    var bankParent = accounts.FirstOrDefault(a => a.NameAr.Contains("النقدية في البنك") || a.NameAr.Contains("البنك"))
                        ?? accounts.FirstOrDefault(a => a.Code == "1102");
                    var walletParent = accounts.FirstOrDefault(a => a.NameAr.Contains("النقدية في المحافظ") || a.NameAr.Contains("المحافظ"))
                        ?? accounts.FirstOrDefault(a => a.Code == "1105");

                    if (cashParent != null && bankParent != null && walletParent != null)
                    {
                        bool changed = false;

                        // 1. Fix Cashier Cash branch accounts
                        var cashierAccounts = accounts.Where(a => a.BranchId != null && a.NameAr.Contains("نقدية كاشير") && a.ParentId != cashParent.Id).ToList();
                        foreach (var acc in cashierAccounts)
                        {
                            acc.ParentId = cashParent.Id;
                            acc.Level = cashParent.Level + 1;
                            
                            var prefix = cashParent.Code;
                            var existingSiblingCodes = accounts.Where(a => a.ParentId == cashParent.Id).Select(a => a.Code).ToList();
                            int maxSuffix = 0;
                            foreach (var code in existingSiblingCodes)
                            {
                                if (code.StartsWith(prefix) && code.Length > prefix.Length)
                                {
                                    var suffixStr = code.Substring(prefix.Length);
                                    if (int.TryParse(suffixStr, out int parsed) && parsed > maxSuffix)
                                        maxSuffix = parsed;
                                }
                            }
                            acc.Code = $"{prefix}{(maxSuffix + 1):D2}";
                            changed = true;
                            Log.Information("Fixed Cashier account: {Name} to code {Code} under parent {Parent}", acc.NameAr, acc.Code, cashParent.NameAr);
                        }

                        // 2. Fix Wallet branch accounts
                        var walletAccounts = accounts.Where(a => a.BranchId != null && (a.NameAr.Contains("فودافون كاش") || a.NameAr.Contains("إنستاباي") || a.NameAr.Contains("انستاباي")) && a.ParentId != walletParent.Id).ToList();
                        foreach (var acc in walletAccounts)
                        {
                            acc.ParentId = walletParent.Id;
                            acc.Level = walletParent.Level + 1;
                            
                            var prefix = walletParent.Code;
                            var existingSiblingCodes = accounts.Where(a => a.ParentId == walletParent.Id).Select(a => a.Code).ToList();
                            int maxSuffix = 0;
                            foreach (var code in existingSiblingCodes)
                            {
                                if (code.StartsWith(prefix) && code.Length > prefix.Length)
                                {
                                    var suffixStr = code.Substring(prefix.Length);
                                    if (int.TryParse(suffixStr, out int parsed) && parsed > maxSuffix)
                                        maxSuffix = parsed;
                                }
                            }
                            acc.Code = $"{prefix}{(maxSuffix + 1):D2}";
                            changed = true;
                            Log.Information("Fixed Wallet account: {Name} to code {Code} under parent {Parent}", acc.NameAr, acc.Code, walletParent.NameAr);
                        }

                        // 3. Fix Bank branch accounts
                        var bankAccounts = accounts.Where(a => a.BranchId != null && a.NameAr.Contains("شبكات تحت التحصيل") && a.ParentId != bankParent.Id).ToList();
                        foreach (var acc in bankAccounts)
                        {
                            acc.ParentId = bankParent.Id;
                            acc.Level = bankParent.Level + 1;
                            
                            var prefix = bankParent.Code;
                            var existingSiblingCodes = accounts.Where(a => a.ParentId == bankParent.Id).Select(a => a.Code).ToList();
                            int maxSuffix = 0;
                            foreach (var code in existingSiblingCodes)
                            {
                                if (code.StartsWith(prefix) && code.Length > prefix.Length)
                                {
                                    var suffixStr = code.Substring(prefix.Length);
                                    if (int.TryParse(suffixStr, out int parsed) && parsed > maxSuffix)
                                        maxSuffix = parsed;
                                }
                            }
                            acc.Code = $"{prefix}{(maxSuffix + 1):D2}";
                            changed = true;
                            Log.Information("Fixed Bank account: {Name} to code {Code} under parent {Parent}", acc.NameAr, acc.Code, bankParent.NameAr);
                        }

                        if (changed)
                        {
                            if (cashParent.Id != currentAssetsAcc.Id) { cashParent.IsLeaf = false; cashParent.AllowPosting = false; }
                            if (bankParent.Id != currentAssetsAcc.Id) { bankParent.IsLeaf = false; bankParent.AllowPosting = false; }
                            if (walletParent.Id != currentAssetsAcc.Id) { walletParent.IsLeaf = false; walletParent.AllowPosting = false; }

                            await context.SaveChangesAsync();
                            Log.Information("Successfully updated existing branch accounts tree structure.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to fix existing branch accounts tree structure on startup.");
            }
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
app.UseMiddleware<Sportive.API.Middleware.SessionLastSeenMiddleware>();

app.UseHangfireDashboard("/jobs", new DashboardOptions
{
    Authorization = new[] { new HangfireAdminFilter() }
});

app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok("Sportive API is running"));
app.MapHub<NotificationHub>("/notifications-hub");

// ── RECURRING JOBS ────────────────────────────────────
try
{
    Log.Information("Registering Hangfire recurring jobs...");
    var backgroundJobs = app.Services.GetRequiredService<IRecurringJobManager>();
    backgroundJobs.AddOrUpdate<IStatisticsService>(
        "UpdateTodayStats", 
        service => service.UpdateDailyStatsAsync(TimeHelper.GetEgyptTime()), 
        "*/15 * * * *");
        
    backgroundJobs.AddOrUpdate<IOutboxProcessor>(
        "ProcessOutbox", 
        processor => processor.ProcessMessagesAsync(), 
        "*/5 * * * *"); // ✅ was every 1 min (60/hour) — now every 5 min (12/hour)

    backgroundJobs.AddOrUpdate<IOrderService>(
        "SyncOrderAccounting",
        service => service.SyncAllOrderAccountingAsync(30),
        "0 3 * * *"); // Every day at 3:00 AM

    backgroundJobs.AddOrUpdate<IAccountingService>(
        "SyncPurchaseAccounting",
        service => service.SyncAllPurchaseAccountingAsync(30),
        "15 3 * * *"); // Every day at 3:15 AM

    Log.Information("Hangfire recurring jobs registered successfully.");
}
catch (Exception ex)
{
    Log.Error(ex, "Failed to register or update Hangfire recurring jobs. The application will continue starting up.");
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");
