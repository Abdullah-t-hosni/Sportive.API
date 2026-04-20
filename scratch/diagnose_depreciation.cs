
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

public class DiagnosticScript
{
    private readonly AppDbContext _db;
    public DiagnosticScript(AppDbContext db) => _db = db;

    public async Task Run()
    {
        var emptyEntries = await _db.JournalEntries
            .Where(e => (e.Description == null || e.Description == "") && e.Type == JournalEntryType.AssetDepreciation)
            .ToListAsync();
        
        Console.WriteLine($"Found {emptyEntries.Count} AssetDepreciation entries with empty descriptions.");
        foreach(var e in emptyEntries)
        {
            Console.WriteLine($"Entry ID: {e.Id}, Number: {e.EntryNumber}, Date: {e.EntryDate}, Ref: {e.Reference}");
        }

        var emptyLines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => (l.Description == null || l.Description == "") && l.JournalEntry.Type == JournalEntryType.AssetDepreciation)
            .ToListAsync();
        
        Console.WriteLine($"Found {emptyLines.Count} AssetDepreciation lines with empty descriptions.");
    }
}
