using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Sportive.API.Data;

/// <summary>
/// Used exclusively by EF Core design-time tools (migrations, scaffolding).
/// This bypasses Program.cs entirely, so JWT/external-service config is not required.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connStr = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "No connection string found. Set DefaultConnection in appsettings.Development.json.");

        if (!connStr.Contains("Allow User Variables=true", StringComparison.OrdinalIgnoreCase))
            connStr = connStr.TrimEnd(';') + ";Allow User Variables=true;";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseMySql(connStr, new MySqlServerVersion(new Version(8, 0, 0)));

        return new AppDbContext(optionsBuilder.Options);
    }
}
