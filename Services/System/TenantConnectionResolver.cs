using System;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Sportive.API.Interfaces;

namespace Sportive.API.Services;

public class TenantConnectionResolver : ITenantConnectionResolver
{
    private readonly ITenantContext _tenantContext;
    private readonly string _baseConnectionString;

    public TenantConnectionResolver(ITenantContext tenantContext, IConfiguration configuration)
    {
        _tenantContext = tenantContext;
        
        _baseConnectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Baseline DefaultConnection connection string is missing.");
    }

    public string GetConnectionString()
    {
        var tenant = _tenantContext.CurrentTenant;
        if (tenant == null || string.IsNullOrWhiteSpace(tenant.DatabaseName))
        {
            // Fallback for EF Core design-time tools which don't have a tenant context
            var entryAssemblyName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
            if (entryAssemblyName != null && (entryAssemblyName.Equals("ef", StringComparison.OrdinalIgnoreCase) || entryAssemblyName.Equals("dotnet-ef", StringComparison.OrdinalIgnoreCase)))
            {
                return _baseConnectionString;
            }

            throw new InvalidOperationException("Cannot resolve tenant connection string: No tenant context set.");
        }

        var builder = new MySqlConnectionStringBuilder(_baseConnectionString)
        {
            Database = tenant.DatabaseName,
            UserID = tenant.DatabaseUser,
            Password = tenant.DatabasePassword
        };

        builder.AllowUserVariables = true;

        return builder.ConnectionString;
    }
}
