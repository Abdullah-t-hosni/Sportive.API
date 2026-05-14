using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Sportive.API.Scratch;

public class DbInspector
{
    public static async Task InspectJournalEntries(AppDbContext db)
    {
        var emptyRefEntries = await db.JournalEntries
            .Where(e => e.Reference == "")
            .Select(e => new { e.Id, e.EntryNumber, e.Type, e.Reference })
            .ToListAsync();

        Console.WriteLine($"Found {emptyRefEntries.Count} entries with empty string reference.");
        foreach (var e in emptyRefEntries)
        {
            Console.WriteLine($"- ID: {e.Id}, Num: {e.EntryNumber}, Type: {e.Type}, Ref: '{e.Reference}'");
        }

        var nullRefEntriesCount = await db.JournalEntries.CountAsync(e => e.Reference == null);
        Console.WriteLine($"Found {nullRefEntriesCount} entries with NULL reference.");
    }
}
