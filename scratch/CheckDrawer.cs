using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Scratch
{
    public class CheckDrawer
    {
        public static async Task Run(IServiceProvider services)
        {
            var db = services.GetRequiredService<AppDbContext>();
            var today = TimeHelper.GetEgyptTime().Date;
            
            Console.WriteLine($"Checking Ledger for Today: {today:yyyy-MM-dd}");
            
            // 1. Get Cash Account (assuming 110101 is POS Cash)
            var cashAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Code == "110101");
            if (cashAccount == null) {
                Console.WriteLine("Cash account 110101 not found.");
                return;
            }
            
            Console.WriteLine($"Cash Account: {cashAccount.NameAr} (ID: {cashAccount.Id})");
            
            // 2. Query Journal Lines for today
            var lines = await db.JournalLines
                .Include(l => l.JournalEntry)
                .Where(l => l.AccountId == cashAccount.Id && l.JournalEntry.EntryDate >= today)
                .ToListAsync();
            
            Console.WriteLine($"Total Lines Today: {lines.Count}");
            
            var breakdown = lines.GroupBy(l => l.CostCenter)
                .Select(g => new { 
                    Source = g.Key?.ToString() ?? "NULL", 
                    Count = g.Count(), 
                    Balance = g.Sum(l => l.Debit - l.Credit) 
                });
            
            foreach (var b in breakdown)
            {
                Console.WriteLine($"Source: {b.Source} | Count: {b.Count} | Balance: {b.Balance:N2}");
            }
            
            if (lines.Any()) {
                Console.WriteLine("\nSample Lines:");
                foreach (var l in lines.Take(5)) {
                    Console.WriteLine($"- {l.JournalEntry.EntryDate:HH:mm} | Ref: {l.JournalEntry.Reference} | Source: {l.CostCenter?.ToString() ?? "NULL"} | Dr: {l.Debit} | Cr: {l.Credit}");
                }
            }
        }
    }
}
