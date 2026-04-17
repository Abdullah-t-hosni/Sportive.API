
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using System;
using System.Linq;

namespace Sportive.API.Scratch;

public class AccCheck {
    public static async Task Run(AppDbContext db) {
        var inactiveWithLines = await db.JournalLines
            .Where(l => !l.Account.IsActive)
            .GroupBy(l => new { l.Account.Id, l.Account.Code, l.Account.NameAr })
            .Select(g => new { 
                g.Key.Id, 
                g.Key.Code, 
                g.Key.NameAr, 
                Count = g.Count(), 
                SumBalance = g.Sum(l => l.Debit - l.Credit) 
            })
            .ToListAsync();

        foreach(var a in inactiveWithLines) {
            Console.WriteLine($"Inactive Account: [{a.Code}] {a.NameAr} - {a.Count} lines - Balance: {a.SumBalance}");
        }
    }
}
