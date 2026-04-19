using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Scratch
{
    public class JournalIntegrityCheck
    {
        public static async Task Run(IServiceProvider services)
        {
            var db = services.GetRequiredService<AppDbContext>();
            Console.WriteLine("--- Checking Journal Entries Integrity ---");

            var unbalanced = await db.JournalEntries
                .Include(e => e.Lines)
                .Select(e => new {
                    e.Id,
                    e.EntryNumber,
                    e.EntryDate,
                    e.Type,
                    e.Status,
                    e.Reference,
                    e.Description,
                    TotalDebit = e.Lines.Sum(l => l.Debit),
                    TotalCredit = e.Lines.Sum(l => l.Credit),
                    Diff = e.Lines.Sum(l => l.Debit) - e.Lines.Sum(l => l.Credit)
                })
                .Where(x => Math.Abs(x.Diff) > 0.001m)
                .Take(50)
                .ToListAsync();

            if (!unbalanced.Any())
            {
                Console.WriteLine("All Journal Entries are balanced.");
            }
            else
            {
                Console.WriteLine($"Found {unbalanced.Count} unbalanced entries:");
                foreach (var x in unbalanced)
                {
                    Console.WriteLine($"ID: {x.Id} | Num: {x.EntryNumber} | Type: {x.Type} | Date: {x.EntryDate:yyyy-MM-dd} | Ref: {x.Reference} | Debit: {x.TotalDebit} | Credit: {x.TotalCredit} | Diff: {x.Diff}");
                    
                    // Show lines for the first few
                    var fullEntry = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account).FirstOrDefaultAsync(e => e.Id == x.Id);
                    if (fullEntry != null)
                    {
                        foreach(var l in fullEntry.Lines)
                        {
                            Console.WriteLine($"  -> Account: {l.Account.Code} ({l.Account.NameAr}) | Dr: {l.Debit} | Cr: {l.Credit}");
                        }
                    }
                    Console.WriteLine(new string('-', 50));
                }
            }

            Console.WriteLine("--- End of Check ---");
        }
    }
}
