using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Hangfire;
using Hangfire.MySql;
using System.Transactions;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Services.BackgroundServices;
using Sportive.API.Utils;
using Sportive.API.Validators;
using Sportive.API.Services.HR;
using Sportive.API.Hubs;

namespace Sportive.API.Extensions;

public static class DependencyInjection
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DbContextOptions<AppDbContext>> _tenantDbOptionsCache = new();
    private static readonly MySqlServerVersion _serverVersion = new(new Version(8, 0, 0));

    public static IServiceCollection AddDatabaseAndIdentityServices(this IServiceCollection services, string connStr, string masterConnStr)
    {
        if (!connStr.Contains("Allow User Variables=true", StringComparison.OrdinalIgnoreCase))
            connStr = connStr.TrimEnd(';') + ";Allow User Variables=true;";

        if (!masterConnStr.Contains("Allow User Variables=true", StringComparison.OrdinalIgnoreCase))
            masterConnStr = masterConnStr.TrimEnd(';') + ";Allow User Variables=true;";

        services.AddDbContextPool<MasterDbContext>(options =>
            options.UseMySql(masterConnStr, _serverVersion,
                mySqlOptions => mySqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(3), errorNumbersToAdd: null)));

        services.AddScoped<AppDbContext>(serviceProvider =>
        {
            var resolver = serviceProvider.GetRequiredService<ITenantConnectionResolver>();
            var tenantConnStr = resolver.GetConnectionString();

            var dbOptions = _tenantDbOptionsCache.GetOrAdd(tenantConnStr, cs =>
            {
                var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
                optionsBuilder.UseMySql(cs, _serverVersion,
                    mySqlOptions => mySqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(3), errorNumbersToAdd: null));
                return optionsBuilder.Options;
            });

            return new AppDbContext(
                dbOptions,
                serviceProvider.GetService<IHttpContextAccessor>(),
                serviceProvider.GetService<IServiceScopeFactory>(),
                serviceProvider.GetService<ITenantContext>()
            );
        });

        services.AddScoped<IDbContextFactory<AppDbContext>, ScopedAppDbContextFactory>();

        services.AddIdentity<AppUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequiredLength = 8;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredUniqueChars = 1;

            // Lockout settings
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        return services;
    }

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSecret = configuration["JWT:Secret"]
            ?? throw new InvalidOperationException("JWT:Secret is not configured");

        if (jwtSecret.Length < 32 || jwtSecret.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("JWT:Secret must be at least 32 characters and not the default placeholder value.");

        var refreshSecret = configuration["Security:RefreshTokenSecret"];
        if (string.IsNullOrEmpty(refreshSecret) || refreshSecret == "${REFRESH_TOKEN_SECRET}")
        {
            refreshSecret = Environment.GetEnvironmentVariable("REFRESH_TOKEN_SECRET");
        }
        if (string.IsNullOrEmpty(refreshSecret))
        {
            throw new InvalidOperationException("Security:RefreshTokenSecret is not configured.");
        }

        var backupSecret = configuration["Security:BackupSecret"];
        if (string.IsNullOrEmpty(backupSecret) || backupSecret == "${BACKUP_SECRET}")
        {
            backupSecret = Environment.GetEnvironmentVariable("BACKUP_SECRET");
        }
        if (string.IsNullOrEmpty(backupSecret))
        {
            throw new InvalidOperationException("Security:BackupSecret is not configured.");
        }

        var searchSecret = configuration["Security:SearchSecret"];
        if (string.IsNullOrEmpty(searchSecret) || searchSecret == "${SEARCH_SECRET}")
        {
            searchSecret = Environment.GetEnvironmentVariable("SEARCH_SECRET");
        }
        if (string.IsNullOrEmpty(searchSecret))
        {
            throw new InvalidOperationException("Security:SearchSecret is not configured.");
        }

        var auditSecret = configuration["Security:AuditSecret"];
        if (string.IsNullOrEmpty(auditSecret) || auditSecret == "${AUDIT_SECRET}")
        {
            auditSecret = Environment.GetEnvironmentVariable("AUDIT_SECRET");
        }
        if (string.IsNullOrEmpty(auditSecret))
        {
            throw new InvalidOperationException("Security:AuditSecret is not configured.");
        }

        var encryptionKey = configuration["Security:EncryptionKeyV1"];
        if (string.IsNullOrEmpty(encryptionKey) || encryptionKey == "${ENCRYPTION_KEY_V1}")
        {
            encryptionKey = Environment.GetEnvironmentVariable("ENCRYPTION_KEY_V1");
        }
        if (string.IsNullOrEmpty(encryptionKey))
        {
            throw new InvalidOperationException("Security:EncryptionKeyV1 is not configured.");
        }

        services.AddAuthentication(opt =>
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
                ValidIssuer = configuration["JWT:Issuer"],
                ValidateAudience = true,
                ValidAudience = configuration["JWT:Audience"],
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

        services.AddAuthorization(options =>
        {
            options.AddPolicy("Orders.Create", p => p.RequireClaim("Permission", "Orders.Create"));
            options.AddPolicy("Orders.View",   p => p.RequireClaim("Permission", "Orders.View"));
            options.AddPolicy("AdminOnly",     p => p.RequireRole("Admin", "SuperAdmin"));
            options.AddPolicy("SuperAdminOnly", p => p.RequireRole("SuperAdmin"));
        });

        services.AddScoped<StaffPermissionService>();

        return services;
    }

    public static IServiceCollection AddRateLimitingAndCors(this IServiceCollection services, IConfiguration configuration)
    {
        // CORS
        services.AddCors(options =>
            options.AddPolicy("AllowReactApp", policy =>
            {
                var origins = new List<string>
                {
                    "http://localhost:3000", "https://localhost:3000",
                    "http://localhost:3001", "https://localhost:3001",
                    "http://localhost:3002", "https://localhost:3002",
                    "http://localhost:5173", "https://localhost:5173",
                    "http://localhost:5174", "https://localhost:5174",
                    "http://localhost:5175", "https://localhost:5175",
                    "http://localhost:5176", "https://localhost:5176",
                    "https://www.sportive-sportwear.com",
                    "https://sportive-sportwear.com",
                    "https://admin.sportive-sportwear.com",
                    "https://sportive-frontend-production.up.railway.app",
                    "https://admin.raakiza.com",
                    "https://www.admin.raakiza.com",
                    "https://app.raakiza.com",
                    "https://www.app.raakiza.com",
                    "https://raakiza.com",
                    "https://www.raakiza.com"
                };

                var extra = configuration["AllowedOrigins"];
                if (!string.IsNullOrWhiteSpace(extra))
                    origins.AddRange(extra.Split(new[] { ',', ';' },
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

                policy.WithOrigins(origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            }));

        // Rate Limiting
        services.AddRateLimiter(options =>
        {
            static string ResolveClientKey(HttpContext httpContext)
            {
                var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                             ?? httpContext.User.Identity?.Name;
                if (!string.IsNullOrEmpty(userId))
                    return $"user:{userId}";

                var ip = httpContext.Connection.RemoteIpAddress?.ToString();
                if (!string.IsNullOrEmpty(ip))
                    return $"ip:{ip}";

                return "ip:unknown";
            }

            options.AddPolicy("auth", httpContext =>
            {
                if (HttpMethods.IsOptions(httpContext.Request.Method))
                    return RateLimitPartition.GetNoLimiter("options");

                var ipKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? ResolveClientKey(httpContext);
                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: $"auth:{ipKey}",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 30, Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 3,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0
                    });
            });

            options.AddPolicy("api", httpContext =>
            {
                if (HttpMethods.IsOptions(httpContext.Request.Method))
                    return RateLimitPartition.GetNoLimiter("options");

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: $"api:{ResolveClientKey(httpContext)}",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 240, Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0
                    });
            });

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                if (HttpMethods.IsOptions(httpContext.Request.Method) ||
                    httpContext.Request.Path.StartsWithSegments("/health") ||
                    httpContext.Request.Path.StartsWithSegments("/uploads") ||
                    httpContext.Request.Path.StartsWithSegments("/notifications-hub"))
                {
                    return RateLimitPartition.GetNoLimiter("exempt");
                }

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: ResolveClientKey(httpContext),
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 600, Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0
                    });
            });

            options.RejectionStatusCode = 429;
            options.OnRejected = async (context, _) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    """{"message":"Too many requests. Please wait a minute and try again."}""");

                var loggerFactory = context.HttpContext.RequestServices.GetService<Microsoft.Extensions.Logging.ILoggerFactory>();
                loggerFactory?.CreateLogger("RateLimiter").LogWarning(
                    "Rate limit exceeded for {Key} on {Method} {Path}",
                    ResolveClientKey(context.HttpContext),
                    context.HttpContext.Request.Method,
                    context.HttpContext.Request.Path);
            };
        });

        return services;
    }

    public static IServiceCollection AddCacheAndValidationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddHttpContextAccessor();

        var redisConn = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(redisConn))
        {
            services.AddStackExchangeRedisCache(opt => opt.Configuration = redisConn);
            services.AddScoped<ICacheService, RedisCacheService>();
        }
        else
        {
            services.AddScoped<ICacheService, MemoryCacheService>();
        }

        // Validation
        services.AddFluentValidationAutoValidation();
        services.AddValidatorsFromAssemblyContaining<RegisterValidator>();

        services.Configure<ApiBehaviorOptions>(options =>
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

        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISecurityEventsEngine, SecurityEventsEngine>();
        services.AddScoped<IBrandService, BrandService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ITaxIntegrationService, TaxIntegrationService>();
        services.AddScoped<Sportive.API.Services.ETA.IEtaIntegrationService, Sportive.API.Services.ETA.EtaIntegrationService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<Sportive.API.Services.Loyalty.ILoyaltyService, Sportive.API.Services.Loyalty.LoyaltyService>();
        services.AddScoped<ICustomerCategoryService, CustomerCategoryService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ICouponService, CouponService>();
        services.AddScoped<IImageService, CloudinaryImageService>();
        services.AddScoped<IPaymobService, PaymobService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IInternalChatBotService, InternalChatBotService>();
        services.AddScoped<IPdfService, PdfService>();
        services.AddScoped<AccountingCoreService>();
        services.AddScoped<SalesAccountingService>();
        services.AddScoped<PurchaseAccountingService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAuditIntegrityService, AuditIntegrityService>();
        services.AddScoped<PaymentAccountingService>();
        services.AddScoped<JournalAccountingService>();
        services.AddScoped<IAccountingService, AccountingService>();
        services.AddScoped<IWaMeService, WaMeService>();
        services.AddHttpClient<IWhatsAppApiService, WhatsAppApiService>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IDataMaintenanceService, DataMaintenanceService>();
        services.AddScoped<IBackfillService, BackfillService>();
        services.AddHostedService<BackupHostedService>();
        // TEMPORARY SUSPENSION: Disabled to unblock production deployment
        // services.AddHostedService<StartupSyncService>();
        services.AddScoped<IWishlistService, WishlistService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<IAiAssistantService, AiAssistantService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantProvider, TenantProvider>();
        services.AddScoped<ITenantRegistry, TenantRegistry>();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITenantResolver, TenantResolver>();
        services.AddScoped<ITenantConnectionResolver, TenantConnectionResolver>();
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<IPlanService, PlanService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<IGoogleAnalyticsService, GoogleAnalyticsService>();
        services.AddScoped<IStatisticsService, StatisticsService>();
        services.AddScoped<IDashboardEventService, DashboardEventService>();
        services.AddScoped<IOutboxProcessor, OutboxProcessor>();
        services.AddScoped<ZKDeviceService>();
        services.AddSingleton<ITranslator, Translator>();
        services.AddSingleton<SequenceService>();
        services.AddSingleton<TimeService>();
        services.AddSingleton<ITimeService>(sp => sp.GetRequiredService<TimeService>());
        services.AddSingleton<Sportive.API.Utils.EncryptionHelper>();
        services.AddHttpClient();
        services.AddHttpClient("Paymob");
        services.AddSignalR();

        return services;
    }

    public static IServiceCollection AddHangfireServices(this IServiceCollection services, string connStr)
    {
        // ✅ Build a separate limited connection string for Hangfire
        // Hangfire has its own pool — we must cap it to avoid exceeding Hostinger's 500/hour limit
        string hangfireConnStr;
        try
        {
            var hfBuilder = new MySqlConnector.MySqlConnectionStringBuilder(connStr)
            {
                Pooling               = true,
                MinimumPoolSize       = 0,   // don't pre-open connections at startup
                MaximumPoolSize       = 3,   // Hangfire only needs a few workers
                ConnectionIdleTimeout = 60,  // release idle connections after 60s
                ConnectionTimeout     = 30,
                DefaultCommandTimeout = 120, // ✅ allow up to 120s command timeout to handle slow Hostinger responses
                AllowUserVariables    = true
            };
            hangfireConnStr = hfBuilder.ConnectionString;
        }
        catch
        {
            hangfireConnStr = connStr; // fallback to original if parsing fails
        }

        services.AddHangfire((provider, config) => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseFilter(new Sportive.API.Filters.TenantJobFilter(provider))
            .UseStorage(new MySqlStorage(hangfireConnStr, new MySqlStorageOptions
            {
                TransactionIsolationLevel = (IsolationLevel)System.Data.IsolationLevel.ReadCommitted,
                QueuePollInterval         = TimeSpan.FromSeconds(60), // ✅ was 30s — poll less often
                JobExpirationCheckInterval = TimeSpan.FromHours(6),   // ✅ was 1h — check less often
                CountersAggregateInterval = TimeSpan.FromMinutes(15), // ✅ was 5min
                PrepareSchemaIfNecessary  = true,
                DashboardJobListLimit     = 1000,                     // ✅ was 50000 — reduce memory
                TransactionTimeout        = TimeSpan.FromMinutes(1),
                TablesPrefix              = "Hangfire"
            })));

        services.AddHangfireServer(opt =>
        {
            opt.WorkerCount  = 2;  // ✅ fixed at 2 — don't scale with CPU (saves connections)
            opt.Queues       = new[] { "critical", "default", "low" };
            opt.ServerName   = $"sportive-{Environment.MachineName}";
        });

        return services;
    }

    public static IServiceCollection AddSwaggerAndApiExplorer(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Sportive API", Version = "v1" });
            c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
            c.CustomSchemaIds(type => (type.FullName ?? type.Name).Replace("+", "_").Replace("Sportive.API.Controllers.", "").Replace("Sportive.API.DTOs.", "").Replace("Sportive.API.Models.", "").Replace(".", "_"));

            // Handle IFormFile parameters for file upload endpoints
            c.MapType<IFormFile>(() => new OpenApiSchema { Type = "string", Format = "binary" });

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
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }
}
