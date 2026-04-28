using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using System.Text.Json;
using Sportive.API.DTOs;
using System.Security.Claims;

namespace Sportive.API.Services;

/// <summary>
/// الخدمة المركزية للمحاسبة - تحتوي على الوظائف الأساسية والمساعدة والمشتركة بين الخدمات المحاسبية التخصصية.
/// </summary>
public class AccountingCoreService
{
    private readonly AppDbContext _db;
    private readonly SequenceService _seq;
    private readonly ILogger<AccountingCoreService> _logger;
    private readonly INotificationService _notifications;

    // كودات الحسابات الافتتاحية (Fallback)
    public const string CASH_CASHIER  = "110101";
    public const string CASH_WEBSITE  = "110102";
    public const string CASH_ACCOUNTS = "110103";
    public const string BANK          = "110202";
    public const string VODAFONE      = "110701";
    public const string INSTAPAY      = "110703";
    public const string VODAFONE_WEB  = "110702";
    public const string INSTAPAY_WEB  = "110704";
    public const string RECEIVABLES   = "1103";
    public const string PAYABLES      = "2101";
    public const string INVENTORY     = "1106";
    public const string SALES_REVENUE = "4101";
    public const string SALES_RETURN  = "4102";
    public const string SALES_DISCOUNT = "410101";
    public const string DELIVERY_REVENUE = "420101";
    public const string COGS          = "51101";
    public const string PURCHASES_NET = "511";
    public const string PURCHASE_DISC = "420102";
    public const string VAT_OUTPUT    = "2104";
    public const string VAT_INPUT     = "2105";

    public AccountingCoreService(AppDbContext db, SequenceService seq, ILogger<AccountingCoreService> logger, INotificationService notifications)
    {
        _db = db;
        _seq = seq;
        _logger = logger;
        _notifications = notifications;
    }

    public async Task CheckDateLockAsync(DateTime date, ClaimsPrincipal? user)
    {
        if (user != null && user.IsInRole("Admin")) return; // Admin bypass

        var settings = await _db.StoreInfo.AsNoTracking().FirstOrDefaultAsync();
        if (settings?.AccountingLockDate != null && date.Date <= settings.AccountingLockDate.Value.Date)
        {
            throw new InvalidOperationException($"الفترة المحاسبية (حتى {settings.AccountingLockDate.Value:yyyy-MM-dd}) مغلقة. لا يمكن الإضافة أو التعديل في فترة مغلقة.");
        }
    }

    public async Task<Dictionary<string, int?>> GetSafeSystemMappingsAsync()
    {
        return await _db.AccountSystemMappings
            .AsNoTracking()
            .ToDictionaryAsync(m => m.Key, m => m.AccountId, StringComparer.OrdinalIgnoreCase);
    }

    public string GetMap(Dictionary<string, int?> map, string key, string fallback)
    {
        if (map.TryGetValue(key.ToLower(), out var id) && id.HasValue)
            return $"ID:{id.Value}";
        return fallback;
    }

    public async Task<bool> EntryExistsAsync(JournalEntryType type, string reference)
    {
        if (string.IsNullOrEmpty(reference)) return false;
        return await _db.JournalEntries
            .AnyAsync(e => e.Type == type 
                         && e.Reference != null 
                         && e.Reference.Trim().ToLower() == reference.Trim().ToLower());
    }

