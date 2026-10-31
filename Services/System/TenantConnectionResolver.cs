using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Sportive.API.Interfaces;

namespace Sportive.API.Services;

public class TenantConnectionResolver : ITenantConnectionResolver
{
    private readonly ITenantContext _tenantContext;
    private static string? _cachedBaseConnectionString;
    private static readonly ConcurrentDictionary<string, string> _tenantConnCache = new();
    private readonly string _baseConnectionString;

    public TenantConnectionResolver(ITenantContext tenantContext, IConfiguration configuration)
    {
        _tenantContext = tenantContext;

        if (_cachedBaseConnectionString == null)
        {
            var rawConnStr = Environment.GetEnvironmentVariable("DATABASE_URL")
                ?? configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Baseline DefaultConnection connection string is missing.");

            var builder = new MySqlConnectionStringBuilder(rawConnStr)
            {
                Pooling = true,
                MinimumPoolSize = 0,
                MaximumPoolSize = 15,
                ConnectionIdleTimeout = 180,
                ConnectionLifeTime = 600,
                Keepalive = 30,
                ConnectionReset = true,
                ConnectionTimeout = 30,
                DefaultCommandTimeout = 90,
                AllowUserVariables = true,
                ConvertZeroDateTime = true
            };

            _cachedBaseConnectionString = builder.ConnectionString;
        }

        _baseConnectionString = _cachedBaseConnectionString;
    }

    public string GetConnectionString()
    {
        var tenant = _tenantContext.CurrentTenant;
        if (tenant == null || string.IsNullOrWhiteSpace(tenant.DatabaseName))
        {
            // If no tenant is set (e.g. background services, recurring Hangfire jobs, CLI tools, or startup tasks),
            // safely fallback to the baseline DefaultConnection string in all environments.
            if (tenant == null)
            {
                return _baseConnectionString;
            }

            // Fallback for EF Core design-time tools which don't have a tenant context
            var entryAssemblyName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
            if (entryAssemblyName != null && (entryAssemblyName.Equals("ef", StringComparison.OrdinalIgnoreCase) || entryAssemblyName.Equals("dotnet-ef", StringComparison.OrdinalIgnoreCase)))
            {
                return _baseConnectionString;
            }

            throw new InvalidOperationException($"Cannot resolve tenant connection string: DatabaseName is empty for tenant '{tenant.Slug}'.");
        }

        var cacheKey = $"{tenant.DatabaseName}|{tenant.DatabaseUser}|{tenant.DatabasePassword}";
        return _tenantConnCache.GetOrAdd(cacheKey, _ =>
        {
            var builder = new MySqlConnectionStringBuilder(_baseConnectionString)
            {
                Database = tenant.DatabaseName,
                UserID = tenant.DatabaseUser,
                Password = tenant.DatabasePassword,
                Pooling = true,
                MinimumPoolSize = 0,
                MaximumPoolSize = 15,
                ConnectionIdleTimeout = 180,
                ConnectionLifeTime = 600,
                Keepalive = 30,
                ConnectionReset = true,
                ConnectionTimeout = 30,
                DefaultCommandTimeout = 90,
                AllowUserVariables = true,
                ConvertZeroDateTime = true
            };

            return builder.ConnectionString;
        });
    }
}
