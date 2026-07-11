using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using System.Text.Json;
using Sportive.API.DTOs;
using System.Security.Claims;
using Sportive.API.Interfaces;
using MK = Sportive.API.Utils.MappingKeys;

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
    private readonly ITranslator _t;

    // كودات الحسابات الافتتاحية (Fallback)
    public const string CASH_CASHIER  = "110101";
    public const string CASH_WEBSITE  = "110102";
    public const string CASH_ACCOUNTS = "110103";
    public const string BANK          = "110202";
    public const string VODAFONE      = "110701";
    public const string INSTAPAY      = "110703";
    public const string VODAFONE_WEB  = "110702";
    public const string INSTAPAY_WEB  = "110704";
    public const string RECEIVABLES   = "1107";
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

    public AccountingCoreService(AppDbContext db, SequenceService seq, ILogger<AccountingCoreService> logger, INotificationService notifications, ITranslator t)
    {
        _db = db;
        _seq = seq;
        _logger = logger;
        _notifications = notifications;
        _t = t;
    }

    public async Task CheckDateLockAsync(DateTime date, ClaimsPrincipal? user)
    {
        if (user != null && (user.IsInRole("Admin") || user.IsInRole("SuperAdmin"))) return; // Admin bypass

        var settings = await _db.StoreInfo.AsNoTracking().FirstOrDefaultAsync();
        if (settings?.AccountingLockDate != null && date.Date <= settings.AccountingLockDate.Value.Date)
        {
            throw new InvalidOperationException(_t.Get("Accounting.LockedPeriodError", settings.AccountingLockDate.Value.ToString("yyyy-MM-dd")));
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
        int? employeeId = null,
        DateTime? createdAt = null,
        int? branchId = null)
    {
        // 🛡️ SMART SYNC: If entry exists, update it instead of returning early
        JournalEntry? existing = null;
        if (!string.IsNullOrEmpty(reference))
        {
            var cleanRef = reference.Trim().ToLower();
            _logger.LogInformation("[Accounting] Searching for existing entries Type={Type}, Ref={Ref}", type, cleanRef);
            
            var matches = await _db.JournalEntries
                .Include(e => e.Lines)
                .Where(e => e.Type == type && e.Reference != null && e.Reference.Trim().ToLower() == cleanRef)
                .ToListAsync();

            if (matches.Any())
            {
                existing = matches.First();
                _logger.LogInformation("[Accounting] Found existing entry ID={Id}, Number={Num} to update.", existing.Id, existing.EntryNumber);
                
                if (matches.Count > 1)
                {
                    _logger.LogWarning("[Accounting] Found {Count} duplicates for Ref={Ref}. Removing extras.", matches.Count, cleanRef);
                    var extras = matches.Skip(1).ToList();
                    foreach(var ex in extras)
                    {
                        _db.JournalLines.RemoveRange(ex.Lines);
                        _db.JournalEntries.Remove(ex);
                    }
                }
            }
            else
            {
                _logger.LogWarning("[Accounting] No existing entry found for Ref={Ref}. A new one will be created.", cleanRef);
            }
        }

        var totalDr = lines.Sum(l => l.debit);
        var totalCr = lines.Sum(l => l.credit);
        if (Math.Round(totalDr, 2) != Math.Round(totalCr, 2))
            throw new InvalidOperationException(_t.Get("Accounting.EntryUnbalanced", totalDr, totalCr, reference));

        JournalEntry entry;
        if (existing != null)
        {
            entry = existing;
            entry.Description = description;
            entry.EntryDate = date;
            if (createdAt.HasValue)
            {
                entry.CreatedAt = createdAt.Value;
            }
            entry.UpdatedAt = TimeHelper.GetEgyptTime();
            entry.OrderId = orderId;
            entry.PurchaseInvoiceId = purchaseInvoiceId;
            source ??= OrderSource.General;
            entry.CostCenter = source;

            // Clear old lines
            _db.JournalLines.RemoveRange(entry.Lines);
            entry.Lines.Clear();
        }
        else
        {
            if (source == null && orderId.HasValue)
            {
                source = await _db.Orders.Where(o => o.Id == orderId.Value).Select(o => (OrderSource?)o.Source).FirstOrDefaultAsync();
            }
            
            // Final fallback
            source ??= OrderSource.General;

            // 📝 Journal Numbering (JE-POS-xxxx, JE-WEB-xxxx, JE-GEN-xxxx)
            var jePrefix = source switch {
                OrderSource.POS     => "JE-POS",
                OrderSource.Website => "JE-WEB",
                _                   => "JE-GEN"
            };
            var entryNo = await _seq.NextAsync(jePrefix);

            entry = new JournalEntry
            {
                EntryNumber = entryNo,
                EntryDate   = date,
                Type        = type,
                Status      = JournalEntryStatus.Posted,
                Reference   = string.IsNullOrWhiteSpace(reference) ? null : reference,
                Description = description,
                OrderId     = orderId,
                PurchaseInvoiceId = purchaseInvoiceId,
                CostCenter  = source,
                CreatedAt   = createdAt ?? TimeHelper.GetEgyptTime(),
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

        int? resolvedBranchId = branchId;
        if (!resolvedBranchId.HasValue && orderId.HasValue)
        {
            resolvedBranchId = await _db.Orders.Where(o => o.Id == orderId.Value).Select(o => o.BranchId).FirstOrDefaultAsync();
        }
        if (!resolvedBranchId.HasValue && purchaseInvoiceId.HasValue)
        {
            resolvedBranchId = await _db.PurchaseInvoices.Where(i => i.Id == purchaseInvoiceId.Value).Select(i => i.BranchId).FirstOrDefaultAsync();
        }
        if (!resolvedBranchId.HasValue && employeeId.HasValue)
        {
            resolvedBranchId = await _db.Employees.Where(e => e.Id == employeeId.Value).Select(e => e.BranchId).FirstOrDefaultAsync();
        }

        // ⚡ PERF FIX: fetch mappings ONCE outside the loop (was causing N+1 DB queries per journal line)
        var mappings = await GetSafeSystemMappingsAsync();
        var hrAccountIds = new List<int>();
        if (mappings.TryGetValue(MK.SalariesPayable.ToLower(), out var h1) && h1.HasValue) hrAccountIds.Add(h1.Value);
        if (mappings.TryGetValue(MK.EmployeeAdvances.ToLower(), out var h2) && h2.HasValue) hrAccountIds.Add(h2.Value);
        if (mappings.TryGetValue(MK.EmployeeBonuses.ToLower(), out var h3) && h3.HasValue) hrAccountIds.Add(h3.Value);
        if (mappings.TryGetValue(MK.EmployeeDeductions.ToLower(), out var h4) && h4.HasValue) hrAccountIds.Add(h4.Value);

        foreach (var (code, debit, credit, desc) in lines)
        {
            if (debit == 0 && credit == 0) continue;
            var (accountId, exactMatch, errorNote) = resolvedAccounts[code];
            if (!exactMatch && errorNote != null)
                _logger.LogWarning("[Accounting] Journal line using fallback/inactive account. Code={Code}, Note={Note}", code, errorNote);
            var finalDesc = desc;
            accountIdCache.TryGetValue(accountId, out var actualAccount);
            var realCode = actualAccount?.Code ?? "";

            bool isReceivables = realCode.StartsWith("1107");
            bool isPayables = realCode.StartsWith("2101") || realCode.StartsWith("2102");

            bool isEmployeeAccount = hrAccountIds.Contains(accountId) || 
                                     realCode == "4109" || realCode.StartsWith("2103") || realCode.StartsWith("1105") ||
                                     actualAccount?.NameAr?.Contains("موظف") == true || actualAccount?.NameEn?.ToLower().Contains("employee") == true ||
                                     actualAccount?.NameAr?.Contains("سلف") == true || actualAccount?.NameAr?.Contains("أجور") == true || actualAccount?.NameAr?.Contains("رواتب") == true;

            
            bool isDiscountAccount = actualAccount?.NameAr?.Contains("خصم") == true || actualAccount?.NameEn?.ToLower().Contains("discount") == true;
            
            entry.Lines.Add(new JournalLine
            {
                AccountId   = accountId,
                Debit       = debit,
                Credit      = credit,
                Description = finalDesc,
                CustomerId  = isReceivables ? customerId : null,
                SupplierId  = isPayables ? supplierId : null,
                EmployeeId  = (isEmployeeAccount || ((type == JournalEntryType.SalesInvoice || type == JournalEntryType.SalesReturn) && isDiscountAccount)) ? employeeId : (type == JournalEntryType.Payroll ? employeeId : (isEmployeeAccount ? employeeId : null)),
                OrderId     = orderId,
                PurchaseInvoiceId = purchaseInvoiceId,
                CostCenter  = source, // 🎯 حفظ مركز التكلفة على كل سطر محاسبي
                CreatedAt   = TimeHelper.GetEgyptTime(),
                BranchId    = resolvedBranchId ?? actualAccount?.BranchId,
            });
        }

        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task<int?> PostInventoryAdjustmentAsync(int auditId, decimal netImpact, string reference, string? userId, OrderSource? costCenter = null)
    {
        if (netImpact == 0) return null;

        var mappings = await GetSafeSystemMappingsAsync();
        
        // STRICT MAPPING: Fail if keys are missing in AccountSystemMappings table
        if (!mappings.TryGetValue(MappingKeys.Inventory.ToLower(), out var iId) || iId == null)
            throw new InvalidOperationException(_t.Get("Accounting.MappingMissing", MappingKeys.Inventory));

        if (!mappings.TryGetValue(MappingKeys.InventoryVariance.ToLower(), out var vId) || vId == null)
            throw new InvalidOperationException(_t.Get("Accounting.MappingMissing", MappingKeys.InventoryVariance));

        var inventoryId = iId.Value;
        var varianceId  = vId.Value;

        var isIncrease = netImpact > 0;
        var absVal = Math.Abs(netImpact);

        var jePrefix = "JE-ADJ";
        var entryNo = await _seq.NextAsync(jePrefix);

        int? auditBranchId = await _db.InventoryAudits.Where(a => a.Id == auditId).Select(a => a.BranchId).FirstOrDefaultAsync();

        var entry = new JournalEntry
        {
            EntryNumber = entryNo,
            EntryDate   = TimeHelper.GetEgyptTime(),
            Type        = JournalEntryType.Manual,
            Status      = JournalEntryStatus.Posted,
            Reference   = reference,
            Description = _t.Get("Accounting.InventoryAdjDesc", auditId),
            CreatedByUserId = userId,
            CostCenter = costCenter,
            CreatedAt   = TimeHelper.GetEgyptTime()
        };

        // 1. Inventory Account
        entry.Lines.Add(new JournalLine { AccountId = inventoryId, Debit = isIncrease ? absVal : 0, Credit = isIncrease ? 0 : absVal, Description = _t.Get("Accounting.InventoryAdjItemDesc", auditId), CreatedAt = TimeHelper.GetEgyptTime(), CostCenter = costCenter, BranchId = auditBranchId });
        
        // 2. Variance Account
        entry.Lines.Add(new JournalLine { AccountId = varianceId, Debit = isIncrease ? 0 : absVal, Credit = isIncrease ? absVal : 0, Description = _t.Get("Accounting.InventoryVarianceDesc", auditId), CreatedAt = TimeHelper.GetEgyptTime(), CostCenter = costCenter, BranchId = auditBranchId });

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
                if (acctById != null) return (acctById.Id, acctById.IsActive, !acctById.IsActive ? _t.Get("Accounting.AccountInactive") : null);
            }
            throw new InvalidOperationException(_t.Get("Accounting.AccountNotFoundById", input));
        }

        var acctByCodeList = await _db.Accounts.Where(a => EF.Functions.Like(a.Code, $"%{cleanInput}%")).Select(a => new { a.Id, a.Code, a.IsActive }).ToListAsync();
        var exactAcct = acctByCodeList.FirstOrDefault(a => a.Code?.Trim().ToLower() == cleanInput);
        if (exactAcct != null)
        {
            if (!exactAcct.IsActive)
                throw new InvalidOperationException(_t.Get("Accounting.AccountInactiveWithCode", input));
            return (exactAcct.Id, true, null);
        }

        throw new InvalidOperationException(_t.Get("Accounting.AccountNotFoundByCode", input));
    }

    public async Task<int> GetRequiredMappedAccountAsync(string key, Dictionary<string, int?>? map = null)
    {
        if (map == null) map = await GetSafeSystemMappingsAsync();
        if (map.TryGetValue(key.ToLower(), out var id) && id.HasValue)
            return id.Value;
            
        throw new InvalidOperationException(_t.Get("Accounting.KeyNotMapped", key));
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
            (PaymentMethod.CostPrice, OrderSource.POS)      => MappingKeys.PosCash,
            (PaymentMethod.CostPrice, OrderSource.Website)  => MappingKeys.WebCash,
            _ => null
        };

        if (key != null)
        {
            var accountId = await GetRequiredMappedAccountAsync(key, map);
            return $"ID:{accountId}";
        }

        throw new InvalidOperationException(_t.Get("Accounting.MethodMappingMissing", GetMethodLabel(method), source.ToString()));
    }

    public string GetMethodLabel(PaymentMethod method) => method switch {
        PaymentMethod.Cash       => _t.Get("PaymentMethod.Cash"),
        PaymentMethod.Bank       => _t.Get("PaymentMethod.Bank"),
        PaymentMethod.CreditCard => _t.Get("PaymentMethod.CreditCard"),
        PaymentMethod.Vodafone   => _t.Get("PaymentMethod.Vodafone"),
        PaymentMethod.InstaPay   => _t.Get("PaymentMethod.InstaPay"),
        PaymentMethod.Credit     => _t.Get("PaymentMethod.Credit"),
        PaymentMethod.CostPrice  => _t.Get("PaymentMethod.CostPrice"),
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
                            var acct = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == l.AccountId);
                            var realCode = acct?.Code ?? "";
                            bool isEmployeeOrDiscount = realCode == "4109" || 
                                     acct?.NameAr?.Contains("موظف") == true || acct?.NameEn?.ToLower().Contains("employee") == true ||
                                     acct?.NameAr?.Contains("سلف") == true || acct?.NameAr?.Contains("أجور") == true || acct?.NameAr?.Contains("رواتب") == true ||
                                     acct?.NameAr?.Contains("خصم") == true || acct?.NameEn?.ToLower().Contains("discount") == true;

                            if (isEmployeeOrDiscount)
                            {
                                l.EmployeeId = employeeId;
                                lineChanged = true;
                            }
                        }
                        if (lineChanged) changed = true;
                    }
                    if (changed) await _db.SaveChangesAsync();
                }
        }
    }

    public async Task ConsolidateSubAccountsToControlAsync()
    {
        var custControl = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1107");
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
                       (a.Code.StartsWith("1107") || a.Code.StartsWith("2101")) && 
                       a.Code != "1107" && a.Code != "2101")
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
                if (code.StartsWith("1107")) l.AccountId = custControl.Id;
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
            
            // إضافة أكواد الموظفين للدمج (2103، 1105)
            var empSubAccounts = await _db.Accounts
                .Where(a => a.Code != null && 
                           (a.Code.StartsWith("2103") || a.Code.StartsWith("1105")) &&
                           a.Code != "2103" && a.Code != "1105")
                .ToListAsync();
            
            var empSubIds = empSubAccounts.Select(x => x.Id).ToList();
            var empLines = await _db.JournalLines.Where(l => empSubIds.Contains(l.AccountId)).ToListAsync();
            
            foreach (var l in empLines)
            {
                var code = empSubAccounts.First(x => x.Id == l.AccountId).Code;
                if (code != null)
                {
                    if (code.StartsWith("2103")) l.AccountId = empControlId.Value;
                    else if (code.StartsWith("1105") && mappings.TryGetValue(MappingKeys.EmployeeAdvances.ToLower(), out var advId) && advId.HasValue)
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
        _logger.LogInformation("[Sync] Starting high-performance bulk synchronization of entity balances.");

        // 1. Sync Orders PaidAmount
        // Group journal lines by OrderId and sum debit - credit in ONE query
        var orderLedgerBalances = await _db.JournalLines
            .Where(l => l.OrderId != null && l.JournalEntry.Type != JournalEntryType.SalesReturn)
            .Where(l => l.Account.Code != null && (l.Account.Code.StartsWith("1107") || l.Account.Code.StartsWith("1105")))
            .GroupBy(l => l.OrderId)
            .Select(g => new {
                OrderId = g.Key!.Value,
                Balance = g.Sum(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0
            })
            .ToDictionaryAsync(x => x.OrderId, x => x.Balance);

        // 🔧 FIX: Orders with CustomerBalance payments have TWO debits to receivables:
        //   1. The balance-consumption debit (NOT a debt — represents consuming customer's stored credit)
        //   2. The remaining outstanding debt debit (actual money still owed)
        // The ledger-based formula (TotalAmount - NetDebit) treats both as debt → PaidAmount becomes 0.
        // Fix: For such orders, compute PaidAmount directly from OrderPayments (sum of all non-Credit payments).
        // 💡 EF CORE nested enum conversion bug fix: we fetch the IDs direct from OrderPayments first to avoid the SQL translation issue.
        var customerBalanceOrderIds = await _db.OrderPayments
            .Where(p => p.Method == PaymentMethod.CustomerBalance && p.Amount > 0)
            .Select(p => p.OrderId)
            .Distinct()
            .ToListAsync();

        var customerBalancePaidAmounts = await _db.Orders
            .Where(o => o.Status != OrderStatus.Cancelled && customerBalanceOrderIds.Contains(o.Id))
            .Select(o => new {
                o.Id,
                PaidAmount = o.Payments
                    .Where(p => p.Method != PaymentMethod.Credit && p.Amount > 0)
                    .Sum(p => (decimal?)p.Amount) ?? 0
            })
            .ToDictionaryAsync(x => x.Id, x => x.PaidAmount);

        // Optimize: Load only the necessary fields using AsNoTracking to determine mismatches
        var ordersProj = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Status != OrderStatus.Cancelled)
            .Select(o => new { o.Id, o.TotalAmount, o.PaidAmount })
            .ToListAsync();

        var mismatchedOrderIds = new List<int>();
        var correctPaidAmounts = new Dictionary<int, decimal>();

        foreach (var o in ordersProj)
        {
            decimal correctPaid;
            if (customerBalancePaidAmounts.TryGetValue(o.Id, out var custPaid))
            {
                correctPaid = custPaid;
            }
            else
            {
                var ledgerBalance = orderLedgerBalances.GetValueOrDefault(o.Id, 0);
                correctPaid = Math.Max(0, o.TotalAmount - ledgerBalance);
            }

            if (Math.Abs(o.PaidAmount - correctPaid) > 0.001m)
            {
                mismatchedOrderIds.Add(o.Id);
                correctPaidAmounts[o.Id] = correctPaid;
            }
        }

        if (mismatchedOrderIds.Any())
        {
            _logger.LogInformation($"[Sync] Found {mismatchedOrderIds.Count} mismatched orders to update.");
            var ordersToUpdate = await _db.Orders
                .Where(o => mismatchedOrderIds.Contains(o.Id))
                .ToListAsync();

            foreach (var o in ordersToUpdate)
            {
                if (correctPaidAmounts.TryGetValue(o.Id, out var cp))
                {
                    o.PaidAmount = cp;
                }
            }
            await _db.SaveChangesAsync();
        }
        _logger.LogInformation("[Sync] Completed order balances sync.");

        // 2. Sync Purchase Invoices
        // Group journal lines by PurchaseInvoiceId and sum Debit (payment) in ONE query
        var invoiceLedgerPaid = await _db.JournalLines
            .Where(l => l.PurchaseInvoiceId != null && l.Debit > 0)
            .Where(l => l.Account.Code != null && l.Account.Code.StartsWith("2101"))
            .GroupBy(l => l.PurchaseInvoiceId)
            .Select(g => new {
                InvoiceId = g.Key!.Value,
                Paid = g.Sum(l => (decimal?)l.Debit) ?? 0
            })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Paid);

        // Group journal lines by Reference (fallback) in ONE query
        var refLedgerPaid = await _db.JournalLines
            .Where(l => l.JournalEntry != null && l.JournalEntry.Reference != null && l.Debit > 0)
            .Where(l => l.Account.Code != null && l.Account.Code.StartsWith("2101"))
            .GroupBy(l => l.JournalEntry.Reference)
            .Select(g => new {
                Ref = g.Key!,
                Paid = g.Sum(l => (decimal?)l.Debit) ?? 0
            })
            .ToDictionaryAsync(x => x.Ref, x => x.Paid);

        var pInvoicesProj = await _db.PurchaseInvoices
            .AsNoTracking()
            .Where(i => i.Status != PurchaseInvoiceStatus.Cancelled && i.Status != PurchaseInvoiceStatus.Draft)
            .Select(i => new {
                i.Id,
                i.InvoiceNumber,
                i.PaymentTerms,
                i.PaidAmount,
                i.TotalAmount,
                i.Status,
                i.DueDate,
                i.SupplierId,
                SupplierName = i.Supplier != null ? i.Supplier.Name : ""
            })
            .ToListAsync();

        var mismatchedInvoiceIds = new List<int>();
        var calculatedInvoiceData = new Dictionary<int, (decimal PaidAmount, PurchaseInvoiceStatus Status)>();
        var today = TimeHelper.GetEgyptTime().Date;

        // Fetch all alerts sent today in a single call to prevent N+1 queries inside the loop
        var todayAlerts = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.Type == "Alert" && n.CreatedAt >= today)
            .Select(n => n.MessageAr)
            .ToListAsync();

        foreach (var inv in pInvoicesProj)
        {
            var ledgerPaidAmount = invoiceLedgerPaid.GetValueOrDefault(inv.Id, 0);
            if (ledgerPaidAmount == 0 && inv.InvoiceNumber != null)
            {
                ledgerPaidAmount = refLedgerPaid.GetValueOrDefault(inv.InvoiceNumber, 0);
            }

            decimal newPaidAmount = ledgerPaidAmount;
            PurchaseInvoiceStatus newStatus = inv.Status;

            // 💡 FIX: For Cash purchases, there's no liability in the 2101 account, so we set PaidAmount = TotalAmount to show 0 remaining in UI
            if (inv.PaymentTerms == PaymentTerms.Cash)
            {
                newPaidAmount = inv.TotalAmount;
                newStatus = PurchaseInvoiceStatus.Paid;
            }
            else if (ledgerPaidAmount >= inv.TotalAmount && inv.TotalAmount > 0)
            {
                newStatus = PurchaseInvoiceStatus.Paid;
            }
            else if (ledgerPaidAmount > 0)
            {
                newStatus = PurchaseInvoiceStatus.PartPaid;
            }
            else if (inv.Status == PurchaseInvoiceStatus.Paid || inv.Status == PurchaseInvoiceStatus.PartPaid)
            {
                // Reset to Received if PaidAmount is 0 but it was marked paid
                newStatus = PurchaseInvoiceStatus.Received;
            }

            // 3. Auto-Overdue & Notifications
            if (newStatus != PurchaseInvoiceStatus.Paid && inv.DueDate.HasValue)
            {
                var due = inv.DueDate.Value.Date;
                if (due < today && newStatus != PurchaseInvoiceStatus.Overdue)
                {
                    newStatus = PurchaseInvoiceStatus.Overdue;
                }
            }

            bool isMismatched = Math.Abs(inv.PaidAmount - newPaidAmount) > 0.001m || inv.Status != newStatus;

            if (!isMismatched && newStatus != PurchaseInvoiceStatus.Paid && inv.DueDate.HasValue)
            {
                var due = inv.DueDate.Value.Date;
                // Only mark as mismatched/needed if the invoice is due today and hasn't been notified yet
                if (due == today)
                {
                    bool alreadyNotified = todayAlerts.Any(msg => msg != null && msg.Contains(inv.InvoiceNumber ?? ""));
                    if (!alreadyNotified)
                    {
                        isMismatched = true;
                    }
                }
            }

            if (isMismatched)
            {
                mismatchedInvoiceIds.Add(inv.Id);
                calculatedInvoiceData[inv.Id] = (newPaidAmount, newStatus);
            }
        }

        if (mismatchedInvoiceIds.Any())
        {
            _logger.LogInformation($"[Sync] Found {mismatchedInvoiceIds.Count} mismatched purchase invoices to update.");
            var invoicesToUpdate = await _db.PurchaseInvoices
                .Include(i => i.Supplier)
                .Where(i => mismatchedInvoiceIds.Contains(i.Id))
                .ToListAsync();

            foreach (var inv in invoicesToUpdate)
            {
                if (calculatedInvoiceData.TryGetValue(inv.Id, out var val))
                {
                    var oldStatus = inv.Status;
                    inv.PaidAmount = val.PaidAmount;
                    inv.Status = val.Status;

                    // Send overdue / due notification if not already sent today
                    if (inv.Status != PurchaseInvoiceStatus.Paid && inv.DueDate.HasValue)
                    {
                        var due = inv.DueDate.Value.Date;
                        bool alreadyNotifiedToday = todayAlerts.Any(msg => msg != null && msg.Contains(inv.InvoiceNumber ?? ""));

                        if (!alreadyNotifiedToday)
                        {
                            if (due < today && oldStatus != PurchaseInvoiceStatus.Overdue && inv.Status == PurchaseInvoiceStatus.Overdue)
                            {
                                await _notifications.SendAsync(null, 
                                    "تأخير سداد فاتورة مورد", "Supplier Payment Overdue",
                                    $"الفاتورة رقم {inv.InvoiceNumber} للمورد {inv.Supplier?.Name} تجاوزت موعد الاستحقاق ({due:yyyy-MM-dd})",
                                    $"Invoice #{inv.InvoiceNumber} for {inv.Supplier?.Name} is overdue since {due:yyyy-MM-dd}",
                                    "Alert", null);
                                todayAlerts.Add($"Invoice #{inv.InvoiceNumber}");
                            }
                            else if (due == today)
                            {
                                await _notifications.SendAsync(null,
                                    "موعد استحقاق دفع", "Payment Due Today",
                                    $"اليوم هو موعد سداد الفاتورة رقم {inv.InvoiceNumber} للمورد {inv.Supplier?.Name}",
                                    $"Today is the due date for Invoice #{inv.InvoiceNumber} from {inv.Supplier?.Name}",
                                    "Alert", null);
                                todayAlerts.Add($"Invoice #{inv.InvoiceNumber}");
                            }
                        }
                    }
                }
            }
            await _db.SaveChangesAsync();
        }
        _logger.LogInformation("[Sync] Completed purchase invoice balances sync.");

        // 3. Sync Suppliers
        // Group purchase invoices by SupplierId and sum TotalAmount in ONE query
        var supplierInvoicesTotal = await _db.PurchaseInvoices
            .Where(i => i.Status != PurchaseInvoiceStatus.Draft && i.Status != PurchaseInvoiceStatus.Cancelled)
            .GroupBy(i => i.SupplierId)
            .Select(g => new {
                SupplierId = g.Key,
                Total = g.Sum(i => (decimal?)i.TotalAmount) ?? 0
            })
            .ToDictionaryAsync(x => x.SupplierId, x => x.Total);

        // Group journal lines by SupplierId or AccountId and sum credit - debit in ONE query
        var supplierLedgerBalancesList = await _db.JournalLines
            .Where(l => l.SupplierId != null || (l.Account != null && l.Account.Code != null && l.Account.Code.StartsWith("2101")))
            .GroupBy(l => new { l.SupplierId, l.AccountId })
            .Select(g => new {
                SupplierId = g.Key.SupplierId,
                AccountId = g.Key.AccountId,
                Debt = g.Sum(l => (decimal?)l.Credit - (decimal?)l.Debit) ?? 0
            })
            .ToListAsync();

        var suppliersProj = await _db.Suppliers
            .AsNoTracking()
            .Select(s => new { s.Id, s.MainAccountId, s.TotalPurchases, s.TotalPaid })
            .ToListAsync();

        var supplierLedgerBalances = new Dictionary<int, decimal>();
        foreach (var s in suppliersProj)
        {
            decimal bal = supplierLedgerBalancesList.Where(x => x.SupplierId == s.Id).Sum(x => x.Debt);
            if (s.MainAccountId.HasValue)
            {
                bal += supplierLedgerBalancesList.Where(x => x.SupplierId == null && x.AccountId == s.MainAccountId.Value).Sum(x => x.Debt);
            }
            supplierLedgerBalances[s.Id] = bal;
        }

        // ✅ FIX: Calculate TotalPaid directly from ledger Debit entries on supplier accounts.
        // This ensures manual journal entries (e.g. debit supplier to pay external party)
        // are reflected in the supplier balance, not just invoice-linked payments.
        var supplierLedgerPaidList = await _db.JournalLines
            .Where(l => l.Debit > 0 && (l.SupplierId != null || (l.Account != null && l.Account.Code != null && l.Account.Code.StartsWith("2101"))))
            .GroupBy(l => new { l.SupplierId, l.AccountId })
            .Select(g => new {
                SupplierId = g.Key.SupplierId,
                AccountId = g.Key.AccountId,
                TotalDebit = g.Sum(l => (decimal?)l.Debit) ?? 0
            })
            .ToListAsync();

        var supplierLedgerPaid = new Dictionary<int, decimal>();
        foreach (var s in suppliersProj)
        {
            decimal paid = supplierLedgerPaidList.Where(x => x.SupplierId == s.Id).Sum(x => x.TotalDebit);
            if (s.MainAccountId.HasValue)
            {
                paid += supplierLedgerPaidList.Where(x => x.SupplierId == null && x.AccountId == s.MainAccountId.Value).Sum(x => x.TotalDebit);
            }
            supplierLedgerPaid[s.Id] = paid;
        }

        var mismatchedSupplierIds = new List<int>();
        var calculatedSupplierData = new Dictionary<int, (decimal TotalPurchases, decimal TotalPaid)>();

        foreach (var s in suppliersProj)
        {
            var volume = supplierInvoicesTotal.GetValueOrDefault(s.Id, 0);
            // Use direct ledger debit sum as TotalPaid — captures all payments including manual journal entries
            var expectedPaid = supplierLedgerPaid.GetValueOrDefault(s.Id, 0);

            if (Math.Abs(s.TotalPurchases - volume) > 0.001m || Math.Abs(s.TotalPaid - expectedPaid) > 0.001m)
            {
                mismatchedSupplierIds.Add(s.Id);
                calculatedSupplierData[s.Id] = (volume, expectedPaid);
            }
        }

        if (mismatchedSupplierIds.Any())
        {
            _logger.LogInformation($"[Sync] Found {mismatchedSupplierIds.Count} mismatched suppliers to update.");
            var suppliersToUpdate = await _db.Suppliers
                .Where(s => mismatchedSupplierIds.Contains(s.Id))
                .ToListAsync();

            foreach (var s in suppliersToUpdate)
            {
                if (calculatedSupplierData.TryGetValue(s.Id, out var val))
                {
                    s.TotalPurchases = val.TotalPurchases;
                    s.TotalPaid = val.TotalPaid;
                }
            }
            await _db.SaveChangesAsync();
        }
        _logger.LogInformation("[Sync] Completed supplier balances sync.");

        // 4. Sync Customers
        // Group orders by CustomerId and sum TotalAmount in ONE query
        var customerOrdersTotal = await _db.Orders
            .Where(o => o.Status != OrderStatus.Cancelled)
            .GroupBy(o => o.CustomerId)
            .Select(g => new {
                CustomerId = g.Key,
                Total = g.Sum(o => (decimal?)o.TotalAmount) ?? 0
            })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Total);

        // Group journal lines by CustomerId or AccountId and sum debit - credit in ONE query
        var customerLedgerBalancesList = await _db.JournalLines
            .Where(l => l.CustomerId != null || (l.Account != null && l.Account.Code != null && l.Account.Code.StartsWith("1104")))
            .GroupBy(l => new { l.CustomerId, l.AccountId })
            .Select(g => new {
                CustomerId = g.Key.CustomerId,
                AccountId = g.Key.AccountId,
                Debt = g.Sum(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0
            })
            .ToListAsync();

        var customersProj = await _db.Customers
            .AsNoTracking()
            .Select(c => new { c.Id, c.MainAccountId, c.TotalSales, c.TotalPaid })
            .ToListAsync();

        var customerLedgerBalances = new Dictionary<int, decimal>();
        foreach (var c in customersProj)
        {
            decimal bal = customerLedgerBalancesList.Where(x => x.CustomerId == c.Id).Sum(x => x.Debt);
            if (c.MainAccountId.HasValue)
            {
                bal += customerLedgerBalancesList.Where(x => x.CustomerId == null && x.AccountId == c.MainAccountId.Value).Sum(x => x.Debt);
            }
            customerLedgerBalances[c.Id] = bal;
        }

        var mismatchedCustomerIds = new List<int>();
        var calculatedCustomerData = new Dictionary<int, (decimal TotalSales, decimal TotalPaid)>();

        foreach (var c in customersProj)
        {
            var volume = customerOrdersTotal.GetValueOrDefault(c.Id, 0);
            var debt = customerLedgerBalances.GetValueOrDefault(c.Id, 0);
            var expectedPaid = volume - debt;

            if (Math.Abs(c.TotalSales - volume) > 0.001m || Math.Abs(c.TotalPaid - expectedPaid) > 0.001m)
            {
                mismatchedCustomerIds.Add(c.Id);
                calculatedCustomerData[c.Id] = (volume, expectedPaid);
            }
        }

        if (mismatchedCustomerIds.Any())
        {
            _logger.LogInformation($"[Sync] Found {mismatchedCustomerIds.Count} mismatched customers to update.");
            var customersToUpdate = await _db.Customers
                .Where(c => mismatchedCustomerIds.Contains(c.Id))
                .ToListAsync();

            foreach (var c in customersToUpdate)
            {
                if (calculatedCustomerData.TryGetValue(c.Id, out var val))
                {
                    c.TotalSales = val.TotalSales;
                    c.TotalPaid = val.TotalPaid;
                }
            }
            await _db.SaveChangesAsync();
        }

        _logger.LogInformation("[Sync] Completed customer balances sync. All sync completed successfully.");
    }
}
