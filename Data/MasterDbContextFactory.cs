using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace Sportive.API.Data;

/// <summary>
/// Used exclusively by EF Core design-time tools (migrations, scaffolding) for MasterDbContext.
/// </summary>
public class MasterDbContextFactory : IDesignTimeDbContextFactory<MasterDbContext>
{
    public MasterDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var masterConnStr = config.GetConnectionString("MasterConnection")
            ?? throw new InvalidOperationException("No connection string named 'MasterConnection' found.");

        if (!masterConnStr.Contains("Allow User Variables=true", StringComparison.OrdinalIgnoreCase))
            masterConnStr = masterConnStr.TrimEnd(';') + ";Allow User Variables=true;";

        var optionsBuilder = new DbContextOptionsBuilder<MasterDbContext>();
        optionsBuilder.UseMySql(masterConnStr, new MySqlServerVersion(new Version(8, 0, 0)));

        return new MasterDbContext(optionsBuilder.Options);
    }
}