    public async Task<JournalEntry> PostEntryAsync(
        JournalEntryType type,
        string reference,
        string description,
        DateTime date,
        List<(string code, decimal debit, decimal credit, string desc)> lines,
        int? orderId    = null,
        int? customerId = null,
        int? supplierId = null,
        int? purchaseInvoiceId = null,
        OrderSource? source = null,
        int? employeeId = null)
    {
        // 🛡️ SMART SYNC: If entry exists, update it instead of returning early
        JournalEntry? existing = null;
        if (!string.IsNullOrEmpty(reference))
        {
            var cleanRef = reference.Trim().ToLower();
            _logger.LogInformation("[Accounting] Searching for existing entry Type={Type}, Ref={Ref}", type, cleanRef);
            
            existing = await _db.JournalEntries
                .Include(e => e.Lines)
                .FirstOrDefaultAsync(e => e.Type == type && e.Reference != null && e.Reference.Trim().ToLower() == cleanRef);

            if (existing != null)
                _logger.LogInformation("[Accounting] Found existing entry ID={Id}, Number={Num} to update.", existing.Id, existing.EntryNumber);
            else
                _logger.LogWarning("[Accounting] No existing entry found for Ref={Ref}. A new one will be created.", cleanRef);
        }

        var totalDr = lines.Sum(l => l.debit);
        var totalCr = lines.Sum(l => l.credit);
        if (Math.Round(totalDr, 2) != Math.Round(totalCr, 2))
            throw new InvalidOperationException($"القيد غير متوازن: مدين={totalDr}, دائن={totalCr} | {reference}");

        JournalEntry entry;
        if (existing != null)
        {
            entry = existing;
            entry.Description = description;
            entry.EntryDate = date;
            entry.UpdatedAt = TimeHelper.GetEgyptTime();
            entry.OrderId = orderId;
            entry.PurchaseInvoiceId = purchaseInvoiceId;
            entry.CostCenter = source;

            // Clear old lines
            _db.JournalLines.RemoveRange(entry.Lines);
            entry.Lines.Clear();
        }
        else
        {
            // 🎯 AUTO-RESOLVE COST CENTER: If not provided, try to infer from the order
            if (source == null && orderId.HasValue)
            {
                source = await _db.Orders.Where(o => o.Id == orderId.Value).Select(o => (OrderSource?)o.Source).FirstOrDefaultAsync();
            }

            // 📝 Journal Numbering (JE-POS-xxxx, JE-WEB-xxxx, JE-GEN-xxxx)
            var jePrefix = source switch {
                OrderSource.POS     => "JE-POS",
                OrderSource.Website => "JE-WEB",
                _                   => "JE-GEN"
            };
            var entryNo = await _seq.NextAsync(jePrefix, async (db, pattern) =>
            {
                var max = await db.JournalEntries
                    .Where(e => EF.Functions.Like(e.EntryNumber, pattern))
                    .Select(e => e.EntryNumber)
                    .ToListAsync();
                return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0).DefaultIfEmpty(0).Max();
            });

            entry = new JournalEntry
            {
                EntryNumber = entryNo,
                EntryDate   = date,
                Type        = type,
                Status      = JournalEntryStatus.Posted,
                Reference   = reference,
                Description = description,
                OrderId     = orderId,
                PurchaseInvoiceId = purchaseInvoiceId,
                CostCenter  = source,
                CreatedAt   = TimeHelper.GetEgyptTime(),
            };
            _db.JournalEntries.Add(entry);
        }

        var resolvedAccounts = new Dictionary<string, (int Id, bool ExactMatch, string? ErrorNote)>();
        var accountIdCache   = new Dictionary<int, Account>();

