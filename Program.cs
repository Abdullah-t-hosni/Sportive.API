using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Serilog;
using Serilog.Events;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Middleware;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;
using Sportive.API.Validators;
using Sportive.API.Hubs;


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

// ── DATABASE ──────────────────────────────────────────
var connStr = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string is missing.");

if (!connStr.Contains("Allow User Variables=true", StringComparison.OrdinalIgnoreCase))
    connStr = connStr.TrimEnd(';') + ";Allow User Variables=true;";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connStr, new MySqlServerVersion(new Version(8, 0, 0)),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure()));

// ── IDENTITY ──────────────────────────────────────────
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 8;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredUniqueChars = 1;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// ── JWT ───────────────────────────────────────────────
var jwtSecret = builder.Configuration["JWT:Secret"]
    ?? throw new InvalidOperationException("JWT:Secret is not configured");

if (jwtSecret.Length < 32 || jwtSecret.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("JWT:Secret must be at least 32 characters and not the default placeholder value.");

builder.Services.AddAuthentication(opt =>
{
    opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(opt =>
{
    opt.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["JWT:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["JWT:Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(5)
    };

    opt.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/notifications-hub"))
                context.Token = accessToken;
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();
builder.Services.AddScoped<Sportive.API.Services.StaffPermissionService>();

// ── CORS ──────────────────────────────────────────────
builder.Services.AddCors(options =>
    options.AddPolicy("AllowReactApp", policy =>
    {
        var origins = new List<string>
        {
            "http://localhost:3000", "https://localhost:3000",
            "http://localhost:5173", "https://localhost:5173",
            "http://localhost:5174", "https://localhost:5174",
            "https://www.sportive-sportwear.com",
            "https://sportive-sportwear.com",
            "https://admin.sportive-sportwear.com"
        };

        var extra = builder.Configuration["AllowedOrigins"];
        if (!string.IsNullOrWhiteSpace(extra))
            origins.AddRange(extra.Split(new[] { ',', ';' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        policy.WithOrigins(origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    }));

// ── RATE LIMITING ─────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    // Per-route auth policy — strict (10 req/min per user or IP)
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.User.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 3,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0
            }));

    // Global policy — 150 req/min keyed by authenticated user ID, falling back to IP
    // This prevents a single shared IP (NAT/proxy) from triggering limits for all users behind it.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.User.Identity?.IsAuthenticated == true
                ? $"user:{httpContext.User.Identity.Name}"
                : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 150, Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 10,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0
            }));

    options.RejectionStatusCode = 429;
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            """{"message":"Too many requests. Please wait a minute and try again."}""");
    };
});

// ── CACHE ─────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

var redisConn = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConn))
{
    builder.Services.AddStackExchangeRedisCache(opt => opt.Configuration = redisConn);
    builder.Services.AddScoped<ICacheService, RedisCacheService>();
}
else
{
    builder.Services.AddScoped<ICacheService, MemoryCacheService>();
}

// ── VALIDATION ────────────────────────────────────────
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterValidator>();

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                e => e.Key,
                e => e.Value!.Errors
                    .Select(x => x.ErrorMessage)
                    .Where(m => !string.IsNullOrEmpty(m))
                    .ToArray());
        return new BadRequestObjectResult(new { success = false, message = "Validation failed", errors });
    };
});

// ── SERVICES ──────────────────────────────────────────
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBrandService, BrandService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<ICustomerCategoryService, CustomerCategoryService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ICouponService, CouponService>();
builder.Services.AddScoped<IImageService, CloudinaryImageService>();
builder.Services.AddScoped<IPaymobService, PaymobService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPdfService, PdfService>();
builder.Services.AddScoped<AccountingCoreService>();
builder.Services.AddScoped<SalesAccountingService>();
builder.Services.AddScoped<PurchaseAccountingService>();
builder.Services.AddScoped<PaymentAccountingService>();
builder.Services.AddScoped<JournalAccountingService>();
builder.Services.AddScoped<IAccountingService, AccountingService>();
builder.Services.AddScoped<IWaMeService, WaMeService>();
builder.Services.AddHttpClient<IWhatsAppApiService, WhatsAppApiService>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddHostedService<BackupHostedService>();
builder.Services.AddHostedService<Sportive.API.Services.BackgroundServices.StartupSyncService>();
builder.Services.AddScoped<IWishlistService, WishlistService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<IAiAssistantService, AiAssistantService>();
builder.Services.AddSingleton<SequenceService>();
builder.Services.AddSingleton<TimeService>();
builder.Services.AddSingleton<ITimeService>(sp => sp.GetRequiredService<TimeService>());
builder.Services.AddHttpClient("Paymob");
builder.Services.AddSignalR();

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
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

// ── SWAGGER ───────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Sportive API", Version = "v1" });

    // ✅ FIX 1: Handle any remaining duplicate routes gracefully
    c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());

    // ✅ FIX 2: Use short type names to avoid FullName issues with nested/generic types
    c.CustomSchemaIds(type => type.Name);

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter: Bearer {token}"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {{
        new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
        },
        Array.Empty<string>()
    }});
});

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

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<NotificationHub>("/notifications-hub");

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");
