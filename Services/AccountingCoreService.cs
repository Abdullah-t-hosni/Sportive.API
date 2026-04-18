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
        var mappings = await _db.AccountSystemMappings
            .AsNoTracking()
            .Include(m => m.Account)
            .ToListAsync();
            
        return mappings
            .Where(m => m.AccountId == null || (m.Account != null && m.Account.IsActive))
            .ToDictionary(m => m.Key.ToLower(), m => m.AccountId);
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

    public async Task PostEntryAsync(
        JournalEntryType type,
        string reference,
        string description,
        DateTime date,
        List<(string code, decimal debit, decimal credit, string desc)> lines,
        int? orderId    = null,
        int? customerId = null,
        int? supplierId = null,
        int? purchaseInvoiceId = null,
        OrderSource? source = null)
    {
        var totalDr = lines.Sum(l => l.debit);
        var totalCr = lines.Sum(l => l.credit);
        if (Math.Round(totalDr, 2) != Math.Round(totalCr, 2))
            throw new InvalidOperationException($"القيد غير متوازن: مدين={totalDr}, دائن={totalCr} | {reference}");

        var jePrefix = source == OrderSource.POS ? "JE-POS" : source == OrderSource.Website ? "JE-WEB" : "JE";
        var entryNo = await _seq.NextAsync(jePrefix, async (db, pattern) =>
        {
            var max = await db.JournalEntries
                .Where(e => EF.Functions.Like(e.EntryNumber, pattern))
                .Select(e => e.EntryNumber)
                .ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0).DefaultIfEmpty(0).Max();
        });

        var entry = new JournalEntry
        {
            EntryNumber = entryNo,
            EntryDate   = date,
            Type        = type,
            Status      = JournalEntryStatus.Posted,
            Reference   = reference,
            Description = description,
            OrderId     = orderId,
            PurchaseInvoiceId = purchaseInvoiceId,
            CreatedAt   = TimeHelper.GetEgyptTime(),
        };

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

            bool isReceivables = realCode.StartsWith("1103");
            bool isPayables = realCode.StartsWith("2101");
            bool isSalesOrPurchase = type == JournalEntryType.SalesInvoice || type == JournalEntryType.SalesReturn || type == JournalEntryType.PurchaseInvoice || type == JournalEntryType.PurchaseReturn;

            entry.Lines.Add(new JournalLine
            {
                AccountId   = accountId,
                Debit       = debit,
                Credit      = credit,
                Description = finalDesc,
                CustomerId  = (isReceivables || isSalesOrPurchase) ? customerId : null,
                SupplierId  = (isPayables || isSalesOrPurchase) ? supplierId : null,
                OrderId     = orderId,
                PurchaseInvoiceId = purchaseInvoiceId,
                CreatedAt   = TimeHelper.GetEgyptTime(),
            });
        }

        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync();
    }

    public async Task<(int Id, bool ExactMatch, string? ErrorNote)> GetAccountIdAsync(string input)
    {
        var cleanInput = input.Trim().ToLower();
        if (cleanInput.StartsWith("id:"))
        {
            if (int.TryParse(cleanInput.Substring(3), out var exactId) && exactId > 0)
            {
                var acctById = await _db.Accounts.Where(a => a.Id == exactId).Select(a => new { a.Id, a.IsActive }).FirstOrDefaultAsync();
                if (acctById != null) return (acctById.Id, acctById.IsActive, !acctById.IsActive ? $"الحساب غير نشط" : null);
            }
        }
        else
        {
            var acctByCodeList = await _db.Accounts.Where(a => EF.Functions.Like(a.Code, $"%{cleanInput}%")).Select(a => new { a.Id, a.Code, a.IsActive }).ToListAsync();
            var exactAcct = acctByCodeList.FirstOrDefault(a => a.Code?.Trim().ToLower() == cleanInput);
            if (exactAcct != null) return (exactAcct.Id, exactAcct.IsActive, !exactAcct.IsActive ? $"الحساب {input} غير نشط" : null);
        }

        var fallbackCash = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == CASH_ACCOUNTS && a.IsActive);
        if (fallbackCash != null) return (fallbackCash.Id, false, $"الحساب {input} غير موجود");
        
        var firstActive = await _db.Accounts.FirstOrDefaultAsync(a => a.IsActive);
        if (firstActive != null) return (firstActive.Id, false, $"توجيه عشوائي");
        
        throw new InvalidOperationException("لا توجد حسابات نشطة!");
    }

    public async Task<string> GetMappedCashAccountAsync(PaymentMethod method, OrderSource source, Dictionary<string, int?>? map = null)
    {
        var constantCode = (method, source) switch
        {
            (PaymentMethod.Vodafone, OrderSource.POS)     => VODAFONE,
            (PaymentMethod.Vodafone, OrderSource.Website) => VODAFONE_WEB,
            (PaymentMethod.InstaPay, OrderSource.POS)     => INSTAPAY,
            (PaymentMethod.InstaPay, OrderSource.Website) => INSTAPAY_WEB,
            (PaymentMethod.Bank, _)                       => BANK,
            (PaymentMethod.CreditCard, _)                 => BANK,
            (PaymentMethod.Cash, OrderSource.Website)     => CASH_WEBSITE,
            (PaymentMethod.Cash, OrderSource.POS)         => CASH_CASHIER,
            _ => null
        };

        if (constantCode != null) return constantCode;

        if (map == null) map = await GetSafeSystemMappingsAsync();

        string? key = (method, source) switch
        {
            (PaymentMethod.Vodafone, OrderSource.POS)     => "posVodafoneAccountID",
            (PaymentMethod.Vodafone, OrderSource.Website) => "webVodafoneAccountID",
            (PaymentMethod.InstaPay, OrderSource.POS)     => "posInstapayAccountID",
            (PaymentMethod.InstaPay, OrderSource.Website) => "webInstapayAccountID",
            (PaymentMethod.CreditCard, _)                 => "posBankAccountID",
            (PaymentMethod.Bank, _)                       => "posBankAccountID",
            (PaymentMethod.Cash, OrderSource.POS)         => "posCashAccountID",
            (PaymentMethod.Cash, OrderSource.Website)     => "webCashAccountID",
            _ => null
        };

        if (key != null && map.TryGetValue(key.ToLower(), out var mappedId) && mappedId.HasValue)
            return $"ID:{mappedId.Value}";

        return source == OrderSource.POS ? CASH_CASHIER : CASH_WEBSITE;
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
                    var acct = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == l.AccountId);
                    if (acct?.Code != null && acct.Code.StartsWith("2101") && l.SupplierId == null)
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
                var lines = await _db.JournalLines.Where(l => l.JournalEntryId == entry.Id).ToListAsync();
                bool changed = false;
                foreach (var l in lines)
                {
                    var acct = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == l.AccountId);
                    if (acct?.Code != null && acct.Code.StartsWith("1103") && l.CustomerId == null)
                    {
                        l.CustomerId = order.CustomerId;
                        changed = true;
                    }
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
            .Where(a => a.Code != null && a.Code.Contains("-") && (a.Code.StartsWith("1103") || a.Code.StartsWith("2101")))
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
        await _db.SaveChangesAsync();
    }

    public async Task SyncEntityBalancesAsync()
    {
        // 1. Sync Orders PaidAmount (The root cause for dashboard debt discrepancies)
        var orders = await _db.Orders.Where(o => o.Status != OrderStatus.Cancelled).ToListAsync();
        foreach (var o in orders)
        {
            // Sum all Credits to Accounts starting with 1103 (Receivables) for this Order
            var ledgerPaidAmount = await _db.JournalLines
                .Where(l => l.OrderId == o.Id && l.Credit > 0)
                .Where(l => l.Account.Code != null && l.Account.Code.StartsWith("1103"))
                .SumAsync(l => l.Credit);

            // 💡 SOURCE OF TRUTH: The ledger (Journal Entries) is the only source for syncing balances.
            // If the ledger is wrong, it must be fixed via Journal Entries, not auto-adjusted here.
            o.PaidAmount = ledgerPaidAmount;
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

        await _db.SaveChangesAsync();
    }
}
