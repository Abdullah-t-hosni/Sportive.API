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

        var revNo = await _seq.NextAsync("JE");

        var reversal = new JournalEntry {
            EntryNumber = revNo, EntryDate = TimeHelper.GetEgyptTime(), Type = entry.Type, Status = JournalEntryStatus.Posted, Reference = entry.EntryNumber, Description = $"عكس: {entry.EntryNumber} — {reason}", ReversalOfId = entry.Id, CostCenter = entry.CostCenter, CreatedAt = TimeHelper.GetEgyptTime()
        };

        foreach (var line in entry.Lines) {
            reversal.Lines.Add(new JournalLine { AccountId = line.AccountId, Debit = line.Credit, Credit = line.Debit, Description = line.Description, CustomerId = line.CustomerId, SupplierId = line.SupplierId, CostCenter = line.CostCenter, CreatedAt = TimeHelper.GetEgyptTime() });
        }
        entry.Status = JournalEntryStatus.Reversed;
        _db.JournalEntries.Add(reversal);
        await _db.SaveChangesAsync();
        await PayrollSyncHelper.SyncPayrollRunsForJournalEntryAsync(_db, _core, entry);
        await _core.SyncEntityBalancesAsync();
    }

    public async Task<JournalEntry> PostManualEntryAsync(CreateJournalEntryDto dto, ClaimsPrincipal? user)
    {
        await _core.CheckDateLockAsync(dto.EntryDate, user);
        
        var userId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var type = dto.Type ?? JournalEntryType.Manual;
        var prefix = type == JournalEntryType.OpeningBalance ? "OPE" : "JE";
        var entryNumber = await _seq.NextAsync(prefix);

        if (!string.IsNullOrWhiteSpace(dto.Reference) && await _db.JournalEntries.AnyAsync(e => e.Reference == dto.Reference && e.Type == type))
        {
            throw new InvalidOperationException($"المرجع '{dto.Reference}' موجود مسبقاً لهذا النوع من القيود.");
        }

        // 💡 AUTO-REFERENCE: If reference is empty, default to the generated EntryNumber
        var finalReference = string.IsNullOrWhiteSpace(dto.Reference) ? entryNumber : dto.Reference;

        var vDate = dto.EntryDate.ToStoreTime();
        if (vDate.TimeOfDay == TimeSpan.Zero) vDate = vDate.Add(TimeHelper.GetEgyptTime().TimeOfDay);

        var entry = new JournalEntry { 
            EntryNumber = entryNumber, 
            EntryDate = vDate, 
            Description = dto.Description, 
            Reference = finalReference, 
            Type = type, 
            Status = JournalEntryStatus.Posted, 
            CreatedByUserId = userId, 
            CostCenter = (OrderSource?)dto.CostCenter,
            CreatedAt = TimeHelper.GetEgyptTime()
        };
        
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
            entry.CostCenter = user?.IsInRole("Cashier") == true ? OrderSource.POS : OrderSource.General;
        }

        foreach (var l in dto.Lines) {
            var account = await _db.Accounts.FindAsync(l.AccountId);
            if (account == null) throw new InvalidOperationException($"الحساب رقم {l.AccountId} غير موجود.");
            if (!account.AllowPosting && !((user?.IsInRole("Admin") ?? false) || (user?.IsInRole("SuperAdmin") ?? false)))
                throw new InvalidOperationException($"الحساب '{account.NameAr}' لا يقبل الترحيل المباشر.");

            entry.Lines.Add(new JournalLine { AccountId = l.AccountId, Debit = l.Debit, Credit = l.Credit, Description = l.Description, CustomerId = l.CustomerId, SupplierId = l.SupplierId, EmployeeId = l.EmployeeId, OrderId = l.OrderId, CostCenter = (OrderSource?)l.CostCenter ?? entry.CostCenter });
        }

        // التحقق من التوازن قبل الحفظ
        var totalDr = entry.Lines.Sum(l => l.Debit);
        var totalCr = entry.Lines.Sum(l => l.Credit);
        if (Math.Round(totalDr, 2) != Math.Round(totalCr, 2))
            throw new InvalidOperationException($"القيد غير متوازن: مجموع المدين ({totalDr}) لا يساوي مجموع الدائن ({totalCr})");

        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync();

        // 🔗 HR Link: Create EmployeeAdvance or deduct if account is "Employee Advances"
        var mappings = await _core.GetSafeSystemMappingsAsync();
        if (mappings.TryGetValue(MappingKeys.EmployeeAdvances.ToLower(), out var advAccId) && advAccId.HasValue)
        {
            var employeeAdvancesAccountId = advAccId.Value;
            foreach (var l in entry.Lines)
            {
                if (l.AccountId == employeeAdvancesAccountId && l.EmployeeId.HasValue)
                {
                    if (l.Debit > 0)
                    {
                        var advNo = await _seq.NextAsync("ADV");
                        var advance = new EmployeeAdvance
                        {
                            AdvanceNumber = advNo,
                            EmployeeId = l.EmployeeId.Value,
                            AdvanceDate = entry.EntryDate,
                            Amount = l.Debit,
                            Reason = l.Description ?? entry.Description,
                            Status = AdvanceStatus.Pending,
                            CreatedAt = TimeHelper.GetEgyptTime(),
                            CreatedByUserId = userId,
                            JournalEntryId = entry.Id
                        };
                        _db.EmployeeAdvances.Add(advance);
                    }
                    else if (l.Credit > 0)
                    {
                        // Deduct from pending advances
                        var pendingAdvances = await _db.EmployeeAdvances
                            .Where(a => a.EmployeeId == l.EmployeeId.Value && a.Status != AdvanceStatus.FullyDeducted)
                            .OrderBy(a => a.AdvanceDate)
                            .ToListAsync();

                        var remaining = l.Credit;
                        foreach (var adv in pendingAdvances)
                        {
                            if (remaining <= 0) break;
                            var canDeduct = Math.Min(adv.RemainingAmount, remaining);
                            adv.DeductedAmount += canDeduct;
                            adv.Status = adv.DeductedAmount >= adv.Amount
                                ? AdvanceStatus.FullyDeducted
                                : AdvanceStatus.PartiallyDeducted;
                            remaining -= canDeduct;
                            adv.UpdatedAt = TimeHelper.GetEgyptTime();
                        }
                    }
                }
            }
            await _db.SaveChangesAsync();
        }

        await _core.SyncEntityBalancesAsync();
        return entry;
    }

    public async Task<JournalEntry> UpdateManualEntryAsync(int id, UpdateJournalEntryDto dto, ClaimsPrincipal? user)
    {
        var entry = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == id);
        if (entry == null) throw new KeyNotFoundException("القيد غير موجود");

        // 🚨 PRO-ACCOUNTING: لا يجوز تعديل قيد مرحل (إلا للأدمن أو السوبر أدمن)
        if (entry.Status == JournalEntryStatus.Posted && !((user?.IsInRole("Admin") ?? false) || (user?.IsInRole("SuperAdmin") ?? false)))
            throw new InvalidOperationException("لا يمكن تعديل قيد مرحل. يرجى عمل قيد عكسي ثم قيد جديد.");

        await _core.CheckDateLockAsync(entry.EntryDate, user); // Check old date
        await _core.CheckDateLockAsync(dto.EntryDate, user);   // Check new date

        // التحقق من توازن القيد الجديد
        var totalDr = dto.Lines.Sum(l => l.Debit);
        var totalCr = dto.Lines.Sum(l => l.Credit);
        if (Math.Round(totalDr, 2) != Math.Round(totalCr, 2))
            throw new InvalidOperationException($"القيد غير متوازن: مجموع المدين ({totalDr}) لا يساوي مجموع الدائن ({totalCr})");

        var vDate = dto.EntryDate.ToStoreTime();
        if (vDate.TimeOfDay == TimeSpan.Zero) vDate = vDate.Add(TimeHelper.GetEgyptTime().TimeOfDay);

        // تحديث البيانات الأساسية
        entry.EntryDate = vDate;
        entry.Description = dto.Description;
        if (!string.IsNullOrWhiteSpace(dto.Reference) && await _db.JournalEntries.AnyAsync(e => e.Reference == dto.Reference && e.Type == entry.Type && e.Id != id))
        {
            throw new InvalidOperationException($"المرجع '{dto.Reference}' موجود مسبقاً في قيد آخر من نفس النوع.");
        }

        entry.Reference = string.IsNullOrWhiteSpace(dto.Reference) ? entry.EntryNumber : dto.Reference;
        entry.CostCenter = (OrderSource?)dto.CostCenter;
        entry.UpdatedAt = TimeHelper.GetEgyptTime();

        // تحديث الأسطر (مسح الحالية وإعادتها)
        _db.JournalLines.RemoveRange(entry.Lines);
        foreach (var l in dto.Lines)
        {
            var account = await _db.Accounts.FindAsync(l.AccountId);
            if (account == null) throw new InvalidOperationException($"الحساب رقم {l.AccountId} غير موجود.");
            if (!account.AllowPosting && !((user?.IsInRole("Admin") ?? false) || (user?.IsInRole("SuperAdmin") ?? false)))
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
                CostCenter = (OrderSource?)l.CostCenter ?? entry.CostCenter,
                CreatedAt = TimeHelper.GetEgyptTime()
            });
        }

        // --- مزامنة التغييرات مع الطلب المرتبط (إن وجد) ---
        Order? order = null;
        if (entry.OrderId.HasValue)
        {
            order = await _db.Orders.Include(o => o.Payments).FirstOrDefaultAsync(o => o.Id == entry.OrderId.Value);
        }
        else if ((entry.Type == JournalEntryType.SalesInvoice || entry.Type == JournalEntryType.Sales) && !string.IsNullOrEmpty(entry.Reference))
        {
            order = await _db.Orders.Include(o => o.Payments).FirstOrDefaultAsync(o => o.OrderNumber == entry.Reference);
        }

        if (order != null)
        {
            var salesAccountId = await _core.GetRequiredMappedAccountAsync(MappingKeys.Sales);
            var discountAccountId = await _core.GetRequiredMappedAccountAsync(MappingKeys.SalesDiscount);

            var revAccountIds = await _db.Accounts.Where(a => a.Code.StartsWith("4") && a.Id != discountAccountId).Select(a => a.Id).ToListAsync();

            decimal newSubTotal = dto.Lines.Where(l => l.AccountId == salesAccountId || revAccountIds.Contains(l.AccountId)).Sum(l => l.Credit);
            decimal newDiscount = dto.Lines.Where(l => l.AccountId == discountAccountId).Sum(l => l.Debit);
            if (newDiscount == 0)
            {
                var discAccountIds = await _db.Accounts.Where(a => a.Code.StartsWith("4102") || a.Code.StartsWith("4105")).Select(a => a.Id).ToListAsync();
                newDiscount = dto.Lines.Where(l => discAccountIds.Contains(l.AccountId)).Sum(l => l.Debit);
            }

            order.SubTotal = newSubTotal;
            order.DiscountAmount = newDiscount;
            order.TemporalDiscount = 0; // تصفير خصم العروض لأن القيد يدمج كل الخصومات في الحساب المالي
            order.TotalAmount = newSubTotal - newDiscount + order.DeliveryFee + order.TotalVatAmount;

            var mappings = await _core.GetSafeSystemMappingsAsync();
            var accountToMethodMap = new Dictionary<int, PaymentMethod>();
            foreach (var kvp in mappings)
            {
                if (kvp.Value.HasValue)
                {
                    var method = kvp.Key.ToLower() switch {
                        var k when k.Contains("vodafone") => PaymentMethod.Vodafone,
                        var k when k.Contains("instapay") => PaymentMethod.InstaPay,
                        var k when k.Contains("bank") || k.Contains("creditcard") => PaymentMethod.CreditCard,
                        var k when k.Contains("cash") => PaymentMethod.Cash,
                        _ => (PaymentMethod?)null
                    };
                    if (method.HasValue)
                    {
                        accountToMethodMap[kvp.Value.Value] = method.Value;
                    }
                }
            }

            // مسح المدفوعات القديمة لإعادة بنائها
            if (order.Payments.Any())
            {
                _db.OrderPayments.RemoveRange(order.Payments);
                order.Payments.Clear();
            }

            var paymentsToCreate = dto.Lines
                .Where(l => l.Debit > 0 && accountToMethodMap.ContainsKey(l.AccountId))
                .GroupBy(l => accountToMethodMap[l.AccountId])
                .Select(g => new OrderPayment
                {
                    OrderId = order.Id,
                    Method = g.Key,
                    Amount = g.Sum(l => l.Debit),
                    IsPosted = true
                }).ToList();

            foreach (var p in paymentsToCreate)
            {
                order.Payments.Add(p);
            }

            decimal newPaidAmount = paymentsToCreate.Sum(p => p.Amount);
            order.PaidAmount = newPaidAmount;

            // تحديث طريقة الدفع
            if (order.Payments.Count == 1)
            {
                order.PaymentMethod = order.Payments.First().Method;
            }
            else if (order.Payments.Count > 1)
            {
                order.PaymentMethod = PaymentMethod.Mixed;
            }
            else
            {
                order.PaymentMethod = PaymentMethod.Credit;
            }

            // تحديث حالة الدفع
            if (order.PaidAmount >= order.TotalAmount)
            {
                order.PaymentStatus = PaymentStatus.Paid;
            }
            else if (order.PaidAmount > 0)
            {
                order.PaymentStatus = PaymentStatus.PartiallyPaid;
            }
            else
            {
                order.PaymentStatus = PaymentStatus.Pending;
            }
        }

        await _db.SaveChangesAsync();
        await _core.SyncEntityBalancesAsync();
        return entry;
    }
}
