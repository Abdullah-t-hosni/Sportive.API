using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportive.API.Data;
using Sportive.API.Models;
using System;
using System.Linq;

namespace Sportive.API.Scratch;

public class Program
{
    public static async Task Main(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("DATABASE_URL") ?? "server=localhost;database=sportive;user=root;password=root";
        
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(connStr, ServerVersion.AutoDetect(connStr)));

        var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try {
            var inactiveAccounts = await db.Accounts
                .Where(a => !a.IsActive)
                .OrderBy(a => a.Code)
                .Select(a => new { a.Code, a.NameAr, a.Description })
                .ToListAsync();

            if (!inactiveAccounts.Any()) {
                Console.WriteLine("--- No inactive accounts found ---");
                return;
            }

            Console.WriteLine($"--- Found {inactiveAccounts.Count} Inactive Accounts ---");
            foreach (var acc in inactiveAccounts) {
                Console.WriteLine($"[ {acc.Code} ] {acc.NameAr} | {acc.Description ?? "No description"}");
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
