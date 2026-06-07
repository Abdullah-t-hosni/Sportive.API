using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;

namespace Sportive.API.Utils
{
    public static class PayrollSyncHelper
    {
        public static bool IsTextMatchingPayrollPeriod(string? text, string payrollNumber, int year, int month)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            
            var textLower = text.ToLower();
            
            // 1. Direct match with payroll number
            if (textLower.Contains(payrollNumber.ToLower())) return true;
            
            // 2. Match month names and year
            string[] arMonths = { "", "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };
            string[] enMonths = { "", "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
            
            var arMonth = arMonths[month];
            var enMonth = enMonths[month];
            
            var yearStr = year.ToString();
            var shortYearStr = (year % 100).ToString("D2"); // e.g. "26"
            
            // Check if it mentions the month and the 4-digit year
            if (textLower.Contains(yearStr))
            {
                if (textLower.Contains(arMonth) || textLower.Contains(enMonth))
                {
                    return true;
                }
                
                // Also check numerical patterns: e.g. "5/2026", "05/2026", "2026/5", "2026/05", "5-2026", "05-2026", "2026-5", "2026-05"
                var pattern1 = $"{month}/{yearStr}";
                var pattern2 = $"{month:D2}/{yearStr}";
                var pattern3 = $"{yearStr}/{month}";
                var pattern4 = $"{yearStr}/{month:D2}";
                var pattern5 = $"{month}-{yearStr}";
                var pattern6 = $"{month:D2}-{yearStr}";
                var pattern7 = $"{yearStr}-{month}";
                var pattern8 = $"{yearStr}-{month:D2}";
                
                if (textLower.Contains(pattern1) || textLower.Contains(pattern2) || 
                    textLower.Contains(pattern3) || textLower.Contains(pattern4) ||
                    textLower.Contains(pattern5) || textLower.Contains(pattern6) ||
                    textLower.Contains(pattern7) || textLower.Contains(pattern8))
                {
                    return true;
                }
            }
            
            // Check if it mentions the month and the 2-digit year (with standard separator, e.g. "5/26", "05-26", "26/5")
            if (textLower.Contains(arMonth) || textLower.Contains(enMonth))
            {
                if (textLower.Contains($" {shortYearStr}") || textLower.Contains($"/{shortYearStr}") || textLower.Contains($"-{shortYearStr}"))
                {
                    return true;
                }
            }
            
            // Check numeric short year: e.g. "5/26", "05/26", "26/5", "26/05", "5-26", "05-26"
            var p1 = $"{month}/{shortYearStr}";
            var p2 = $"{month:D2}/{shortYearStr}";
            var p3 = $"{shortYearStr}/{month}";
            var p4 = $"{shortYearStr}/{month:D2}";
            var p5 = $"{month}-{shortYearStr}";
            var p6 = $"{month:D2}-{shortYearStr}";
            
            if (textLower.Contains(p1) || textLower.Contains(p2) || 
                textLower.Contains(p3) || textLower.Contains(p4) ||
                textLower.Contains(p5) || textLower.Contains(p6))
            {
                return true;
            }

            return false;
        }

        public static async Task SyncPayrollRunPaymentsAsync(AppDbContext db, AccountingCoreService core, int payrollRunId)
        {
            var run = await db.PayrollRuns
                .Include(p => p.Items).ThenInclude(i => i.Employee)
                .FirstOrDefaultAsync(p => p.Id == payrollRunId);
            if (run == null || run.Status == PayrollStatus.Draft) return;

            var mapDict = await core.GetSafeSystemMappingsAsync();
            
            // Get the accrued salaries account (Salaries Payable)
            int accrualAccId;
            if (run.AccruedSalariesAccountId.HasValue)
            {
                accrualAccId = run.AccruedSalariesAccountId.Value;
            }
            else
            {
                // Fallback to system mapping
                if (mapDict.TryGetValue(MappingKeys.SalariesPayable.ToLower(), out var val) && val.HasValue)
                {
                    accrualAccId = val.Value;
                }
                else
                {
                    // If not mapped, we cannot sync payments
                    return;
                }
            }

            // Find all posted journal lines debiting the accrued salaries account for this payroll run.
            // We fetch the candidate lines from the database.
            var candidateLines = await db.JournalLines
                .Include(l => l.JournalEntry)
                .Where(l => l.AccountId == accrualAccId 
                            && l.Debit > 0 
                            && l.JournalEntry.Status == JournalEntryStatus.Posted
                            && l.JournalEntryId != run.JournalEntryId)
                .ToListAsync();

            // Filter lines in-memory using our flexible pattern matcher
            var matchingLines = candidateLines
                .Where(l => 
                    IsTextMatchingPayrollPeriod(l.JournalEntry.Reference, run.PayrollNumber, run.PeriodYear, run.PeriodMonth) ||
                    IsTextMatchingPayrollPeriod(l.JournalEntry.Description, run.PayrollNumber, run.PeriodYear, run.PeriodMonth) ||
                    IsTextMatchingPayrollPeriod(l.Description, run.PayrollNumber, run.PeriodYear, run.PeriodMonth)
                )
                .ToList();

            bool hasChanges = false;

            foreach (var item in run.Items)
            {
                var employeeLines = matchingLines.Where(l => l.EmployeeId == item.EmployeeId).ToList();
                if (employeeLines.Any())
                {
                    var totalDebited = employeeLines.Sum(l => l.Debit);
                    var latestLine = employeeLines.OrderByDescending(l => l.JournalEntry.EntryDate).ThenByDescending(l => l.Id).First();
                    
                    if (item.PaidAmount != totalDebited || item.PaymentJournalEntryId != latestLine.JournalEntryId || !item.IsPaid)
                    {
                        item.PaidAmount = totalDebited;
                        item.PaymentJournalEntryId = latestLine.JournalEntryId;
                        item.PaidAt = latestLine.JournalEntry.EntryDate;
                        item.IsPaid = totalDebited >= item.NetPayable && item.NetPayable > 0;
                        hasChanges = true;
                    }
                }
                else
                {
                    // No matching lines found. Let's make sure if it was paid, we reset it
                    if (item.PaidAmount > 0 || item.IsPaid || item.PaymentJournalEntryId.HasValue)
                    {
                        item.PaidAmount = 0;
                        item.IsPaid = false;
                        item.PaidAt = null;
                        item.PaymentJournalEntryId = null;
                        hasChanges = true;
                    }
                }
            }

            // Sync overall payroll run status
            if (run.PaymentJournalEntryId.HasValue)
            {
                var exists = await db.JournalEntries.AnyAsync(je => je.Id == run.PaymentJournalEntryId.Value);
                if (!exists)
                {
                    run.PaymentJournalEntryId = null;
                    hasChanges = true;
                }
            }

            bool allPaid = run.Items.All(i => i.IsPaid || i.NetPayable <= 0);
            if (allPaid && run.Status == PayrollStatus.Posted)
            {
                run.Status = PayrollStatus.Paid;
                hasChanges = true;
            }
            else if (!allPaid && run.Status == PayrollStatus.Paid)
            {
                run.Status = PayrollStatus.Posted;
                hasChanges = true;
            }

            if (hasChanges)
            {
                run.UpdatedAt = TimeHelper.GetEgyptTime();
                await db.SaveChangesAsync();
            }
        }

        public static async Task SyncPayrollRunsForJournalEntryAsync(AppDbContext db, AccountingCoreService core, JournalEntry entry)
        {
            if (entry == null) return;

            // Fetch all posted/paid payroll runs
            var payrollRuns = await db.PayrollRuns.Where(r => r.Status != PayrollStatus.Draft).ToListAsync();
            
            // Identify which runs are mentioned in the journal entry
            var runsToSync = payrollRuns.Where(r => 
                IsTextMatchingPayrollPeriod(entry.Reference, r.PayrollNumber, r.PeriodYear, r.PeriodMonth) ||
                IsTextMatchingPayrollPeriod(entry.Description, r.PayrollNumber, r.PeriodYear, r.PeriodMonth) ||
                entry.Lines.Any(l => IsTextMatchingPayrollPeriod(l.Description, r.PayrollNumber, r.PeriodYear, r.PeriodMonth))
            ).ToList();

            foreach (var run in runsToSync)
            {
                await SyncPayrollRunPaymentsAsync(db, core, run.Id);
            }
        }
    }
}
