using System;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportive.API.Interfaces;

namespace Sportive.API.Data;

public class ScopedAppDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly IServiceProvider _serviceProvider;

    public ScopedAppDbContextFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public AppDbContext CreateDbContext()
    {
        var resolver = _serviceProvider.GetRequiredService<ITenantConnectionResolver>();
        var tenantConnStr = resolver.GetConnectionString();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseMySql(tenantConnStr, new MySqlServerVersion(new Version(8, 0, 0)),
            mySqlOptions => mySqlOptions.EnableRetryOnFailure());

        return new AppDbContext(
            optionsBuilder.Options,
            _serviceProvider.GetService<IHttpContextAccessor>(),
            _serviceProvider.GetService<IServiceScopeFactory>(),
            _serviceProvider.GetService<ITenantContext>()
        );
    }
}
