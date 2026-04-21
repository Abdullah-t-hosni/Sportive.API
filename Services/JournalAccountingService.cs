using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using Sportive.API.DTOs;
using System.Security.Claims;

namespace Sportive.API.Services;

/// <summary>
/// خدمة مخصصة للقيود اليدوية وعكس القيود
/// </summary>
public class JournalAccountingService
{
    private readonly AppDbContext _db;
    private readonly SequenceService _seq;
    private readonly AccountingCoreService _core;

    public JournalAccountingService(AppDbContext db, SequenceService seq, AccountingCoreService core)
    {
        _db = db;
        _seq = seq;
        _core = core;
    }

    public async Task ReverseEntryAsync(int journalEntryId, string reason)
    {
        var entry = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == journalEntryId);
        if (entry == null || entry.Status == JournalEntryStatus.Reversed) return;

        var revNo = await _seq.NextAsync("JE", async (db, pattern) => {
            var max = await db.JournalEntries.Where(e => EF.Functions.Like(e.EntryNumber, pattern)).Select(e => e.EntryNumber).ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0).DefaultIfEmpty(0).Max();
        });

        var reversal = new JournalEntry {
            EntryNumber = revNo, EntryDate = TimeHelper.GetEgyptTime(), Type = entry.Type, Status = JournalEntryStatus.Posted, Reference = entry.EntryNumber, Description = $"عكس: {entry.EntryNumber} — {reason}", ReversalOfId = entry.Id, CostCenter = entry.CostCenter, CreatedAt = TimeHelper.GetEgyptTime()
        };

        foreach (var line in entry.Lines) {
            reversal.Lines.Add(new JournalLine { AccountId = line.AccountId, Debit = line.Credit, Credit = line.Debit, Description = line.Description, CustomerId = line.CustomerId, SupplierId = line.SupplierId, CostCenter = line.CostCenter, CreatedAt = TimeHelper.GetEgyptTime() });
        }
        entry.Status = JournalEntryStatus.Reversed;
        _db.JournalEntries.Add(reversal);
        await _db.SaveChangesAsync();
    }

    public async Task<JournalEntry> PostManualEntryAsync(CreateJournalEntryDto dto, ClaimsPrincipal? user)
    {
        await _core.CheckDateLockAsync(dto.EntryDate, user);
        
        var userId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var type = dto.Type ?? JournalEntryType.Manual;
        var prefix = type == JournalEntryType.OpeningBalance ? "OPE" : "JE";
        var entryNumber = await _seq.NextAsync(prefix, async (db, pattern) => {
            var max = await db.JournalEntries.Where(e => EF.Functions.Like(e.EntryNumber, pattern)).Select(e => e.EntryNumber).ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0).DefaultIfEmpty(0).Max();
        });

        var entry = new JournalEntry { EntryNumber = entryNumber, EntryDate = dto.EntryDate, Description = dto.Description, Reference = dto.Reference, Type = type, Status = JournalEntryStatus.Posted, CreatedByUserId = userId, CostCenter = dto.CostCenter };
        
        // 🎯 AUTO-RESOLVE COST CENTER: If not provided, try to infer from the first line that has an OrderId
        if (entry.CostCenter == null)
        {
            var firstOrderLine = dto.Lines.FirstOrDefault(l => l.OrderId.HasValue);
            if (firstOrderLine != null)
            {
                entry.CostCenter = await _db.Orders.Where(o => o.Id == firstOrderLine.OrderId!.Value).Select(o => (OrderSource?)o.Source).FirstOrDefaultAsync();
            }
        }
        
        // If still null, default based on User Role (Cashiers -> POS, Others -> Website)
        if (entry.CostCenter == null)
        {
            entry.CostCenter = user?.IsInRole("Cashier") == true ? OrderSource.POS : OrderSource.Website;
        }

        foreach (var l in dto.Lines) {
            var account = await _db.Accounts.FindAsync(l.AccountId);
            if (account == null) throw new InvalidOperationException($"الحساب رقم {l.AccountId} غير موجود.");
            if (!account.AllowPosting && !user.IsInRole("Admin"))
                throw new InvalidOperationException($"الحساب '{account.NameAr}' لا يقبل الترحيل المباشر.");

            entry.Lines.Add(new JournalLine { AccountId = l.AccountId, Debit = l.Debit, Credit = l.Credit, Description = l.Description, CustomerId = l.CustomerId, SupplierId = l.SupplierId, EmployeeId = l.EmployeeId, OrderId = l.OrderId, CostCenter = l.CostCenter ?? entry.CostCenter });
        }

        // التحقق من التوازن قبل الحفظ
        var totalDr = entry.Lines.Sum(l => l.Debit);
        var totalCr = entry.Lines.Sum(l => l.Credit);
        if (Math.Round(totalDr, 2) != Math.Round(totalCr, 2))
            throw new InvalidOperationException($"القيد غير متوازن: مجموع المدين ({totalDr}) لا يساوي مجموع الدائن ({totalCr})");

        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task<JournalEntry> UpdateManualEntryAsync(int id, UpdateJournalEntryDto dto, ClaimsPrincipal? user)
    {
        var entry = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == id);
        if (entry == null) throw new KeyNotFoundException("القيد غير موجود");

        // 🚨 PRO-ACCOUNTING: لا يجوز تعديل قيد مرحل
        if (entry.Status == JournalEntryStatus.Posted && !(user?.IsInRole("Admin") ?? false))
            throw new InvalidOperationException("لا يمكن تعديل قيد مرحل. يرجى عمل قيد عكسي ثم قيد جديد.");

        await _core.CheckDateLockAsync(entry.EntryDate, user); // Check old date
        await _core.CheckDateLockAsync(dto.EntryDate, user);   // Check new date

        // التحقق من توازن القيد الجديد
        var totalDr = dto.Lines.Sum(l => l.Debit);
        var totalCr = dto.Lines.Sum(l => l.Credit);
        if (Math.Round(totalDr, 2) != Math.Round(totalCr, 2))
            throw new InvalidOperationException($"القيد غير متوازن: مجموع المدين ({totalDr}) لا يساوي مجموع الدائن ({totalCr})");

        // تحديث البيانات الأساسية
        entry.EntryDate = dto.EntryDate;
        entry.Description = dto.Description;
        entry.Reference = dto.Reference;
        entry.CostCenter = dto.CostCenter;
        entry.UpdatedAt = TimeHelper.GetEgyptTime();

        // تحديث الأسطر (مسح الحالية وإعادتها)
        _db.JournalLines.RemoveRange(entry.Lines);
        foreach (var l in dto.Lines)
        {
            var account = await _db.Accounts.FindAsync(l.AccountId);
            if (account == null) throw new InvalidOperationException($"الحساب رقم {l.AccountId} غير موجود.");
            if (!account.AllowPosting && !(user?.IsInRole("Admin") ?? false))
                throw new InvalidOperationException($"الحساب '{account.NameAr}' لا يقبل الترحيل المباشر.");

            entry.Lines.Add(new JournalLine
            {
                AccountId = l.AccountId,
                Debit = l.Debit,
                Credit = l.Credit,
                Description = l.Description,
                CustomerId = l.CustomerId,
                SupplierId = l.SupplierId,
                EmployeeId = l.EmployeeId,
                OrderId = l.OrderId,
                CostCenter = l.CostCenter ?? dto.CostCenter,
                CreatedAt = TimeHelper.GetEgyptTime()
            });
        }

        await _db.SaveChangesAsync();
        return entry;
    }
}
