
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Sportive.API.Scratch;

public class TrialBalanceAudit {
    public static async Task Run(AppDbContext db) {
        var from = new DateTime(2026, 1, 1);
        var to = DateTime.Now;

        // 1. التحقق من توازن القيود ككل (Aggregation on SQL side)
        var totalDr = await db.JournalLines
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted && l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to)
            .SumAsync(l => l.Debit);
        
        var totalCr = await db.JournalLines
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted && l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to)
            .SumAsync(l => l.Credit);

        Console.WriteLine($"--- PERIOD AUDIT ---");
        Console.WriteLine($"Total Dr: {totalDr:N2}, Total Cr: {totalCr:N2}");
        if (Math.Abs(totalDr - totalCr) > 0.01m) {
            Console.WriteLine($"🚨 ALERT: Period is NOT balanced! Diff: {totalDr - totalCr:N2}");
        } else {
            Console.WriteLine($"✅ Period is balanced.");
        }

        // 2. التحقق من توازن الرصيد الافتتاحي
        var accounts = await db.Accounts.Where(a => a.IsActive).ToListAsync();
        var opMovementsDr = await db.JournalLines
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted && l.JournalEntry.EntryDate < from)
            .SumAsync(l => l.Debit);
        var opMovementsCr = await db.JournalLines
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted && l.JournalEntry.EntryDate < from)
            .SumAsync(l => l.Credit);

        var opInitialDr = accounts.Where(a => a.Nature == AccountNature.Debit).Sum(a => a.OpeningBalance);
        var opInitialCr = accounts.Where(a => a.Nature == AccountNature.Credit).Sum(a => a.OpeningBalance);

        var totalOpDr = opMovementsDr + opInitialDr;
        var totalOpCr = opMovementsCr + opInitialCr;

        Console.WriteLine($"\n--- OPENING AUDIT ---");
        Console.WriteLine($"Total Op Dr: {totalOpDr:N2}, Total Op Cr: {totalOpCr:N2}");
        if (Math.Abs(totalOpDr - totalOpCr) > 0.01m) {
            Console.WriteLine($"🚨 ALERT: Opening Balance is NOT balanced! Diff: {totalOpDr - totalOpCr:N2}");
        } else {
            Console.WriteLine($"✅ Opening Balance is balanced.");
        }

        // 3. البحث عن القيود المضروبة (Unbalanced Individual Entries)
        Console.WriteLine($"\n--- INDIVIDUAL ENTRY SCAN ---");
        var brokenEntries = await db.JournalLines
            .GroupBy(l => l.JournalEntryId)
            .Select(g => new { 
                Id = g.Key, 
                Diff = g.Sum(l => l.Debit) - g.Sum(l => l.Credit) 
            })
            .Where(x => Math.Abs(x.Diff) > 0.01m)
            .ToListAsync();

        if (brokenEntries.Any()) {
            Console.WriteLine($"❌ Found {brokenEntries.Count} unbalanced journal entries!");
            foreach(var e in brokenEntries) {
                var info = await db.JournalEntries.FindAsync(e.Id);
                Console.WriteLine($" - Entry #{info?.EntryNumber} ({info?.EntryDate:yyyy-MM-dd}): Diff = {e.Diff:N2}");
            }
        } else {
            Console.WriteLine($"✅ All individual entries are balanced.");
        }
    }
}
