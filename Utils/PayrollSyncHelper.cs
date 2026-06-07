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

            var payrollNumberLower = run.PayrollNumber.Trim().ToLower();

            // Find all posted journal lines debiting the accrued salaries account for this payroll run.
            // We search for mentions of the payroll number in entry Reference, Description, or line Description.
            var matchingLines = await db.JournalLines
                .Include(l => l.JournalEntry)
                .Where(l => l.AccountId == accrualAccId 
                            && l.Debit > 0 
                            && l.JournalEntry.Status == JournalEntryStatus.Posted
                            && l.JournalEntryId != run.JournalEntryId
                            && (
                                (l.JournalEntry.Reference != null && l.JournalEntry.Reference.ToLower().Contains(payrollNumberLower)) ||
                                (l.JournalEntry.Description != null && l.JournalEntry.Description.ToLower().Contains(payrollNumberLower)) ||
                                (l.Description != null && l.Description.ToLower().Contains(payrollNumberLower))
                            ))
                .ToListAsync();

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

            // Gather all text fields from this journal entry to look for payroll numbers
            var textToSearch = $"{entry.Reference} {entry.Description} {string.Join(" ", entry.Lines.Select(l => l.Description))}".ToLower();

            // Fetch all posted/paid payroll runs
            var payrollRuns = await db.PayrollRuns.Where(r => r.Status != PayrollStatus.Draft).ToListAsync();
            
            // Identify which runs are mentioned in the journal entry
            var runsToSync = payrollRuns.Where(r => textToSearch.Contains(r.PayrollNumber.ToLower())).ToList();

            foreach (var run in runsToSync)
            {
                await SyncPayrollRunPaymentsAsync(db, core, run.Id);
            }
        }
    }
}