        foreach (var (code, _, _, _) in lines)
        {
            if (resolvedAccounts.ContainsKey(code)) continue;
            var resolved = await GetAccountIdAsync(code);
            resolvedAccounts[code] = resolved;
            if (!accountIdCache.ContainsKey(resolved.Id))
            {
                var acct = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == resolved.Id);
                if (acct != null) accountIdCache[resolved.Id] = acct;
            }
        }

        foreach (var (code, debit, credit, desc) in lines)
        {
            if (debit == 0 && credit == 0) continue;
            var (accountId, exactMatch, errorNote) = resolvedAccounts[code];
            var finalDesc = exactMatch ? desc : $"{desc} [تنبيه: {errorNote}]";
            accountIdCache.TryGetValue(accountId, out var actualAccount);
            var realCode = actualAccount?.Code ?? "";

            bool isReceivables = realCode.StartsWith("1103") || realCode.StartsWith("1202");
            bool isPayables = realCode.StartsWith("2101") || realCode.StartsWith("2102");
            bool isSalesOrPurchase = type == JournalEntryType.SalesInvoice || type == JournalEntryType.SalesReturn || type == JournalEntryType.PurchaseInvoice || type == JournalEntryType.PurchaseReturn;

            bool isEmployeeAccount = realCode.StartsWith("22") || realCode.StartsWith("12") || realCode.StartsWith("52") || realCode.StartsWith("4109") ||
                                     actualAccount?.NameAr?.Contains("موظف") == true || actualAccount?.NameEn?.ToLower().Contains("employee") == true ||
                                     actualAccount?.NameAr?.Contains("سلف") == true || actualAccount?.NameAr?.Contains("أجور") == true || actualAccount?.NameAr?.Contains("رواتب") == true;
            
            bool isDiscountAccount = actualAccount?.NameAr?.Contains("خصم") == true || actualAccount?.NameEn?.ToLower().Contains("discount") == true;
            
            entry.Lines.Add(new JournalLine
            {
                AccountId   = accountId,
                Debit       = debit,
                Credit      = credit,
                Description = finalDesc,
                CustomerId  = (isReceivables || isSalesOrPurchase) ? customerId : null,
                SupplierId  = (isPayables || isSalesOrPurchase) ? supplierId : null,
                EmployeeId  = (isEmployeeAccount || ((type == JournalEntryType.SalesInvoice || type == JournalEntryType.SalesReturn) && isDiscountAccount)) ? employeeId : (type == JournalEntryType.Payroll ? employeeId : null),
                OrderId     = orderId,
                PurchaseInvoiceId = purchaseInvoiceId,
                CostCenter  = source, // 🎯 حفظ مركز التكلفة على كل سطر محاسبي
                CreatedAt   = TimeHelper.GetEgyptTime(),
            });
        }

        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task<int?> PostInventoryAdjustmentAsync(int auditId, decimal netImpact, string reference, string? userId)
    {
        if (netImpact == 0) return null;

        var mappings = await GetSafeSystemMappingsAsync();
        
        // STRICT MAPPING: Fail if keys are missing in AccountSystemMappings table
        if (!mappings.TryGetValue(MappingKeys.Inventory.ToLower(), out var iId) || iId == null)
            throw new InvalidOperationException($"فشل الترحيل المحاسبي: حساب المخزون ({MappingKeys.Inventory}) غير مربوط في الإعدادات.");

        if (!mappings.TryGetValue(MappingKeys.InventoryVariance.ToLower(), out var vId) || vId == null)
            throw new InvalidOperationException($"فشل الترحيل المحاسبي: حساب فروقات الجرد ({MappingKeys.InventoryVariance}) غير مربوط في الإعدادات.");

        var inventoryId = iId.Value;
        var varianceId  = vId.Value;

        var isIncrease = netImpact > 0;
        var absVal = Math.Abs(netImpact);

        var jePrefix = "JE-ADJ";
        var entryNo = await _seq.NextAsync(jePrefix, async (db, pattern) =>
        {
            var max = await db.JournalEntries.Where(e => EF.Functions.Like(e.EntryNumber, pattern)).Select(e => e.EntryNumber).ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0).DefaultIfEmpty(0).Max();
        });

        var entry = new JournalEntry
        {
            EntryNumber = entryNo,
            EntryDate   = TimeHelper.GetEgyptTime(),
            Type        = JournalEntryType.Manual,
            Status      = JournalEntryStatus.Posted,
            Reference   = reference,
            Description = $"تسوية جرد آلي رقم {auditId}",
            CreatedByUserId = userId,
            CreatedAt   = TimeHelper.GetEgyptTime()
        };

        // 1. Inventory Account
        entry.Lines.Add(new JournalLine { AccountId = inventoryId, Debit = isIncrease ? absVal : 0, Credit = isIncrease ? 0 : absVal, Description = $"تسوية مخزون - جرد #{auditId}", CreatedAt = TimeHelper.GetEgyptTime() });
        
        // 2. Variance Account
        entry.Lines.Add(new JournalLine { AccountId = varianceId, Debit = isIncrease ? 0 : absVal, Credit = isIncrease ? absVal : 0, Description = $"فروقات جرد مخزون #{auditId}", CreatedAt = TimeHelper.GetEgyptTime() });

        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry.Id;
    }

    public async Task<(int Id, bool IsActive, string? ErrorNote)> GetAccountIdAsync(string input)
    {
        var cleanInput = input.Trim().ToLower();
        if (cleanInput.StartsWith("id:"))
        {
            if (int.TryParse(cleanInput.Substring(3), out var exactId) && exactId > 0)
            {
                var acctById = await _db.Accounts.Where(a => a.Id == exactId).Select(a => new { a.Id, a.IsActive }).FirstOrDefaultAsync();
                if (acctById != null) return (acctById.Id, acctById.IsActive, !acctById.IsActive ? $"الحساب غير نشط" : null);
            }
            throw new InvalidOperationException($"فشل العملية: الحساب المعرف بـ ID ( {input} ) غير موجود حالياً.");
        }

        var acctByCodeList = await _db.Accounts.Where(a => EF.Functions.Like(a.Code, $"%{cleanInput}%")).Select(a => new { a.Id, a.Code, a.IsActive }).ToListAsync();
        var exactAcct = acctByCodeList.FirstOrDefault(a => a.Code?.Trim().ToLower() == cleanInput);
        if (exactAcct != null)
        {
            if (!exactAcct.IsActive)
                throw new InvalidOperationException($"الحساب ( {input} ) غير نشط حالياً. يرجى تفعيله من دليل الحسابات.");
            return (exactAcct.Id, true, null);
        }

        throw new InvalidOperationException($"فشل العملية: الحساب المطلوب ( {input} ) غير موجود في النظام. يرجى التأكد من دليل الحسابات أو صفحة الربط المالي.");
    }

    public async Task<int> GetRequiredMappedAccountAsync(string key, Dictionary<string, int?>? map = null)
    {
        if (map == null) map = await GetSafeSystemMappingsAsync();
        if (map.TryGetValue(key.ToLower(), out var id) && id.HasValue)
            return id.Value;
            
        throw new InvalidOperationException($"فشل العملية: لم يتم تحديد حساب ( {key} ) في صفحة الربط المالي.");
    }

    public async Task<string> GetMappedCashAccountAsync(PaymentMethod method, OrderSource source, Dictionary<string, int?>? map = null)
    {
        if (map == null) map = await GetSafeSystemMappingsAsync();

        string? key = (method, source) switch
        {
            (PaymentMethod.Vodafone, OrderSource.POS)     => MappingKeys.PosVodafone,
            (PaymentMethod.Vodafone, OrderSource.Website) => MappingKeys.WebVodafone,
            (PaymentMethod.InstaPay, OrderSource.POS)     => MappingKeys.PosInstaPay,
            (PaymentMethod.InstaPay, OrderSource.Website) => MappingKeys.WebInstaPay,
            (PaymentMethod.CreditCard, OrderSource.POS)     => MappingKeys.PosBank,
            (PaymentMethod.CreditCard, OrderSource.Website) => MappingKeys.WebBank,
            (PaymentMethod.Bank, OrderSource.POS)           => MappingKeys.PosBank,
            (PaymentMethod.Bank, OrderSource.Website)       => MappingKeys.WebBank,
            (PaymentMethod.Cash, OrderSource.POS)           => MappingKeys.PosCash,
            (PaymentMethod.Cash, OrderSource.Website)       => MappingKeys.WebCash,
            _ => null
        };

        if (key != null)
        {
            var accountId = await GetRequiredMappedAccountAsync(key, map);
            return $"ID:{accountId}";
        }

        throw new InvalidOperationException($"فشل العملية: لم يتم تحديد حساب لوسيلة الدفع ({GetMethodLabel(method)}) للمصدر ({source}) في صفحة الربط المالي.");
    }

    public string GetMethodLabel(PaymentMethod method) => method switch {
        PaymentMethod.Cash       => "نقدي",
        PaymentMethod.Bank       => "حوالة بنكية",
        PaymentMethod.CreditCard => "فيزا / شبكة",
        PaymentMethod.Vodafone   => "فودافون كاش",
        PaymentMethod.InstaPay   => "انستاباي",
        PaymentMethod.Credit     => "آجل",
        _                        => method.ToString()
    };

    public List<(PaymentMethod method, decimal amount)> ParseMixedPayments(string? note)
    {
        var splits = new List<(PaymentMethod method, decimal amount)>();
        if (string.IsNullOrWhiteSpace(note) || !note.Trim().StartsWith("{")) return splits;

        try
        {
            using var doc = JsonDocument.Parse(note);
            var root = doc.RootElement;
            JsonElement mixedProps;

            if (root.TryGetProperty("mixed", out mixedProps)) { }
            else if (root.TryGetProperty("amounts", out mixedProps)) { }
            else { mixedProps = root; }

            foreach (var prop in mixedProps.EnumerateObject())
            {
                var pm = prop.Name.ToLower();
                if (new[] { "credit", "remaining", "deferred", "debt", "change", "date" }.Contains(pm)) continue;

                var m = pm switch {
                    "cash" => PaymentMethod.Cash,
                    "bank" => PaymentMethod.Bank,
                    "visa" or "creditcard" or "fawry" => PaymentMethod.CreditCard,
                    "vodafone" => PaymentMethod.Vodafone,
                    "instapay" => PaymentMethod.InstaPay,
                    _ => (PaymentMethod?)null
                };

                decimal val = 0;
                bool parsed = prop.Value.ValueKind == JsonValueKind.Number ? (val = prop.Value.GetDecimal(), true).Item2 : decimal.TryParse(prop.Value.GetString(), out val);
                if (m.HasValue && parsed && val > 0) splits.Add((m.Value, val));
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Error parsing payment JSON"); }
        return splits;
    }

    public async Task SyncAllEntityIdsAsync()
    {
        // 1. Fix Purchase Invoices
        var purchaseEntries = await _db.JournalEntries
            .Where(e => e.Type == JournalEntryType.PurchaseInvoice && e.Reference != null)
            .ToListAsync();

        foreach (var entry in purchaseEntries)
        {
            var inv = await _db.PurchaseInvoices
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.InvoiceNumber == entry.Reference);
            
            if (inv != null)
            {
                var lines = await _db.JournalLines.Where(l => l.JournalEntryId == entry.Id).ToListAsync();
                bool changed = false;
                foreach (var l in lines)
                {
                    if (l.SupplierId == null)
                    {
                        l.SupplierId = inv.SupplierId;
                        changed = true;
                    }
                }
                if (changed) await _db.SaveChangesAsync();
            }
        }

        // 2. Fix Sales Orders
        var salesEntries = await _db.JournalEntries
            .Where(e => (e.Type == JournalEntryType.SalesInvoice || e.Type == JournalEntryType.ReceiptVoucher) && e.Reference != null)
            .ToListAsync();

        foreach (var entry in salesEntries)
        {
            var order = await _db.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrderNumber == entry.Reference);
            
                if (order != null)
                {
                    int? employeeId = null;
                    if (!string.IsNullOrEmpty(order.SalesPersonId))
                    {
                        // 1. Direct Employee ID (numeric)
                        if (int.TryParse(order.SalesPersonId, out int parsedId))
                        {
                            employeeId = parsedId;
                        }
                        else 
                        {
                            // 2. Direct AppUser Link
                            employeeId = await _db.Employees
                                .Where(e => e.AppUserId == order.SalesPersonId)
                                .Select(e => (int?)e.Id)
                                .FirstOrDefaultAsync();

                            // 3. Fallback by Email
                            if (employeeId == null)
                            {
                                var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == order.SalesPersonId);
                                if (user != null && !string.IsNullOrEmpty(user.Email))
                                {
                                    employeeId = await _db.Employees
                                        .Where(e => e.Email == user.Email)
                                        .Select(e => (int?)e.Id)
                                        .FirstOrDefaultAsync();
                                }
                            }
                        }
                    }

                    var lines = await _db.JournalLines.Where(l => l.JournalEntryId == entry.Id).ToListAsync();
                    bool changed = false;
                    foreach (var l in lines)
                    {
                        bool lineChanged = false;
                        if (l.CustomerId == null)
                        {
                            l.CustomerId = order.CustomerId;
                            lineChanged = true;
                        }
                        if (employeeId.HasValue && l.EmployeeId == null)
                        {
                            l.EmployeeId = employeeId;
                            lineChanged = true;
                        }
                        if (lineChanged) changed = true;
                    }
                    if (changed) await _db.SaveChangesAsync();
                }
        }
    }

    public async Task ConsolidateSubAccountsToControlAsync()
    {
        var custControl = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1103");
        var suppControl = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "2101");

        if (custControl == null || suppControl == null) return;

        // 1. Update Customers & Suppliers pointers
        var customers = await _db.Customers.ToListAsync();
        foreach (var c in customers) c.MainAccountId = custControl.Id;

        var suppliers = await _db.Suppliers.ToListAsync();
        foreach (var s in suppliers) s.MainAccountId = suppControl.Id;

        await _db.SaveChangesAsync();

        // 2. Move Journal Lines from sub-accounts to control accounts
        var subAccountsData = await _db.Accounts
            .Where(a => a.Code != null && 
                       (a.Code.StartsWith("1103") || a.Code.StartsWith("2101")) && 
                       a.Code != "1103" && a.Code != "2101")
            .Select(a => new { a.Id, a.Code })
            .ToListAsync();
        
        var subIds = subAccountsData.Select(x => x.Id).ToList();
        
        var linesToMove = await _db.JournalLines
            .Where(l => subIds.Contains(l.AccountId))
            .ToListAsync();

        foreach (var l in linesToMove)
        {
            var code = subAccountsData.First(x => x.Id == l.AccountId).Code;
            if (code != null)
            {
                if (code.StartsWith("1103")) l.AccountId = custControl.Id;
                else if (code.StartsWith("2101")) l.AccountId = suppControl.Id;
            }
        }

        await _db.SaveChangesAsync();
        
        // 3. Deactivate old sub-accounts to keep COA clean
        var accountsToDeactivate = await _db.Accounts.Where(a => subIds.Contains(a.Id)).ToListAsync();
        foreach (var a in accountsToDeactivate)
        {
            a.IsActive = false;
        }

        // تحويل الحسابات الرئيسية إلى حسابات نهائية (Leaf) للسماح بالترحيل المباشر
        custControl.IsLeaf = true;
        custControl.AllowPosting = true;
        suppControl.IsLeaf = true;
        suppControl.AllowPosting = true;

        await _db.SaveChangesAsync();

        // 4. Employees Consolidation
        var mappings = await GetSafeSystemMappingsAsync();
        if (mappings.TryGetValue(MappingKeys.SalariesPayable.ToLower(), out var empControlId) && empControlId.HasValue)
        {
            var employees = await _db.Employees.ToListAsync();
            foreach (var e in employees) e.AccountId = empControlId.Value;
            
            // إضافة أكواد الموظفين للدمج (2201، 1201)
            var empSubAccounts = await _db.Accounts
                .Where(a => a.Code != null && 
                           (a.Code.StartsWith("2201") || a.Code.StartsWith("1201")) &&
                           a.Code != "2201" && a.Code != "1201")
                .ToListAsync();
            
            var empSubIds = empSubAccounts.Select(x => x.Id).ToList();
            var empLines = await _db.JournalLines.Where(l => empSubIds.Contains(l.AccountId)).ToListAsync();
            
            foreach (var l in empLines)
            {
                var code = empSubAccounts.First(x => x.Id == l.AccountId).Code;
                if (code != null)
                {
                    if (code.StartsWith("2201")) l.AccountId = empControlId.Value;
                    else if (code.StartsWith("1201") && mappings.TryGetValue(MappingKeys.EmployeeAdvances.ToLower(), out var advId) && advId.HasValue)
                        l.AccountId = advId.Value;
                }
            }

            foreach (var a in empSubAccounts) a.IsActive = false;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// حذف الحسابات غير النشطة (التي تم دمجها بالفعل) بشرط عدم وجود حركات مالية عليها
    /// </summary>
    public async Task<int> PurgeInactiveSubAccountsAsync()
    {
        // 1. استحضار كل الحسابات غير النشطة
        var inactiveAccounts = await _db.Accounts
            .Where(a => !a.IsActive)
            .ToListAsync();

        if (!inactiveAccounts.Any()) return 0;

        var deletedCount = 0;
        foreach (var acc in inactiveAccounts)
        {
            // التأكد أن الحساب لا يحتوي على أي حركات مالية
            bool hasLines = await _db.JournalLines.AnyAsync(l => l.AccountId == acc.Id);
            if (hasLines) continue;

            // التأكد أن الحساب غير مربوط بعميل أو مورد أو موظف كحساب رئيسي
            bool isReferenced = await _db.Customers.AnyAsync(c => c.MainAccountId == acc.Id)
                             || await _db.Suppliers.AnyAsync(s => s.MainAccountId == acc.Id)
                             || await _db.Employees.AnyAsync(e => e.AccountId == acc.Id);
            if (isReferenced) continue;

            _db.Accounts.Remove(acc);
            deletedCount++;
        }

        await _db.SaveChangesAsync();
        return deletedCount;
    }

    public async Task SyncEntityBalancesAsync()
    {
        // 1. Sync Orders PaidAmount (The root cause for dashboard debt discrepancies)
        var orders = await _db.Orders.Where(o => o.Status != OrderStatus.Cancelled).ToListAsync();
        foreach (var o in orders)
        {
            // 💡 REFINED LOGIC: PaidAmount = TotalAmount - CurrentReceivableBalance
            // CurrentReceivableBalance is (Sum of Debits to 1103 - Sum of Credits to 1103) for this Order
            var ledgerBalance = await _db.JournalLines
                .Where(l => l.OrderId == o.Id && l.Account.Code != null && (l.Account.Code.StartsWith("1103") || l.Account.Code.StartsWith("1201")))
                .SumAsync(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0;

            // If ledger balance is 100, then (1500 - 100) = 1400 paid. Perfect!
            o.PaidAmount = Math.Max(0, o.TotalAmount - ledgerBalance);
        }
        await _db.SaveChangesAsync();

        // 2. Sync Purchase Invoices
        var pInvoices = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Where(i => i.Status != PurchaseInvoiceStatus.Cancelled && i.Status != PurchaseInvoiceStatus.Draft)
            .ToListAsync();
        foreach (var inv in pInvoices)
        {
            // Sum all DEBITS to Payables (2101) for this PurchaseInvoice
            // Check by ID or fallback to InvoiceNumber Reference
            var ledgerPaidAmount = await _db.JournalLines
                .Where(l => (l.PurchaseInvoiceId == inv.Id || (l.JournalEntry != null && l.JournalEntry.Reference == inv.InvoiceNumber)) && l.Debit > 0)
                .Where(l => l.Account.Code != null && l.Account.Code.StartsWith("2101"))
                .SumAsync(l => (decimal?)l.Debit) ?? 0;

            inv.PaidAmount = ledgerPaidAmount;

            // 💡 FIX: For Cash purchases, there's no liability in the 2101 account, so we set PaidAmount = TotalAmount to show 0 remaining in UI
            if (inv.PaymentTerms == PaymentTerms.Cash)
            {
                inv.PaidAmount = inv.TotalAmount;
                inv.Status = PurchaseInvoiceStatus.Paid;
            }
            else if (inv.PaidAmount >= inv.TotalAmount && inv.TotalAmount > 0)
            {
                inv.Status = PurchaseInvoiceStatus.Paid;
            }
            else if (inv.PaidAmount > 0)
            {
                inv.Status = PurchaseInvoiceStatus.PartPaid;
            }
            else if (inv.Status == PurchaseInvoiceStatus.Paid || inv.Status == PurchaseInvoiceStatus.PartPaid)
            {
                // Reset to Received if PaidAmount is 0 but it was marked paid
                inv.Status = PurchaseInvoiceStatus.Received;
            }

            // 3. Auto-Overdue & Notifications
            if (inv.Status != PurchaseInvoiceStatus.Paid && inv.DueDate.HasValue)
            {
                var today = TimeHelper.GetEgyptTime().Date;
                var due = inv.DueDate.Value.Date;

                if (due < today && inv.Status != PurchaseInvoiceStatus.Overdue)
                {
                    inv.Status = PurchaseInvoiceStatus.Overdue;
                    await _notifications.SendAsync(null, 
                        "تأخير سداد فاتورة مورد", "Supplier Payment Overdue",
                        $"الفاتورة رقم {inv.InvoiceNumber} للمورد {inv.Supplier?.Name} تجاوزت موعد الاستحقاق ({due:yyyy-MM-dd})",
                        $"Invoice #{inv.InvoiceNumber} for {inv.Supplier?.Name} is overdue since {due:yyyy-MM-dd}",
                        "Alert", null);
                }
                else if (due == today)
                {
                    await _notifications.SendAsync(null,
                        "موعد استحقاق دفع", "Payment Due Today",
                        $"اليوم هو موعد سداد الفاتورة رقم {inv.InvoiceNumber} للمورد {inv.Supplier?.Name}",
                        $"Today is the due date for Invoice #{inv.InvoiceNumber} from {inv.Supplier?.Name}",
                        "Alert", null);
                }
            }
        }
        await _db.SaveChangesAsync();

        // 3. Sync Suppliers
        var suppliers = await _db.Suppliers.ToListAsync();
        foreach (var s in suppliers)
        {
            // A. Volume (All non-cancelled invoices)
            var volume = await _db.PurchaseInvoices
                .Where(i => i.SupplierId == s.Id && i.Status != PurchaseInvoiceStatus.Draft && i.Status != PurchaseInvoiceStatus.Cancelled)
                .SumAsync(i => (decimal?)i.TotalAmount) ?? 0;
            
            // B. DEBT (From Ledger - Account 2101)
            // Balance = Credit (Liability) - Debit (Payment/Return)
            var debt = await _db.JournalLines
                .Where(l => l.SupplierId == s.Id && l.Account.Code != null && l.Account.Code.StartsWith("2101"))
                .SumAsync(l => (decimal?)l.Credit - (decimal?)l.Debit) ?? 0;

            s.TotalPurchases = volume;
            s.TotalPaid      = volume - debt; 
        }

        // 4. Sync Customers
        var customers = await _db.Customers.ToListAsync();
        foreach (var c in customers)
        {
            // A. Volume (All non-cancelled orders)
            var volume = await _db.Orders
                .Where(o => o.CustomerId == c.Id && o.Status != OrderStatus.Cancelled)
                .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;
            
            // B. DEBT (From Ledger - Account 1103/1201)
            // Balance = Debit (Sales) - Credit (Payments/Returns)
            var debt = await _db.JournalLines
                .Where(l => l.CustomerId == c.Id && (l.Account.Code.StartsWith("1103") || l.Account.Code.StartsWith("1201")))
                .SumAsync(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0;

            c.TotalSales = volume;
            c.TotalPaid  = volume - debt; 
        }

        // 5. Sync Employees
        var empControlAccId = await _db.AccountSystemMappings
            .Where(m => m.Key == MappingKeys.SalariesPayable)
            .Select(m => m.AccountId)
            .FirstOrDefaultAsync();

        if (empControlAccId.HasValue)
        {
            var activeEmployees = await _db.Employees.ToListAsync();
            foreach (var e in activeEmployees)
            {
                // Balance = Credit (Salary/Bonus) - Debit (Advances/Deductions/Payments)
                // We check all lines linked to THIS employee on the Salaries Payable account
                var balance = await _db.JournalLines
                    .Where(l => l.EmployeeId == e.Id && l.AccountId == empControlAccId.Value)
                    .SumAsync(l => (decimal?)l.Credit - (decimal?)l.Debit) ?? 0;
                
                // You can add logic here if you want to store this balance in Employee model
                // For now, it's computed in statements.
            }
        }

        await _db.SaveChangesAsync();
    }
}
