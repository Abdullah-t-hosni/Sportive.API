using System.IO;
using System.Text;
using System.Text.Json;
using FluentValidation;
using FluentValidation.AspNetCore;
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
using Sportive.API.Validators;
using Sportive.API.Hubs;


Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/sportive-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// ── DATABASE ──────────────────────────────────────────
var connStr = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string is missing.");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseMySql(connStr, new MySqlServerVersion(new Version(8, 0, 0)),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure());
});

// ── IDENTITY ──────────────────────────────────────────
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// ── JWT ───────────────────────────────────────────────
var jwtSecret = builder.Configuration["JWT:Secret"]
    ?? throw new InvalidOperationException("JWT:Secret is not configured");

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
        ClockSkew = TimeSpan.Zero
    };

    opt.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/notifications-hub"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// ── CORS ──────────────────────────────────────────────
builder.Services.AddCors(options =>
    options.AddPolicy("AllowReactApp", policy =>
    {
        var origins = new List<string>
        {
            "http://localhost:3000",
            "https://localhost:3000",
            "http://localhost:5173",
            "https://localhost:5173",
            "http://localhost:5174",
            "https://localhost:5174"
        };

        var extra = builder.Configuration["AllowedOrigins"];
        if (!string.IsNullOrWhiteSpace(extra))
            origins.AddRange(extra.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        policy.WithOrigins(origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    }));

// ── CACHE ─────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

var redisConn = builder.Configuration.GetConnectionString("Redis");

if (!string.IsNullOrEmpty(redisConn))
{
    builder.Services.AddStackExchangeRedisCache(opt =>
        opt.Configuration = redisConn);

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
                    .Select(x => string.IsNullOrEmpty(x.ErrorMessage) ? x.Exception?.Message : x.ErrorMessage)
                    .Where(m => !string.IsNullOrEmpty(m))
                    .ToArray());

        return new BadRequestObjectResult(new
        {
            message = "Validation failed",
            errors
        });
    };
});

// ── SERVICES ──────────────────────────────────────────
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ICouponService, CouponService>();
builder.Services.AddScoped<IImageService, CloudinaryImageService>();
builder.Services.AddScoped<IPaymobService, PaymobService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPdfService, PdfService>();

// Wishlist, Reviews, Analytics — handled directly in controllers

builder.Services.AddHttpClient("Paymob");
builder.Services.AddSignalR();


// ── SWAGGER ───────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Sportive API", Version = "v1" });

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
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
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
        x.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// ── BUILD ─────────────────────────────────────────────
var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Sportive API v1");
    c.RoutePrefix = string.Empty;
    c.DocumentTitle = "Sportive API";
});

app.UseCors("AllowReactApp");
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionMiddleware>();

app.UseHttpsRedirection();
app.UseStaticFiles();

// ✅ أضف الكود ده عشان يخدم الصور من مجلد uploads في جذر المشروع
var uploadsPath = Path.Combine(builder.Environment.ContentRootPath, "uploads");
if (!Directory.Exists(uploadsPath)) Directory.CreateDirectory(uploadsPath);
Log.Information("Photo uploads path: {Path}", uploadsPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHub<NotificationHub>("/notifications-hub");

await SeedAsync(app);

app.Run();

// ── SEED ──────────────────────────────────────────────
static async Task SeedAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

    try
    {
        await db.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Database connection failed");
        throw;
    }

    foreach (var role in new[] { "Admin", "Customer", "Staff", "Cashier" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    const string adminEmail = "admin@sportive.com";

    if (await userManager.FindByEmailAsync(adminEmail) == null)
    {
        var admin = new AppUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FirstName = "Sport",
            LastName = "Zone",
            IsActive = true
        };

        await userManager.CreateAsync(admin, "Admin@123456");
        await userManager.AddToRoleAsync(admin, "Admin");
    }
}