using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Scratch
{
    public class SyncSource
    {
        public static async Task Run(IServiceProvider services)
        {
            var db = services.GetRequiredService<AppDbContext>();
            Console.WriteLine("Syncing Source fields in Ledger...");

            // 1. Sync JournalEntries
            var entries = await db.JournalEntries
                .Where(e => e.CostCenter == null && e.OrderId != null)
                .ToListAsync();
            
            foreach (var e in entries) {
                var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == e.OrderId);
                if (order != null) e.CostCenter = order.Source;
            }

            // 2. Sync JournalLines
            var lines = await db.JournalLines
                .Where(l => l.CostCenter == null && l.OrderId != null)
                .ToListAsync();
            
            foreach (var l in lines) {
                var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == l.OrderId);
                if (order != null) l.CostCenter = order.Source;
            }

            int saved = await db.SaveChangesAsync();
            Console.WriteLine($"Sync Completed. Updated {entries.Count} entries and {lines.Count} lines. Saved {saved} changes.");
        }
    }
}
