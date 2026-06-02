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
    public static IServiceCollection AddDatabaseAndIdentityServices(this IServiceCollection services, string connStr)
    {
        if (!connStr.Contains("Allow User Variables=true", StringComparison.OrdinalIgnoreCase))
            connStr = connStr.TrimEnd(';') + ";Allow User Variables=true;";

        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(connStr, new MySqlServerVersion(new Version(8, 0, 0)),
                mySqlOptions => mySqlOptions.EnableRetryOnFailure()));

        services.AddIdentity<AppUser, IdentityRole>(options =>
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

        return services;
    }

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSecret = configuration["JWT:Secret"]
            ?? throw new InvalidOperationException("JWT:Secret is not configured");

        if (jwtSecret.Length < 32 || jwtSecret.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("JWT:Secret must be at least 32 characters and not the default placeholder value.");

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
                    "http://localhost:5173", "https://localhost:5173",
                    "http://localhost:5174", "https://localhost:5174",
                    "https://www.sportive-sportwear.com",
                    "https://sportive-sportwear.com",
                    "https://admin.sportive-sportwear.com",
                    "https://sportive-frontend-production.up.railway.app"
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
            options.AddPolicy("auth", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: httpContext.User.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10, Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 3,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0
                    }));

            options.AddPolicy("api", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: httpContext.User.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 60, Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0
                    }));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: httpContext.User.Identity?.IsAuthenticated == true
                        ? $"user:{httpContext.User.Identity.Name}"
                        : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 300, Window = TimeSpan.FromMinutes(1),
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
        services.AddScoped<IBrandService, BrandService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICustomerCategoryService, CustomerCategoryService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ICouponService, CouponService>();
        services.AddScoped<IImageService, CloudinaryImageService>();
        services.AddScoped<IPaymobService, PaymobService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IPdfService, PdfService>();
        services.AddScoped<AccountingCoreService>();
        services.AddScoped<SalesAccountingService>();
        services.AddScoped<PurchaseAccountingService>();
        services.AddScoped<IAuditService, AuditService>();
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
        services.AddHostedService<StartupSyncService>();
        services.AddScoped<IWishlistService, WishlistService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<IAiAssistantService, AiAssistantService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantProvider, TenantProvider>();
        services.AddScoped<IStatisticsService, StatisticsService>();
        services.AddScoped<IDashboardEventService, DashboardEventService>();
        services.AddScoped<IOutboxProcessor, OutboxProcessor>();
        services.AddScoped<ZKDeviceService>();
        services.AddSingleton<ITranslator, Translator>();
        services.AddSingleton<SequenceService>();
        services.AddSingleton<TimeService>();
        services.AddSingleton<ITimeService>(sp => sp.GetRequiredService<TimeService>());
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
                AllowUserVariables    = true
            };
            hangfireConnStr = hfBuilder.ConnectionString;
        }
        catch
        {
            hangfireConnStr = connStr; // fallback to original if parsing fails
        }

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
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
