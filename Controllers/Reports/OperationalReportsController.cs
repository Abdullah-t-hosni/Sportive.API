using Sportive.API.Interfaces;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using Microsoft.Extensions.Caching.Memory;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.ReportsMain + "," + ModuleKeys.Dashboard + "," + ModuleKeys.Pos + "," + ModuleKeys.InventoryGroup)]
public class OperationalReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<OperationalReportsController> _logger;
    private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;
    private readonly ITranslator _t;
    public OperationalReportsController(AppDbContext db, ILogger<OperationalReportsController> logger, Microsoft.Extensions.Caching.Memory.IMemoryCache cache, ITranslator t)
    {
        _db = db;
        _logger = logger;
        _cache = cache;
        _t = t;
    }
    [HttpPost("reset-supplier-balances")]
    public async Task<IActionResult> ResetSupplierBalances()
    {
        try 
        {
            var suppliers = await _db.Suppliers.ToListAsync();
            foreach(var s in suppliers) s.OpeningBalance = 0;
            await _db.SaveChangesAsync();
            return Ok(new { message = _t.Get("Reports.ResetSuccess") });
        }
        catch(Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }


    [HttpGet("dictionaries")]
    public async Task<IActionResult> GetDictionaries()
    {
        var colors = await _db.ProductVariants
            .Where(v => v.ColorAr != null || v.Color != null)
            .Select(v => v.ColorAr ?? v.Color)
            .Distinct()
            .ToListAsync();

        var sizes = await _db.ProductVariants
            .Where(v => v.Size != null)
            .Select(v => v.Size)
            .Distinct()
            .ToListAsync();

        var products = await _db.Products
            .OrderBy(p => p.NameAr)
            .Select(p => new { 
                p.Id, 
                p.NameAr, 
                p.SKU,
                Variants = p.Variants.Select(v => new { v.Color, v.ColorAr, v.Size }).ToList()
            })
            .ToListAsync();

        return Ok(new { colors, sizes, products });
    }

    // ══════════════════════════════════════════════════════
    // 1. كشف حساب عميل
    // GET /api/operationalreports/customer-statement?customerId=&fromDate=&toDate=
    // ══════════════════════════════════════════════════════
    // ══════════════════════════════════════════════════════
    [HttpGet("customer-statement")]
    [RequirePermission(ModuleKeys.ReportsMain + "," + ModuleKeys.Dashboard + "," + ModuleKeys.Pos + "," + ModuleKeys.InventoryGroup)]
    public async Task<IActionResult> CustomerStatement(
        [FromQuery] int?      customerId = null,
        [FromQuery] string?   search     = null,
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] bool      excel      = false,
        [FromQuery] bool      unpaidOnly = false,
        [FromQuery] int       page       = 1,
        [FromQuery] int       pageSize   = 50,
        [FromQuery] int?      branchId   = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var cacheKey = $"CustStatement_{customerId}_{search}_{fromDate}_{toDate}_{unpaidOnly}_{page}_{pageSize}_{branchId}";
        if (!excel && _cache.TryGetValue(cacheKey, out var cachedData))
            return Ok(cachedData);
        // 🕒 BUSINESS DAY OFFSET: The day ends at 2 AM.
        var from = (fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1)).Date.AddHours(TimeHelper.GetBusinessDayEndHour());
        var to   = (toDate ?? TimeHelper.GetEgyptTime()).Date.AddDays(1).AddHours(TimeHelper.GetBusinessDayEndHour()).AddTicks(-1);

        if (customerId == null && !string.IsNullOrEmpty(search))
        {
            var searchHash = Customer.EncryptionHelper?.ComputeSearchHash(search);
            var found = await _db.Customers
                .Where(c => c.FullName.Contains(search) || (searchHash != null && c.PhoneHash == searchHash))
                .Select(c => c.Id)
                .FirstOrDefaultAsync();
            if (found > 0) customerId = found;
        }

        if (customerId == null)
        {
            var totalCusts = await _db.Customers.CountAsync();
            var customers = await _db.Customers
                .AsNoTracking()
                .OrderBy(c => c.FullName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new { c.Id, c.FullName, c.Phone, c.Email })
                .ToListAsync();
            return Ok(new { items = customers, pagination = new { totalCount = totalCusts, pageSize, currentPage = page, totalPages = (int)Math.Ceiling(totalCusts / (double)pageSize) } });
        }

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId);
        if (customer == null) return NotFound();

        // ✅ REFACTORED TO LEDGER-BASED (Source of Truth)
        // 1. Calculate Prior Balance
        var priorQuery = _db.JournalLines
            .Where(l => l.CustomerId == customerId && l.JournalEntry.EntryDate < from && l.JournalEntry.Status == JournalEntryStatus.Posted);
        
        if (branchId.HasValue)
        {
            priorQuery = priorQuery.Where(l => l.BranchId == branchId.Value);
        }

        decimal priorBalance = await priorQuery.SumAsync(l => (decimal?)(l.Debit - l.Credit)) ?? 0;

        // 2. ديون العملاء (عمر الدين)
        var entriesQuery = _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.CustomerId == customerId && l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to && l.JournalEntry.Status == JournalEntryStatus.Posted);

        if (branchId.HasValue)
        {
            entriesQuery = entriesQuery.Where(l => l.BranchId == branchId.Value);
        }

        var entries = await entriesQuery.ToListAsync();
        entries = entries.OrderBy(l => TimeHelper.GetBusinessDate(l.JournalEntry.EntryDate))
                     .ThenBy(l => {
                         var type = l.JournalEntry.Type;
                         var reference = l.JournalEntry.Reference ?? "";
                         if (type == JournalEntryType.OpeningBalance) return 0;
                         if (type == JournalEntryType.SalesInvoice || type == JournalEntryType.PurchaseInvoice) return 10;
                         if (type == JournalEntryType.SalesReturn || type == JournalEntryType.PurchaseReturn) return 20;
                         if (type == JournalEntryType.Manual && reference.StartsWith("SHIFT-CLOSE")) return 30;
                         if (type == JournalEntryType.ReceiptVoucher || type == JournalEntryType.PaymentVoucher) return 40;
                         return 50;
                     })
                     .ThenBy(l => l.JournalEntry.EntryDate)
                     .ThenBy(l => l.JournalEntryId)
                     .ThenBy(l => l.Id)
                     .ToList();

        var lines = new List<CustomerStatementLine>();
        decimal balance = priorBalance;

        if (balance != 0)
        {
            lines.Add(new CustomerStatementLine(from.AddSeconds(-1), _t.Get("Reports.Balance"), "OPENING", _t.Get("Reports.OpeningBalance"), balance > 0 ? balance : 0, balance < 0 ? Math.Abs(balance) : 0, balance));
        }

        foreach (var l in entries)
        {
            balance += (l.Debit - l.Credit);
            var typeStr = l.JournalEntry.Type switch {
                JournalEntryType.Sales => _t.Get("Reports.Invoice"),
                JournalEntryType.ReceiptVoucher => _t.Get("Reports.ReceiptVoucher"),
                JournalEntryType.SalesReturn => _t.Get("Reports.SalesReturn"),
                _ => _t.Get("Reports.JournalEntry")
            };
            lines.Add(new CustomerStatementLine(
                l.JournalEntry.EntryDate, typeStr, l.JournalEntry.Reference ?? l.JournalEntry.EntryNumber,
                l.Description ?? l.JournalEntry.Description ?? _t.Get("Reports.AccountActivity"),
                l.Debit, l.Credit, balance));
        }

        if (unpaidOnly) {
            lines = lines.Where(l => l.Debit > 0 && l.Balance > 0).ToList();
        }

        var totalDebit  = lines.Where(l => l.Reference != "OPENING").Sum(l => l.Debit);
        var totalCredit = lines.Where(l => l.Reference != "OPENING").Sum(l => l.Credit);
        
        if (excel) return ExcelCustomerStatement(customer, lines, totalDebit, totalCredit, balance, from, to);

        var totalCount = lines.Count;
        var paginatedLines = lines.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var result = new {
            customer = new { customer.Id, customer.FullName, customer.Phone, customer.Email },
            from, to, lines = paginatedLines,
            totalDebit, totalCredit, outstanding = balance,
            hasBalance = Math.Abs(balance) > 0.01M,
            pagination = new {
                totalCount,
                pageSize,
                currentPage = page,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            }
        };

        if (!excel) _cache.Set(cacheKey, result, TimeSpan.FromMinutes(2));

        return Ok(result);
    }

    // ══════════════════════════════════════════════════════
    // 1.5 كشف حساب مورد
    // GET /api/operationalreports/supplier-statement?supplierId=&fromDate=&toDate=
    // ══════════════════════════════════════════════════════
    // ══════════════════════════════════════════════════════
    [HttpGet("supplier-statement")]
    [RequirePermission(ModuleKeys.ReportsMain + "," + ModuleKeys.Dashboard + "," + ModuleKeys.Pos + "," + ModuleKeys.InventoryGroup)]
    public async Task<IActionResult> SupplierStatement(
        [FromQuery] int?      supplierId = null,
        [FromQuery] string?   search     = null,
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] bool      excel      = false,
        [FromQuery] bool      unpaidOnly = false,
        [FromQuery] int       page       = 1,
        [FromQuery] int       pageSize   = 50,
        [FromQuery] int?      branchId   = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var cacheKey = $"SuppStatement_{supplierId}_{search}_{fromDate}_{toDate}_{unpaidOnly}_{page}_{pageSize}_{branchId}";
        if (!excel && _cache.TryGetValue(cacheKey, out var cachedData))
            return Ok(cachedData);
        // 🕒 BUSINESS DAY OFFSET: The day ends at 2 AM.
        var from = (fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1)).Date.AddHours(TimeHelper.GetBusinessDayEndHour());
        var to   = (toDate ?? TimeHelper.GetEgyptTime()).Date.AddDays(1).AddHours(TimeHelper.GetBusinessDayEndHour()).AddTicks(-1);

        if (supplierId == null && !string.IsNullOrEmpty(search))
        {
            supplierId = await _db.Suppliers
                .Where(s => s.Name.Contains(search) || s.Phone.Contains(search))
                .Select(s => s.Id)
                .FirstOrDefaultAsync();
        }

        if (supplierId == null)
        {
            var totalSupps = await _db.Suppliers.CountAsync();
            var suppliers = await _db.Suppliers
                .AsNoTracking()
                .OrderBy(s => s.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new { s.Id, s.Name, s.Phone })
                .ToListAsync();
            return Ok(new { items = suppliers, pagination = new { totalCount = totalSupps, pageSize, currentPage = page, totalPages = (int)Math.Ceiling(totalSupps / (double)pageSize) } });
        }

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId);
        if (supplier == null) return NotFound();

        // ✅ REFACTORED TO LEDGER-BASED
        var priorQuery = _db.JournalLines
            .Where(l => l.SupplierId == supplierId && l.JournalEntry.EntryDate < from && l.JournalEntry.Status == JournalEntryStatus.Posted);

        if (branchId.HasValue)
        {
            priorQuery = priorQuery.Where(l => l.BranchId == branchId.Value);
        }

        decimal priorBalance = await priorQuery.SumAsync(l => (decimal?)(l.Credit - l.Debit)) ?? 0;

        var entriesQuery = _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.SupplierId == supplierId && l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to && l.JournalEntry.Status == JournalEntryStatus.Posted);

        if (branchId.HasValue)
        {
            entriesQuery = entriesQuery.Where(l => l.BranchId == branchId.Value);
        }

        var entries = await entriesQuery.ToListAsync();
        entries = entries.OrderBy(l => TimeHelper.GetBusinessDate(l.JournalEntry.EntryDate))
                     .ThenBy(l => {
                         var type = l.JournalEntry.Type;
                         var reference = l.JournalEntry.Reference ?? "";
                         if (type == JournalEntryType.OpeningBalance) return 0;
                         if (type == JournalEntryType.SalesInvoice || type == JournalEntryType.PurchaseInvoice) return 10;
                         if (type == JournalEntryType.SalesReturn || type == JournalEntryType.PurchaseReturn) return 20;
                         if (type == JournalEntryType.Manual && reference.StartsWith("SHIFT-CLOSE")) return 30;
                         if (type == JournalEntryType.ReceiptVoucher || type == JournalEntryType.PaymentVoucher) return 40;
                         return 50;
                     })
                     .ThenBy(l => l.JournalEntry.EntryDate)
                     .ThenBy(l => l.JournalEntryId)
                     .ThenBy(l => l.Id)
                     .ToList();

        var lines = new List<CustomerStatementLine>();
        decimal balance = priorBalance;

        if (balance != 0)
            lines.Add(new CustomerStatementLine(from.AddSeconds(-1), _t.Get("Reports.Balance"), "OPENING", _t.Get("Reports.OpeningBalance"), balance > 0 ? balance : 0, balance < 0 ? Math.Abs(balance) : 0, balance));
        
        foreach (var l in entries)
        {
            balance += (l.Credit - l.Debit); // For suppliers, Credit increases balance (debt to them)
            var typeStr = l.JournalEntry.Type switch {
                JournalEntryType.Purchases => _t.Get("Reports.PurchaseInvoice"),
                JournalEntryType.PaymentVoucher => _t.Get("Reports.PaymentVoucher"),
                JournalEntryType.PurchaseReturn => _t.Get("Reports.PurchaseReturn"),
                _ => _t.Get("Reports.JournalEntry")
            };
            lines.Add(new CustomerStatementLine(
                l.JournalEntry.EntryDate, typeStr, l.JournalEntry.Reference ?? l.JournalEntry.EntryNumber,
                l.Description ?? l.JournalEntry.Description ?? _t.Get("Reports.AccountActivity"),
                l.Credit, l.Debit, balance));
        }

        if (unpaidOnly) lines = lines.Where(l => l.Credit > 0 && l.Balance > 0).ToList();

        var totalCredit = lines.Where(l => l.Reference != "OPENING").Sum(l => l.Debit); // Debit side here
        var totalDebt   = lines.Where(l => l.Reference != "OPENING").Sum(l => l.Credit);

        if (excel) return ExcelSupplierStatement(supplier, lines, totalDebt, totalCredit, from, to);

        var totalCount = lines.Count;
        var paginatedLines = lines.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var result = new { 
            supplier = new { supplier.Id, supplier.Name, supplier.Phone },
            from, to, lines = paginatedLines, 
            totalInvoiced = totalDebt, 
            totalPaid = totalCredit,
            outstanding = balance,
            pagination = new {
                totalCount,
                pageSize,
                currentPage = page,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            }
        };

        if (!excel) _cache.Set(cacheKey, result, TimeSpan.FromMinutes(2));

        return Ok(result);
    }

    // ══════════════════════════════════════════════════════
    // 2. ديون العملاء (عمر الدين)
    // GET /api/operationalreports/customer-aging
    // ══════════════════════════════════════════════════════
    // ══════════════════════════════════════════════════════
    [HttpGet("customer-aging")]
    [RequirePermission(ModuleKeys.ReportsMain + "," + ModuleKeys.Dashboard + "," + ModuleKeys.Pos + "," + ModuleKeys.InventoryGroup)]
    public async Task<IActionResult> CustomerAging(
        [FromQuery] string?   search   = null,
        [FromQuery] DateTime? asOfDate = null,
        [FromQuery] bool      excel    = false,
        [FromQuery] int?      branchId = null)
    {
        // 🕒 BUSINESS DAY OFFSET: The day ends at 2 AM.
        var asOf = (asOfDate ?? TimeHelper.GetEgyptTime()).Date.AddDays(1).AddHours(TimeHelper.GetBusinessDayEndHour()).AddTicks(-1);

        var customersQuery = _db.Customers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            var searchHash = Customer.EncryptionHelper?.ComputeSearchHash(search);
            customersQuery = customersQuery.Where(c => c.FullName.Contains(search) || (searchHash != null && c.PhoneHash == searchHash));
        }

        var customers = await customersQuery
            .Select(c => new {
                c.Id,
                c.FullName,
                c.Phone,
                Orders = c.Orders
                    .Where(o => o.Status != OrderStatus.Cancelled 
                             && o.CreatedAt <= asOf
                             && (o.PaymentMethod == PaymentMethod.Credit || o.PaymentMethod == PaymentMethod.Mixed || (o.TotalAmount - o.PaidAmount) > 0)
                             && (!branchId.HasValue || o.BranchId == branchId.Value))
                    .Select(o => new {
                        o.CreatedAt,
                        o.TotalAmount,
                        o.PaidAmount,
                        ReturnedAmount = o.Items.Sum(i => i.ReturnedQuantity * i.UnitPrice)
                    }).ToList()
            })
            .ToListAsync();

        // ✅ FIX: Use Ledger (JournalLines) to get all movements accurately
        var ledgerQuery = _db.JournalLines
            .Where(l => l.Account.Code.StartsWith("1107"))
            .Where(l => l.JournalEntry.EntryDate <= asOf && (l.JournalEntry.Status == JournalEntryStatus.Posted));

        if (branchId.HasValue)
        {
            ledgerQuery = ledgerQuery.Where(l => l.BranchId == branchId.Value);
        }

        var ledgerBalances = await ledgerQuery
            .GroupBy(l => l.CustomerId)
            .Select(g => new { CustomerId = g.Key, Balance = g.Sum(l => l.Debit - l.Credit) })
            .ToListAsync();

        var balanceMap = ledgerBalances
            .Where(x => x.CustomerId != null)
            .ToDictionary(x => x.CustomerId!.Value, x => x.Balance);

        var rows = new List<CustomerAgingRow>();

        foreach (var c in customers)
        {
            if (!balanceMap.TryGetValue(c.Id, out var balance) || balance <= 0) 
                continue;

            // فط ا&ب`عات اآجة حت0 تار`خ asOf تز`ع ع&ر اد` 
            var creditOrders = c.Orders.OrderBy(o => o.CreatedAt).ToList();

            // حساب ع&ر اد`   تز`ع ارص`د ع0 اطبات حسب أع&ار!ا (LIFO logic for payments assumption)
            decimal rem = balance;
            decimal c30 = 0, c60 = 0, c90 = 0, c90plus = 0;

            foreach (var o in creditOrders)
            {
                if (rem <= 0) break;
                var days = (asOf - o.CreatedAt).Days;
                
                // Calculate net order amount after returns
                var oNet = o.TotalAmount - o.ReturnedAmount;
                if (oNet <= 0) continue;

                var amt = Math.Min(rem, oNet);
                rem -= amt;
                
                if      (days <= 30) c30    += amt;
                else if (days <= 60) c60    += amt;
                else if (days <= 90) c90    += amt;
                else                 c90plus += amt;
            }

            // If there's still balance left after orders, put it in the oldest bucket (Opening Balance/Adjustments)
            if (rem > 0) c90plus += rem;

            rows.Add(new CustomerAgingRow(c.Id, c.FullName, c.Phone ?? "", balance, c30, c60, c90, c90plus));
        }

        rows = rows.OrderByDescending(r => r.Total).ToList();

        if (excel) return ExcelCustomerAging(rows, asOf);

        var summary = new {
            total   = rows.Sum(r => r.Total),
            current = rows.Sum(r => r.Current),
            days60  = rows.Sum(r => r.Days60),
            days90  = rows.Sum(r => r.Days90),
            over90  = rows.Sum(r => r.Over90),
            count   = rows.Count,
            // Compatibility aliases
            totalBalance  = rows.Sum(r => r.Total),
            totalCurrent  = rows.Sum(r => r.Current),
            totalOver30   = rows.Sum(r => r.Days60),
            totalOver60   = rows.Sum(r => r.Days90),
            totalOver90   = rows.Sum(r => r.Over90),
            customerCount = rows.Count
        };

        return Ok(new { asOf, rows, totals = summary, summary });
    }

    // ══════════════════════════════════════════════════════
    // 3. د`  ا&رد` 
    // GET /api/operationalreports/supplier-aging
    // ══════════════════════════════════════════════════════
    [HttpGet("supplier-aging")]
    [RequirePermission(ModuleKeys.ReportsMain + "," + ModuleKeys.Dashboard + "," + ModuleKeys.Pos + "," + ModuleKeys.InventoryGroup)]
    public async Task<IActionResult> SupplierAging(
        [FromQuery] string?   search   = null,
        [FromQuery] DateTime? asOfDate = null,
        [FromQuery] bool      excel    = false,
        [FromQuery] int?      branchId = null)
    {
        // 🕒 BUSINESS DAY OFFSET: The day ends at 2 AM.
        var asOf = (asOfDate ?? TimeHelper.GetEgyptTime()).Date.AddDays(1).AddHours(TimeHelper.GetBusinessDayEndHour()).AddTicks(-1);
        _logger.LogInformation("Generating Supplier Aging report as of {AsOf}", asOf);

        try
        {
            var suppliersQuery = _db.Suppliers.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(search))
                suppliersQuery = suppliersQuery.Where(s => s.Name.Contains(search) || s.Phone.Contains(search));

            var suppliers = await suppliersQuery
                .Select(s => new {
                    s.Id,
                    s.Name,
                    s.Phone,
                    s.CompanyName,
                    Invoices = s.Invoices
                        .Where(i => i.Status != PurchaseInvoiceStatus.Cancelled
                                 && i.InvoiceDate <= asOf
                                 && i.PaymentTerms == PaymentTerms.Credit)
                        .Select(i => new {
                            i.InvoiceDate,
                            i.TotalAmount,
                            i.ReturnedAmount
                        }).ToList()
                })
                .ToListAsync();

            // ✅ FIX: Use Ledger (JournalLines) for accurate balance
            var ledgerQuery = _db.JournalLines
                .AsNoTracking()
                .Where(l => l.SupplierId != null && l.Account.Code.StartsWith("2101"))
                .Where(l => l.JournalEntry.EntryDate <= asOf && l.JournalEntry.Status == JournalEntryStatus.Posted);

            if (branchId.HasValue)
            {
                ledgerQuery = ledgerQuery.Where(l => l.BranchId == branchId.Value);
            }

            var ledgerBalances = await ledgerQuery
                .GroupBy(l => l.SupplierId)
                .Select(g => new { SupplierId = g.Key, Balance = g.Sum(l => l.Credit - l.Debit) })
                .ToListAsync();

            var balanceMap = ledgerBalances
                .Where(x => x.SupplierId != null)
                .GroupBy(x => x.SupplierId!.Value)
                .ToDictionary(g => g.Key, g => g.First().Balance);

            var rows = new List<SupplierAgingRow>();
            foreach (var s in suppliers)
            {
                if (!balanceMap.TryGetValue(s.Id, out var balance) || balance <= 0) 
                    continue;

                // افات`ر اآجة حت0 تار`خ asOf تز`ع ع&ر اد` 
                var creditInvoices = s.Invoices.OrderBy(i => i.InvoiceDate).ToList();

                decimal b = balance;
                decimal c30 = 0, c60 = 0, c90 = 0, c90p = 0;

                // تز`ع ارص`د ع0 افات`ر بحسب ع&ر!ا
                foreach (var inv in creditInvoices)
                {
                    if (b <= 0) break;
                    var days = (asOf - inv.InvoiceDate).Days;
                    
                    var invAmt = Math.Max(0, inv.TotalAmount - inv.ReturnedAmount);
                    if (invAmt <= 0) continue;

                    var amt = Math.Min(b, invAmt);
                    b -= amt;
                    
                    if      (days <= 30) c30  += amt;
                    else if (days <= 60) c60  += amt;
                    else if (days <= 90) c90  += amt;
                    else                 c90p += amt;
                }

                // If balance remains, put in oldest bucket
                if (b > 0) c90p += b;

                rows.Add(new SupplierAgingRow(
                    s.Id, s.Name, s.Phone,
                    s.CompanyName ?? "",
                    balance, c30, c60, c90, c90p));
            }

            rows = rows.OrderByDescending(r => r.Total).ToList();

            if (excel) return ExcelSupplierAging(rows, asOf);

            var summary = new {
                total   = rows.Sum(r => r.Total),
                current = rows.Sum(r => r.Current),
                days60  = rows.Sum(r => r.Days60),
                days90  = rows.Sum(r => r.Days90),
                over90  = rows.Sum(r => r.Over90),
                count   = rows.Count,
                // Compatibility aliases
                totalBalance  = rows.Sum(r => r.Total),
                totalCurrent  = rows.Sum(r => r.Current),
                totalOver30   = rows.Sum(r => r.Days60),
                totalOver60   = rows.Sum(r => r.Days90),
                totalOver90   = rows.Sum(r => r.Over90),
                supplierCount = rows.Count
            };

            return Ok(new { asOf, rows, totals = summary, summary });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SupplierAging report");
            return StatusCode(500, new { message = "Error generating report", detail = ex.Message });
        }
    }

    // ══════════════════════════════════════════════════════
    // 4. تقرير المخزون (إجمالي + تفصيلي)
    // GET /api/operationalreports/inventory
    // ══════════════════════════════════════════════════════
    // GET /api/operationalreports/inventory
    // ══════════════════════════════════════════════════════
    [HttpGet("inventory")]
    public async Task<IActionResult> Inventory(
        [FromQuery] string? search      = null,
        [FromQuery] int?    categoryId  = null,
        [FromQuery] int?    brandId     = null,
        [FromQuery] string? color       = null,
        [FromQuery] string? size        = null,
        [FromQuery] bool    lowStock    = false,
        [FromQuery] string  stockStatus = "all", // "all", "positive", "zero"
        [FromQuery] int     page        = 1,
        [FromQuery] int     pageSize    = 50,
        [FromQuery] OrderSource? source  = null,
        [FromQuery] DateTime? toDate    = null,
        [FromQuery] int?    branchId    = null,
        [FromQuery] bool    excel       = false)
    {
        if (branchId.HasValue)
        {
            var branch = await _db.Branches.FirstOrDefaultAsync(b => b.Id == branchId.Value);
            if (branch != null && branch.LinkedWarehouseId.HasValue)
            {
                var mainBranchId = await _db.Warehouses.Where(w => w.Id == branch.LinkedWarehouseId.Value).Select(w => w.BranchId).FirstOrDefaultAsync();
                if (mainBranchId > 0) branchId = mainBranchId;
            }
        }

        pageSize = Math.Clamp(pageSize, 1, 100);

        var cacheKey = $"Inventory_{search}_{categoryId}_{brandId}_{color}_{size}_{lowStock}_{stockStatus}_{page}_{pageSize}_{source}_{toDate}_{branchId}";
        if (!excel && _cache.TryGetValue(cacheKey, out var cachedData))
            return Ok(cachedData);

        var q = _db.Products
            .Include(p => p.Category)
            .Include(p => p.Variants)
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active || p.Status == ProductStatus.OutOfStock || p.Status == ProductStatus.Discontinued || p.Status == ProductStatus.Hidden);

        // --- Filtering ---
        if (!string.IsNullOrEmpty(search))
            q = q.Where(p => p.NameAr.Contains(search) || p.SKU.Contains(search));
            
        if (categoryId.HasValue && categoryId > 0)
        {
            var categoryIds = await FilterHelper.GetCategoryFamilyIds(_db, categoryId);
            q = q.Where(p => p.CategoryId.HasValue && categoryIds.Contains(p.CategoryId.Value));
        }

        if (brandId.HasValue && brandId > 0)
        {
            var brandIds = await FilterHelper.GetBrandFamilyIds(_db, brandId);
            q = q.Where(p => p.BrandId.HasValue && brandIds.Contains(p.BrandId.Value));
        }

        if (!string.IsNullOrEmpty(color))
            q = q.Where(p => p.Variants.Any(v => v.Color == color || v.ColorAr == color));

        if (!string.IsNullOrEmpty(size))
            q = q.Where(p => p.Variants.Any(v => v.Size == size));

        if (toDate.HasValue || branchId.HasValue)
        {
            Dictionary<int, int> variantStocks;
            Dictionary<int, int> simpleProductStocks;

            if (toDate.HasValue)
            {
                var limit = toDate.Value.Date.AddDays(1).AddHours(TimeHelper.GetBusinessDayEndHour()).AddTicks(-1);
                var movementQuery = _db.InventoryMovements.Where(m => m.CreatedAt <= limit);
                if (source.HasValue)
                {
                    movementQuery = movementQuery.Where(m => m.CostCenter == source.Value);
                }
                if (branchId.HasValue)
                {
                    var branchWarehouseIds = await _db.Warehouses.Where(w => w.BranchId == branchId.Value).Select(w => w.Id).ToListAsync();
                    movementQuery = movementQuery.Where(m => m.WarehouseId.HasValue && branchWarehouseIds.Contains(m.WarehouseId.Value));
                }

                variantStocks = await movementQuery
                    .Where(m => m.ProductVariantId.HasValue)
                    .GroupBy(m => m.ProductVariantId!.Value)
                    .Select(g => new { VariantId = g.Key, Stock = g.Sum(m => m.Quantity) })
                    .ToDictionaryAsync(x => x.VariantId, x => x.Stock);

                simpleProductStocks = await movementQuery
                    .Where(m => !m.ProductVariantId.HasValue && m.ProductId.HasValue)
                    .GroupBy(m => m.ProductId!.Value)
                    .Select(g => new { ProductId = g.Key, Stock = g.Sum(m => m.Quantity) })
                    .ToDictionaryAsync(x => x.ProductId, x => x.Stock);
            }
            else
            {
                // toDate is null, but branchId.HasValue
                var branchWarehouseIds = await _db.Warehouses.Where(w => w.BranchId == branchId).Select(w => w.Id).ToListAsync();

                variantStocks = await _db.ProductWarehouseStocks
                    .Where(w => branchWarehouseIds.Contains(w.WarehouseId))
                    .GroupBy(w => w.ProductVariantId)
                    .Select(g => new { VariantId = g.Key, Stock = g.Sum(w => w.Quantity) })
                    .ToDictionaryAsync(x => x.VariantId, x => x.Stock);

                simpleProductStocks = await _db.InventoryMovements
                    .Where(m => !m.ProductVariantId.HasValue && m.ProductId.HasValue && m.WarehouseId.HasValue && branchWarehouseIds.Contains(m.WarehouseId.Value))
                    .GroupBy(m => m.ProductId!.Value)
                    .Select(g => new { ProductId = g.Key, Stock = g.Sum(m => m.Quantity) })
                    .ToDictionaryAsync(x => x.ProductId, x => x.Stock);
            }

            var productsRaw = await q.ToListAsync();
            var allRows = new List<InventoryRow>();

            foreach (var p in productsRaw)
            {
                var filteredVariants = p.Variants.Where(v => 
                    (string.IsNullOrEmpty(color) || v.Color == color || v.ColorAr == color) &&
                    (string.IsNullOrEmpty(size) || v.Size == size)
                ).ToList();

                var variantRows = filteredVariants.Select(v => {
                    var vStock = variantStocks.GetValueOrDefault(v.Id, 0);
                    return new VariantInventoryRow(
                        v.Id, v.Size ?? "", v.Color ?? "", v.ColorAr ?? "",
                        vStock,
                        p.Price + (v.PriceAdjustment ?? 0),
                        (decimal)vStock * (p.Price + (v.PriceAdjustment ?? 0))
                    );
                }).ToList();

                var totalStock = p.Variants.Any()
                    ? variantRows.Sum(v => v.StockQuantity)
                    : simpleProductStocks.GetValueOrDefault(p.Id, 0);

                if (lowStock)
                {
                    bool isLow = (totalStock <= (p.ReorderLevel > 0 ? p.ReorderLevel : 5)) ||
                                 variantRows.Any(v => v.StockQuantity <= 2);
                    if (!isLow) continue;
                }

                if (stockStatus == "positive" && totalStock <= 0)
                    continue;
                if (stockStatus == "zero" && totalStock > 0)
                    continue;

                allRows.Add(new InventoryRow(
                    p.Id, p.NameAr, p.NameEn, p.SKU,
                    p.Category?.NameAr ?? "",
                    p.Price, p.DiscountPrice,
                    p.CostPrice ?? 0,
                    totalStock,
                    (decimal)totalStock * p.Price,
                    (decimal)totalStock * (p.CostPrice ?? 0),
                    variantRows
                ));
            }

            var totalUnits = allRows.Sum(r => r.TotalStock);
            var lowStockCount = allRows.Count(r => r.TotalStock <= 5);
            var outOfStock = allRows.Count(r => r.TotalStock <= 0);
            var totalSalesVal = allRows.Sum(r => r.TotalValue);
            var totalCostVal = allRows.Sum(r => r.TotalCostValue);

            var maps = await _db.AccountSystemMappings.ToDictionaryAsync(m => m.Key, m => m.AccountId);
            var inventoryAccId = maps.GetValueOrDefault(MappingKeys.Inventory);
            decimal ledgerInventoryValue = 0;
            if (inventoryAccId != null) {
                var ledgerQ = _db.JournalLines
                    .Where(l => l.AccountId == inventoryAccId && l.JournalEntry.Status == JournalEntryStatus.Posted);
                
                if (toDate.HasValue)
                {
                    var limit = toDate.Value.Date.AddDays(1).AddHours(TimeHelper.GetBusinessDayEndHour()).AddTicks(-1);
                    ledgerQ = ledgerQ.Where(l => l.JournalEntry.EntryDate <= limit);
                }

                if (source.HasValue) ledgerQ = ledgerQ.Where(l => l.CostCenter == source.Value);

                ledgerInventoryValue = await ledgerQ.SumAsync(l => (decimal?)(l.Debit - l.Credit)) ?? 0;
            }

            var summary = new {
                totalFilteredProducts = allRows.Count,
                totalUnits            = totalUnits,
                lowStockCount         = lowStockCount,
                outOfStock            = outOfStock,
                totalSalesValue       = totalSalesVal,
                totalCostValue        = totalCostVal,
                ledgerInventoryValue  = ledgerInventoryValue,
                valuationDifference   = ledgerInventoryValue - totalCostVal,
                agingAlerts           = allRows.Count(x => x.TotalStock > 0 && x.CostPrice > 0)
            };

            var totalCount = allRows.Count;
            var paginatedRows = excel
                ? allRows
                : allRows.OrderBy(r => r.CategoryName).ThenBy(r => r.NameAr).Skip((page - 1) * pageSize).Take(pageSize).ToList();

            if (excel) return ExcelInventory(paginatedRows, summary);

            var result = new { 
                rows = paginatedRows, 
                summary,
                pagination = new {
                    totalCount,
                    pageSize,
                    currentPage = page,
                    totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                }
            };

            if (!excel) _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));

            return Ok(result);
        }

        // --- Standard (Non-date-filtered) flow ---
        if (lowStock)
            q = q.Where(p => 
                (p.TotalStock <= (p.ReorderLevel > 0 ? p.ReorderLevel : 5)) ||
                p.Variants.Any(v => v.StockQuantity <= (v.ReorderLevel > 0 ? v.ReorderLevel : 2))
            );

        if (stockStatus == "positive")
        {
            if (!string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(size))
                q = q.Where(p => p.Variants.Any(v => 
                    (string.IsNullOrEmpty(color) || v.Color == color || v.ColorAr == color) &&
                    (string.IsNullOrEmpty(size) || v.Size == size) &&
                    v.StockQuantity > 0));
            else
                q = q.Where(p => p.TotalStock > 0 || p.Variants.Any(v => v.StockQuantity > 0));
        }
        else if (stockStatus == "zero")
        {
            if (!string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(size))
                q = q.Where(p => p.Variants.Any(v => 
                    (string.IsNullOrEmpty(color) || v.Color == color || v.ColorAr == color) &&
                    (string.IsNullOrEmpty(size) || v.Size == size) &&
                    v.StockQuantity <= 0));
            else
                q = q.Where(p => (p.Variants.Any() && p.Variants.Any(v => v.StockQuantity <= 0)) || (!p.Variants.Any() && p.TotalStock <= 0));
        }

        // --- Pagination ---
        var dbTotalCount = await q.CountAsync();
        
        var productsQuery = q.OrderBy(p => p.CategoryId).ThenBy(p => p.NameAr);
        var products = excel
            ? await productsQuery.ToListAsync()
            : await productsQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var totalStats = await q.Select(p => new {
            Stock = p.Variants.Any() 
                ? p.Variants.Where(v => 
                    (string.IsNullOrEmpty(color) || v.Color == color || v.ColorAr == color) &&
                    (string.IsNullOrEmpty(size) || v.Size == size) &&
                    (stockStatus == "all" || (stockStatus == "positive" && v.StockQuantity > 0) || (stockStatus == "zero" && v.StockQuantity <= 0))
                ).Sum(v => v.StockQuantity)
                : p.TotalStock,
            p.Price,
            Cost = p.CostPrice ?? 0
        }).GroupBy(x => 1).Select(g => new {
            Units = g.Sum(x => x.Stock),
            SalesVal = g.Sum(x => (decimal)x.Stock * x.Price),
            CostVal = g.Sum(x => (decimal)x.Stock * x.Cost),
            LowStock = g.Count(x => x.Stock <= 5),
            OutOfStock = g.Count(x => x.Stock <= 0)
        }).FirstOrDefaultAsync();

        var dbTotalUnits     = totalStats?.Units ?? 0;
        var dbLowStockCount  = totalStats?.LowStock ?? 0;
        var dbOutOfStock     = totalStats?.OutOfStock ?? 0;
        var dbTotalSalesVal  = totalStats?.SalesVal ?? 0;
        var dbTotalCostVal   = totalStats?.CostVal ?? 0;

        var dbRows = products.Select(p =>
        {
            var filteredVariants = p.Variants.Where(v => 
                (string.IsNullOrEmpty(color) || v.Color == color || v.ColorAr == color) &&
                (string.IsNullOrEmpty(size) || v.Size == size) &&
                (stockStatus == "all" || (stockStatus == "positive" && v.StockQuantity > 0) || (stockStatus == "zero" && v.StockQuantity <= 0))
            ).ToList();

            var totalStock = filteredVariants.Any()
                ? filteredVariants.Sum(v => v.StockQuantity)
                : (p.Variants.Any() ? 0 : p.TotalStock);

            var variantRows = filteredVariants.Select(v => new VariantInventoryRow(
                v.Id, v.Size ?? "", v.Color ?? "", v.ColorAr ?? "",
                v.StockQuantity,
                p.Price + (v.PriceAdjustment ?? 0),
                (decimal)v.StockQuantity * (p.Price + (v.PriceAdjustment ?? 0))
            )).ToList();

            return new InventoryRow(
                p.Id, p.NameAr, p.NameEn, p.SKU,
                p.Category?.NameAr ?? "",
                p.Price, p.DiscountPrice,
                p.CostPrice ?? 0,
                totalStock,
                (decimal)totalStock * p.Price,
                (decimal)totalStock * (p.CostPrice ?? 0),
                variantRows
            );
        }).ToList();

        var dbMaps = await _db.AccountSystemMappings.ToDictionaryAsync(m => m.Key, m => m.AccountId);
        var dbInventoryAccId = dbMaps.GetValueOrDefault(MappingKeys.Inventory);
        decimal dbLedgerInventoryValue = 0;
        if (dbInventoryAccId != null) {
            var ledgerQ = _db.JournalLines
                .Where(l => l.AccountId == dbInventoryAccId && l.JournalEntry.Status == JournalEntryStatus.Posted);
            
            if (source.HasValue) ledgerQ = ledgerQ.Where(l => l.CostCenter == source.Value);

            dbLedgerInventoryValue = await ledgerQ.SumAsync(l => (decimal?)(l.Debit - l.Credit)) ?? 0;
        }

        var dbSummary = new {
            totalFilteredProducts = dbTotalCount,
            totalUnits            = dbTotalUnits,
            lowStockCount         = dbLowStockCount,
            outOfStock            = dbOutOfStock,
            totalSalesValue       = dbTotalSalesVal,
            totalCostValue        = dbTotalCostVal,
            ledgerInventoryValue  = dbLedgerInventoryValue,
            valuationDifference   = dbLedgerInventoryValue - dbTotalCostVal,
            agingAlerts           = dbRows.Count(x => x.TotalStock > 0 && x.CostPrice > 0)
        };

        if (excel) return ExcelInventory(dbRows, dbSummary);

        var dbResult = new { 
            rows = dbRows, 
            summary = dbSummary,
            pagination = new {
                totalCount = dbTotalCount,
                pageSize,
                currentPage = page,
                totalPages = (int)Math.Ceiling(dbTotalCount / (double)pageSize)
            }
        };

        if (!excel) _cache.Set(cacheKey, dbResult, TimeSpan.FromMinutes(5));
        
        return Ok(dbResult);
    }

    // ══════════════════════════════════════════════════════
    // 5. تقرير المبيعات
    // GET /api/operationalreports/sales?fromDate=&toDate=&source=
    // ══════════════════════════════════════════════════════
    [HttpGet("sales")]
    [RequirePermission(ModuleKeys.ReportsMain + "," + ModuleKeys.Dashboard + "," + ModuleKeys.Pos + "," + ModuleKeys.InventoryGroup)]
    public async Task<IActionResult> SalesReport(
        [FromQuery] DateTime?    fromDate   = null,
        [FromQuery] DateTime?    toDate     = null,
        [FromQuery] OrderSource? source     = null,
        [FromQuery] int?         categoryId = null,
        [FromQuery] int?         brandId    = null,
        [FromQuery] string?      color      = null,
        [FromQuery] string?      size       = null,
        [FromQuery] int          page       = 1,
        [FromQuery] int          pageSize   = 50,
        [FromQuery] bool         excel      = false,
        [FromQuery] int?         branchId   = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var cacheKey = $"Sales_{fromDate}_{toDate}_{source}_{categoryId}_{brandId}_{color}_{size}_{page}_{pageSize}";
        if (!excel && _cache.TryGetValue(cacheKey, out var cachedData))
            return Ok(cachedData);

        var from = (fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1)).Date.AddHours(TimeHelper.GetBusinessDayEndHour());
        var to   = (toDate ?? TimeHelper.GetEgyptTime()).Date.AddDays(1).AddHours(TimeHelper.GetBusinessDayEndHour()).AddTicks(-1);

        // ✅ LEDGER-BASED RECONCILIATION
        var maps = await _db.AccountSystemMappings.ToDictionaryAsync(m => m.Key, m => m.AccountId);
        var salesAccId = maps.GetValueOrDefault(MappingKeys.Sales);
        
        // 1. Fetch Orders joined with their POSTED Journal Entries
        var ordersQ = _db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= from && o.CreatedAt <= to)
            .Where(o => o.JournalEntries.Any(j => j.Status == JournalEntryStatus.Posted));

        if (source.HasValue)
        {
            ordersQ = ordersQ.Where(o => o.Source == source.Value);
        }

        if (branchId.HasValue)
        {
            ordersQ = ordersQ.Where(o => o.BranchId == branchId.Value);
        }

        var catIds = categoryId.HasValue && categoryId > 0 ? await FilterHelper.GetCategoryFamilyIds(_db, categoryId) : new List<int>();
        var brIds = brandId.HasValue && brandId > 0 ? await FilterHelper.GetBrandFamilyIds(_db, brandId) : new List<int>();

        if (catIds.Any())
            ordersQ = ordersQ.Where(o => o.Items.Any(i => i.Product != null && i.Product.CategoryId.HasValue && catIds.Contains(i.Product.CategoryId.Value)));

        if (brIds.Any())
            ordersQ = ordersQ.Where(o => o.Items.Any(i => i.Product != null && i.Product.BrandId.HasValue && brIds.Contains(i.Product.BrandId.Value)));

        if (!string.IsNullOrEmpty(color))
            ordersQ = ordersQ.Where(o => o.Items.Any(i => i.Color == color || (i.Color == null && i.Product != null && i.Product.Variants.Any(v => (v.Color == color || v.ColorAr == color)))));

        if (!string.IsNullOrEmpty(size))
            ordersQ = ordersQ.Where(o => o.Items.Any(i => i.Size == size || (i.Size == null && i.Product != null && i.Product.Variants.Any(v => v.Size == size))));

        var totalOrdersCount = await ordersQ.CountAsync();

        var ordersQuery = ordersQ
            .Select(o => new {
                o.Id,
                o.OrderNumber,
                o.CreatedAt,
                CustomerName = o.Customer != null ? o.Customer.FullName : null,
                CustomerPhone = o.Customer != null ? o.Customer.PhoneEncrypted : null,
                o.Source,
                o.Status,
                o.PaymentMethod,
                o.SubTotal,
                o.DiscountAmount,
                o.TemporalDiscount,
                o.TotalAmount,
                Items = o.Items
                    .Where(i => 
                        (!catIds.Any() || (i.Product != null && i.Product.CategoryId.HasValue && catIds.Contains(i.Product.CategoryId.Value))) &&
                        (!brIds.Any() || (i.Product != null && i.Product.BrandId.HasValue && brIds.Contains(i.Product.BrandId.Value))) &&
                        (string.IsNullOrEmpty(color) || i.Color == color || (i.Color == null && i.Product != null && i.Product.Variants.Any(v => (v.Color == color || v.ColorAr == color)))) &&
                        (string.IsNullOrEmpty(size) || i.Size == size || (i.Size == null && i.Product != null && i.Product.Variants.Any(v => v.Size == size)))
                    )
                    .Select(i => new {
                        ProductSKU = i.Product != null ? i.Product.SKU : "",
                        ProductNameAr = i.Product != null ? i.Product.NameAr : i.ProductNameAr,
                        i.Size,
                        i.Color,
                        i.Quantity,
                        i.UnitPrice,
                        CostPrice = i.Product != null ? (i.Product.CostPrice ?? 0) : 0,
                        i.DiscountAmount,
                        i.TotalPrice,
                        i.ProductId,
                        i.ProductVariantId
                    }).ToList(),
                PostedSales = o.JournalEntries
                    .Where(j => j.Status == JournalEntryStatus.Posted)
                    .SelectMany(j => j.Lines)
                    .Where(l => l.AccountId == salesAccId)
                    .Sum(l => l.Credit),
                Payments = o.Payments.Select(p => new { p.Method, p.Amount }).ToList()
            })
            .OrderByDescending(o => o.CreatedAt);

        var orders = excel
            ? await ordersQuery.ToListAsync()
            : await ordersQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var salesReturnAccId = maps.GetValueOrDefault(MappingKeys.SalesReturn);
        var returnsQ = _db.JournalLines
            .Where(l => l.AccountId == salesReturnAccId && l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to && l.JournalEntry.Status == JournalEntryStatus.Posted);
        
        if (source.HasValue)
        {
            returnsQ = returnsQ.Where(l => l.CostCenter == source.Value);
        }

        decimal ledgerReturns = await returnsQ.SumAsync(l => (decimal?)l.Debit) ?? 0;

        var rows = orders.Select(o => {
            var paySummary = string.Join(", ", o.Payments.Select(p => $"{p.Method}: {p.Amount:N0}"));
            var itemsTotal = o.Items.Sum(i => i.TotalPrice);

            return new SalesRow(
                o.Id, o.OrderNumber, o.CreatedAt,
                o.CustomerName ?? "Walk-in",
                (Sportive.API.Models.Customer.EncryptionHelper != null && o.CustomerPhone != null ? Sportive.API.Models.Customer.EncryptionHelper.Decrypt(o.CustomerPhone) : o.CustomerPhone) ?? "",
                o.Source.ToString(),
                o.Status.ToString(),
                o.PaymentMethod.ToString(),
                o.SubTotal, o.DiscountAmount + o.TemporalDiscount, 
                (catIds.Any() || brIds.Any() || !string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(size)) ? itemsTotal : (o.PostedSales > 0 ? o.PostedSales : o.TotalAmount),
                o.Items.Sum(i => i.Quantity),
                o.Items.Select(i => new ReportItemDto(
                    i.ProductSKU,
                    i.ProductNameAr,
                    i.Size ?? "",
                    i.Color ?? "",
                    i.Quantity,
                    i.UnitPrice,
                    i.CostPrice, 
                    i.DiscountAmount / (i.Quantity > 0 ? i.Quantity : 1),
                    i.TotalPrice,
                    i.ProductId ?? 0,
                    i.ProductVariantId ?? 0
                )).ToList(),
                paySummary
            );
        }).ToList();

        var hasFilters = catIds.Any() || brIds.Any() || !string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(size);

        var summaryData = await ordersQ.Select(o => new {
            o.TotalAmount,
            DiscountAmount = o.DiscountAmount + o.TemporalDiscount,
            ItemsTotal = o.Items
                .Where(i => 
                    (!catIds.Any() || (i.Product != null && i.Product.CategoryId.HasValue && catIds.Contains(i.Product.CategoryId.Value))) &&
                    (!brIds.Any() || (i.Product != null && i.Product.BrandId.HasValue && brIds.Contains(i.Product.BrandId.Value))) &&
                    (string.IsNullOrEmpty(color) || i.Color == color || (i.Color == null && i.Product != null && i.Product.Variants.Any(v => (v.Color == color || v.ColorAr == color)))) &&
                    (string.IsNullOrEmpty(size) || i.Size == size || (i.Size == null && i.Product != null && i.Product.Variants.Any(v => v.Size == size)))
                )
                .Sum(i => (decimal?)i.TotalPrice) ?? 0,
            ItemCount = o.Items
                .Where(i => 
                    (!catIds.Any() || (i.Product != null && i.Product.CategoryId.HasValue && catIds.Contains(i.Product.CategoryId.Value))) &&
                    (!brIds.Any() || (i.Product != null && i.Product.BrandId.HasValue && brIds.Contains(i.Product.BrandId.Value))) &&
                    (string.IsNullOrEmpty(color) || i.Color == color || (i.Color == null && i.Product != null && i.Product.Variants.Any(v => (v.Color == color || v.ColorAr == color)))) &&
                    (string.IsNullOrEmpty(size) || i.Size == size || (i.Size == null && i.Product != null && i.Product.Variants.Any(v => v.Size == size)))
                )
                .Sum(i => (int?)i.Quantity) ?? 0,
            PostedSales = o.JournalEntries
                .Where(j => j.Status == JournalEntryStatus.Posted)
                .SelectMany(j => j.Lines)
                .Where(l => l.AccountId == salesAccId)
                .Sum(l => (decimal?)l.Credit) ?? 0,
            o.Source
        }).ToListAsync();

        var totalGrossRevenue = summaryData.Sum(o => hasFilters ? o.ItemsTotal : (o.PostedSales > 0 ? o.PostedSales : o.TotalAmount));
        var totalDiscountRaw = summaryData.Sum(o => o.DiscountAmount);
        var totalUnits = summaryData.Sum(o => o.ItemCount);

        // ✅ Subtract discount returns (same logic as Dashboard & Daily Report)
        // When a sales return reverses a discount, it credits the Sales Discount account.
        // We must deduct those credits to get the net applied discount.
        var salesDiscountAccId = maps.GetValueOrDefault("salesDiscountAccountID");
        decimal periodDiscountReturned = 0;
        if (salesDiscountAccId != null)
        {
            var discountReturnsQ = _db.JournalEntries
                .Where(e => e.Type == JournalEntryType.SalesReturn && e.EntryDate >= from && e.EntryDate <= to && e.Status == JournalEntryStatus.Posted);
            if (source.HasValue)
                discountReturnsQ = discountReturnsQ.Where(e => e.CostCenter == source.Value);
            if (branchId.HasValue)
                discountReturnsQ = discountReturnsQ.Where(j => (j.Order != null && j.Order.BranchId == branchId.Value) || j.Lines.Any(l => l.BranchId == branchId.Value));
            periodDiscountReturned = await discountReturnsQ
                .SelectMany(e => e.Lines)
                .Where(l => l.Credit > 0 && l.AccountId == salesDiscountAccId)
                .SumAsync(l => (decimal?)l.Credit) ?? 0;
        }
        var totalDiscount = totalDiscountRaw - periodDiscountReturned;

        var summary = new {
            totalOrders   = totalOrdersCount,
            totalGrossRevenue  = totalGrossRevenue,
            totalDiscount      = totalDiscount,
            totalUnits         = totalUnits,
            totalReturns       = ledgerReturns,
            totalNetRevenue    = totalGrossRevenue - totalDiscount - ledgerReturns,
            avgOrder      = totalOrdersCount > 0 ? (totalGrossRevenue - totalDiscount - ledgerReturns) / totalOrdersCount : 0,
            pos           = summaryData.Count(o => o.Source == OrderSource.POS),
            website       = summaryData.Count(o => o.Source == OrderSource.Website)
        };

        if (excel) return ExcelSales(rows, summary, from, to);

        var result = new { 
            from, to, rows, summary,
            pagination = new {
                totalCount = totalOrdersCount,
                pageSize,
                currentPage = page,
                totalPages = (int)Math.Ceiling(totalOrdersCount / (double)pageSize)
            }
        };

        if (!excel) _cache.Set(cacheKey, result, TimeSpan.FromMinutes(2));
        
        return Ok(result);
    }

    // ══════════════════════════════════════════════════════
    // 6. تقرير المشتريات
    // GET /api/operationalreports/purchases?fromDate=&toDate=&supplierId=
    // ══════════════════════════════════════════════════════
    [HttpGet("purchases")]
    [RequirePermission(ModuleKeys.ReportsMain + "," + ModuleKeys.Dashboard + "," + ModuleKeys.Pos + "," + ModuleKeys.InventoryGroup)]
    public async Task<IActionResult> PurchasesReport(
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] int?      supplierId = null,
        [FromQuery] int?      categoryId = null,
        [FromQuery] int?      brandId    = null,
        [FromQuery] string?   color      = null,
        [FromQuery] string?   size       = null,
        [FromQuery] OrderSource? source     = null,
        [FromQuery] int       page       = 1,
        [FromQuery] int       pageSize   = 50,
        [FromQuery] bool      excel      = false,
        [FromQuery] int?      branchId   = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var cacheKey = $"Purchases_{fromDate}_{toDate}_{supplierId}_{categoryId}_{brandId}_{color}_{size}_{source}_{page}_{pageSize}";
        if (!excel && _cache.TryGetValue(cacheKey, out var cachedData))
            return Ok(cachedData);

        var from = fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();
        _logger.LogInformation("Generating Purchases report from {From} to {To}", from, to);

        try
        {
            var maps = await _db.AccountSystemMappings.ToDictionaryAsync(m => m.Key, m => m.AccountId);
            var purchaseAccId = maps.GetValueOrDefault(MappingKeys.Purchase);

            var catIds = categoryId.HasValue && categoryId > 0 ? await FilterHelper.GetCategoryFamilyIds(_db, categoryId) : new List<int>();
            var brIds = brandId.HasValue && brandId > 0 ? await FilterHelper.GetBrandFamilyIds(_db, brandId) : new List<int>();

            // 1. Fetch Invoices joined with Posted JVs
            var q = _db.PurchaseInvoices.AsNoTracking()
                .Where(i => i.InvoiceDate >= from && i.InvoiceDate <= to)
                .Where(i => i.JournalEntries.Any(j => j.Status == JournalEntryStatus.Posted));

            if (supplierId.HasValue) q = q.Where(i => i.SupplierId == supplierId.Value);
            if (source.HasValue) q = q.Where(i => i.CostCenter == source.Value);
            if (branchId.HasValue)
            {
                var branchWarehouseIds = await _db.Warehouses.Where(w => w.BranchId == branchId.Value).Select(w => w.Id).ToListAsync();
                if (branchWarehouseIds.Any()) q = q.Where(i => i.WarehouseId.HasValue && branchWarehouseIds.Contains(i.WarehouseId.Value));
                else q = q.Where(i => false); // No warehouses for this branch — return empty
            }

            if (catIds.Any())
                q = q.Where(i => i.Items.Any(it => it.Product != null && it.Product.CategoryId.HasValue && catIds.Contains(it.Product.CategoryId.Value)));

            if (brIds.Any())
                q = q.Where(i => i.Items.Any(it => it.Product != null && it.Product.BrandId.HasValue && brIds.Contains(it.Product.BrandId.Value)));

            if (!string.IsNullOrEmpty(color))
                q = q.Where(i => i.Items.Any(it => it.ProductVariant != null && (it.ProductVariant.Color == color || it.ProductVariant.ColorAr == color)));

            if (!string.IsNullOrEmpty(size))
                q = q.Where(i => i.Items.Any(it => it.ProductVariant != null && it.ProductVariant.Size == size));

            var totalCount = await q.CountAsync();
            var invoicesQuery = q
                .Select(i => new {
                    i.Id, i.InvoiceNumber, i.SupplierInvoiceNumber, i.InvoiceDate,
                    SupplierName = i.Supplier != null ? i.Supplier.Name : "N/A",
                    i.PaymentTerms, i.Status, i.SubTotal, i.TaxAmount, i.TotalAmount, i.ReturnedAmount, i.PaidAmount,
                    Items = i.Items
                        .Where(it => 
                            (!catIds.Any() || (it.Product != null && it.Product.CategoryId.HasValue && catIds.Contains(it.Product.CategoryId.Value))) &&
                            (!brIds.Any() || (it.Product != null && it.Product.BrandId.HasValue && brIds.Contains(it.Product.BrandId.Value))) &&
                            (string.IsNullOrEmpty(color) || (it.ProductVariant != null && (it.ProductVariant.Color == color || it.ProductVariant.ColorAr == color))) &&
                            (string.IsNullOrEmpty(size) || (it.ProductVariant != null && it.ProductVariant.Size == size))
                        )
                        .Select(it => new {
                            ProductSKU = it.Product != null ? it.Product.SKU : "",
                            ProductNameAr = it.Product != null ? it.Product.NameAr : it.Description,
                            Size = it.ProductVariant != null ? it.ProductVariant.Size : "",
                            ColorAr = it.ProductVariant != null ? (it.ProductVariant.ColorAr ?? it.ProductVariant.Color) : "",
                            it.Quantity, it.UnitCost, it.TotalCost,
                            it.ProductId, it.ProductVariantId
                        }).ToList(),
                    LedgerPostedAmount = i.JournalEntries
                        .Where(j => j.Status == JournalEntryStatus.Posted)
                        .SelectMany(j => j.Lines)
                        .Where(l => l.AccountId == purchaseAccId)
                        .Sum(l => l.Debit)
                })
                .OrderByDescending(i => i.InvoiceDate);

            var invoices = excel
                ? await invoicesQuery.ToListAsync()
                : await invoicesQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var rows = invoices.Select(i => {
                var itemsTotal = i.Items.Sum(it => it.TotalCost);
                return new PurchaseRow(
                    i.Id, i.InvoiceNumber, i.SupplierInvoiceNumber ?? "",
                    i.SupplierName, i.InvoiceDate,
                    i.PaymentTerms.ToString(), i.Status.ToString(),
                    i.SubTotal, i.TaxAmount, 
                    (catIds.Any() || brIds.Any() || !string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(size)) ? itemsTotal : (i.LedgerPostedAmount > 0 ? i.LedgerPostedAmount : i.TotalAmount),
                    i.ReturnedAmount,
                    i.PaidAmount, i.TotalAmount - i.PaidAmount - i.ReturnedAmount,
                    i.Items.Select(it => new ReportItemDto(
                        it.ProductSKU, it.ProductNameAr, it.Size ?? "", it.ColorAr ?? "",
                        it.Quantity, 0, it.UnitCost, 0, it.TotalCost,
                        it.ProductId ?? 0, it.ProductVariantId ?? 0
                    )).ToList()
                );
            }).ToList();

            var summary = new {
                totalInvoices  = rows.Count,
                totalGross     = rows.Sum(r => r.TotalAmount),
                totalUnits     = rows.Sum(r => r.Items?.Sum(it => it.Quantity) ?? 0),
                totalReturned  = rows.Sum(r => r.ReturnedAmount),
                totalNet       = rows.Sum(r => r.TotalAmount - r.ReturnedAmount),
                totalPaid      = rows.Sum(r => r.PaidAmount),
                totalRemaining = rows.Sum(r => r.RemainingAmount),
            };

            if (excel) return ExcelPurchases(rows, summary, from, to);

            var result = new { 
                from, to, rows, summary,
                pagination = new {
                    totalCount,
                    pageSize,
                    currentPage = page,
                    totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                }
            };

            if (!excel) _cache.Set(cacheKey, result, TimeSpan.FromMinutes(2));

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PurchasesReport action");
            return StatusCode(500, new { message = "Error generating report", detail = ex.Message });
        }
    }

    // ══════════════════════════════════════════════════════
    // 7. مرتجعات المبيعات
    // GET /api/operationalreports/sales-returns
    // ══════════════════════════════════════════════════════
    [HttpGet("sales-returns")]
    [RequirePermission(ModuleKeys.ReportsMain + "," + ModuleKeys.Dashboard + "," + ModuleKeys.Pos + "," + ModuleKeys.InventoryGroup)]
    public async Task<IActionResult> SalesReturns(
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] int?      categoryId = null,
        [FromQuery] int?      brandId    = null,
        [FromQuery] string?   color      = null,
        [FromQuery] string?   size       = null,
        [FromQuery] string?   search     = null,
        [FromQuery] string?   returnType = null,
        [FromQuery] string?   reason     = null,
        [FromQuery] OrderSource? source     = null,
        [FromQuery] int       page       = 1,
        [FromQuery] int       pageSize   = 50,
        [FromQuery] bool      excel      = false,
        [FromQuery] int?      branchId   = null)
    {
        var from = fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var catIds = categoryId.HasValue && categoryId > 0 ? await FilterHelper.GetCategoryFamilyIds(_db, categoryId) : new List<int>();
        var brIds = brandId.HasValue && brandId > 0 ? await FilterHelper.GetBrandFamilyIds(_db, brandId) : new List<int>();

        var maps = await _db.AccountSystemMappings.ToDictionaryAsync(m => m.Key, m => m.AccountId);
        var inventoryAccId = maps.GetValueOrDefault(MappingKeys.Inventory);

        // Get all SalesReturn journal entries
        var returnsQ = _db.JournalEntries.AsNoTracking()
            .Where(j => j.Type == JournalEntryType.SalesReturn 
                     && j.EntryDate >= from && j.EntryDate <= to);

        if (source.HasValue)
        {
            returnsQ = returnsQ.Where(j => j.Order != null && j.Order.Source == source.Value);
        }

        if (branchId.HasValue)
        {
            returnsQ = returnsQ.Where(j => j.Order != null && j.Order.BranchId == branchId.Value);
        }

        if (catIds.Any())
            returnsQ = returnsQ.Where(j => j.Order != null && j.Order.Items.Any(it => it.Product != null && it.Product.CategoryId.HasValue && catIds.Contains(it.Product.CategoryId.Value)));

        if (brIds.Any())
            returnsQ = returnsQ.Where(j => j.Order != null && j.Order.Items.Any(it => it.Product != null && it.Product.BrandId.HasValue && brIds.Contains(it.Product.BrandId.Value)));

        if (!string.IsNullOrEmpty(color))
            returnsQ = returnsQ.Where(j => j.Order != null && j.Order.Items.Any(it => it.Color == color || (it.Color == null && it.Product != null && it.Product.Variants.Any(v => (v.Color == color || v.ColorAr == color)))));

        if (!string.IsNullOrEmpty(size))
            returnsQ = returnsQ.Where(j => j.Order != null && j.Order.Items.Any(it => it.Size == size || (it.Size == null && it.Product != null && it.Product.Variants.Any(v => v.Size == size))));

        if (!string.IsNullOrEmpty(search))
        {
            var searchLower = search.ToLower();
            var searchHash = Sportive.API.Models.Customer.EncryptionHelper?.ComputeSearchHash(search) ?? "";

            returnsQ = returnsQ.Where(j => 
                (j.Reference != null && j.Reference.ToLower().Contains(searchLower)) ||
                (j.Order != null && j.Order.Customer != null && j.Order.Customer.FullName.ToLower().Contains(searchLower)) ||
                (j.Order != null && j.Order.Customer != null && j.Order.Customer.PhoneHash == searchHash)
            );
        }

        if (!string.IsNullOrEmpty(returnType) && returnType != "all")
        {
            if (returnType == "direct")
                returnsQ = returnsQ.Where(j => j.OrderId == null || (j.Description != null && j.Description.Contains("بدون فاتورة")));
            else if (returnType == "invoice")
                returnsQ = returnsQ.Where(j => j.OrderId != null && (j.Description == null || !j.Description.Contains("بدون فاتورة")));
        }

        if (!string.IsNullOrEmpty(reason) && reason != "all")
        {
            if (reason == "سبب آخر")
            {
                returnsQ = returnsQ.Where(j => 
                    !(j.Order != null && j.Order.StatusHistory.Any(h => (h.Status == OrderStatus.Returned || h.Status == OrderStatus.PartiallyReturned) && 
                        (h.Note.Contains("منتج تالف") || h.Note.Contains("صنف خاطئ") || h.Note.Contains("مقاس غير مناسب") || h.Note.Contains("جودة غير مرضية") || h.Note.Contains("تغيير رأي"))))
                    && !(j.OrderId == null && j.Description != null && 
                        (j.Description.Contains("منتج تالف") || j.Description.Contains("صنف خاطئ") || j.Description.Contains("مقاس غير مناسب") || j.Description.Contains("جودة غير مرضية") || j.Description.Contains("تغيير رأي")))
                );
            }
            else
            {
                returnsQ = returnsQ.Where(j => 
                    (j.Order != null && j.Order.StatusHistory.Any(h => (h.Status == OrderStatus.Returned || h.Status == OrderStatus.PartiallyReturned) && h.Note.Contains(reason))) ||
                    (j.OrderId == null && j.Description != null && j.Description.Contains(reason))
                );
            }
        }

        pageSize = Math.Clamp(pageSize, 1, 100);
        var totalCount = await returnsQ.CountAsync();

        var returnsQuery = returnsQ
            .Select(j => new {
                j.Reference, j.EntryNumber, j.EntryDate,
                OrderId = j.Order != null ? (int?)j.Order.Id : null,
                CustomerName = j.Order != null && j.Order.Customer != null ? j.Order.Customer.FullName : "Walk-in",
                CustomerPhone = j.Order != null && j.Order.Customer != null ? j.Order.Customer.PhoneEncrypted : "",
                OriginalAmount = j.Lines.Where(l => l.Debit > 0 && (!inventoryAccId.HasValue || l.AccountId != inventoryAccId.Value)).Sum(l => (decimal?)l.Debit) ?? 0,
                Description = j.Order != null ? j.Order.StatusHistory
                    .Where(h => h.Status == OrderStatus.PartiallyReturned || h.Status == OrderStatus.Returned)
                    .OrderByDescending(h => h.CreatedAt)
                    .Select(h => h.Note)
                    .FirstOrDefault() ?? j.Description : j.Description,
                CreatedByUserId = j.Order != null ? j.Order.StatusHistory
                    .Where(h => (int)h.Status == (int)OrderStatus.PartiallyReturned || (int)h.Status == (int)OrderStatus.Returned)
                    .OrderByDescending(h => h.CreatedAt)
                    .Select(h => h.ChangedByUserId)
                    .FirstOrDefault() ?? j.CreatedByUserId : j.CreatedByUserId,
                SalesPersonId = j.Order != null ? j.Order.SalesPersonId : null,
                RefundMethod = j.Lines.Where(l => l.Credit > 0 && l.Account != null).Select(l => l.Account.NameAr).FirstOrDefault() ?? "نقدي",
                OrderStatus = j.Order != null ? j.Order.Status.ToString() : "",
                Items = j.Order != null ? j.Order.Items
                    .Where(i => i.ReturnedQuantity > 0)
                    .Where(i => 
                        (!catIds.Any() || (i.Product != null && i.Product.CategoryId.HasValue && catIds.Contains(i.Product.CategoryId.Value))) &&
                        (!brIds.Any() || (i.Product != null && i.Product.BrandId.HasValue && brIds.Contains(i.Product.BrandId.Value))) &&
                        (string.IsNullOrEmpty(color) || i.Color == color || (i.Color == null && i.Product != null && i.Product.Variants.Any(v => (v.Color == color || v.ColorAr == color)))) &&
                        (string.IsNullOrEmpty(size) || i.Size == size || (i.Size == null && i.Product != null && i.Product.Variants.Any(v => v.Size == size))))
                    .Select(i => new {
                        ProductSKU = i.Product != null ? i.Product.SKU : "",
                        ProductNameAr = i.Product != null ? i.Product.NameAr : i.ProductNameAr,
                        i.Size, i.Color, i.ReturnedQuantity, i.UnitPrice, i.Quantity, i.DiscountAmount,
                        i.ProductId, i.ProductVariantId
                    }).ToList() : null
            })
            .OrderByDescending(j => j.EntryDate);

        var returns = excel
            ? await returnsQuery.ToListAsync()
            : await returnsQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var allReturnRefs = returns.Select(r => r.Reference ?? r.EntryNumber).ToList();
        var movements = await _db.InventoryMovements
            .Include(m => m.Product)
            .Include(m => m.ProductVariant)
            .Where(m => m.Reference != null && allReturnRefs.Contains(m.Reference) && m.Type == InventoryMovementType.ReturnIn)
            .ToListAsync();

        var movementsMap = movements.GroupBy(m => m.Reference!).ToDictionary(g => g.Key!, g => g.ToList());

        // Resolve staff names for the report
        var personIds = returns.Select(r => r.CreatedByUserId)
            .Concat(returns.Select(r => r.SalesPersonId))
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        var numericIds = personIds.Where(id => int.TryParse(id, out _)).Select(id => int.Parse(id!)).ToList();
        var empNames = await _db.Employees.Where(e => numericIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id.ToString(), e => e.Name);
        var remainingIds = personIds.Where(id => id != null && !empNames.ContainsKey(id)).ToList();
        var userNamesResult = await _db.Users.Where(u => remainingIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);

        var personNamesMap = empNames;
        foreach (var un in userNamesResult) 
        {
            if (!personNamesMap.ContainsKey(un.Key)) 
                personNamesMap[un.Key] = un.Value;
        }

        var rows = returns.Select(j => {
            List<ReportItemDto>? itemsList = null;
            var refKey = j.Reference ?? j.EntryNumber;

            if (movementsMap.TryGetValue(refKey, out var movs) && movs.Any())
            {
                itemsList = movs.Select(m => {
                    decimal unitPrice = m.UnitCost;
                    if (j.OrderId != null && j.Items != null)
                    {
                        var orderItem = j.Items.FirstOrDefault(oi => 
                            oi.ProductSKU == (m.Product?.SKU ?? "") && 
                            (oi.Size ?? "") == (m.ProductVariant?.Size ?? "") && 
                            (oi.Color ?? "") == (m.ProductVariant?.ColorAr ?? m.ProductVariant?.Color ?? "")
                        );
                        if (orderItem != null)
                        {
                            unitPrice = orderItem.UnitPrice;
                        }
                    }
                    return new ReportItemDto(
                        m.Product?.SKU ?? "",
                        m.Product?.NameAr ?? "",
                        m.ProductVariant?.Size ?? "",
                        m.ProductVariant?.ColorAr ?? m.ProductVariant?.Color ?? "",
                        Math.Abs(m.Quantity),
                        unitPrice,
                        0,
                        0,
                        Math.Abs(m.Quantity) * unitPrice,
                        m.ProductId ?? 0,
                        m.ProductVariantId ?? 0
                    );
                }).ToList();
            }
            else if (j.Items != null)
            {
                itemsList = j.Items.Select(i => new ReportItemDto(
                    i.ProductSKU,
                    i.ProductNameAr,
                    i.Size ?? "",
                    i.Color ?? "",
                    i.ReturnedQuantity,
                    i.UnitPrice,
                    0,
                    i.DiscountAmount / (i.Quantity > 0 ? i.Quantity : 1),
                    (i.UnitPrice - (i.DiscountAmount / (i.Quantity > 0 ? i.Quantity : 1))) * i.ReturnedQuantity,
                    i.ProductId ?? 0,
                    i.ProductVariantId ?? 0
                )).ToList();
            }

            var itemsAmount = itemsList?.Sum(it => it.LineTotal) ?? 0;

            return new ReturnRow(
                j.Reference ?? j.EntryNumber, j.EntryDate,
                j.CustomerName,
                (Sportive.API.Models.Customer.EncryptionHelper != null && !string.IsNullOrEmpty(j.CustomerPhone) ? Sportive.API.Models.Customer.EncryptionHelper.Decrypt(j.CustomerPhone) : j.CustomerPhone) ?? "",
                (catIds.Any() || brIds.Any() || !string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(size)) ? itemsAmount : j.OriginalAmount,
                j.Description ?? "",
                itemsList,
                j.OrderId,
                (j.SalesPersonId != null && personNamesMap.TryGetValue(j.SalesPersonId, out var creator)) ? creator : "System/Unknown",
                (j.CreatedByUserId != null && personNamesMap.TryGetValue(j.CreatedByUserId, out var returner)) ? returner : "System/Unknown",
                j.RefundMethod,
                j.OrderStatus
            );
        }).ToList();

        decimal totalAmount = 0;
        if (totalCount > 0)
        {
            totalAmount = await returnsQ.Select(j => j.Lines
                .Where(l => l.Debit > 0 && (!inventoryAccId.HasValue || l.AccountId != inventoryAccId.Value))
                .Sum(l => (decimal?)l.Debit) ?? 0
            ).SumAsync();
        }

        var allReturnRefsForSummary = await returnsQ.Select(j => j.Reference ?? j.EntryNumber).ToListAsync();
        var totalReturnedItems = await _db.InventoryMovements
            .Where(m => m.Reference != null && allReturnRefsForSummary.Contains(m.Reference) && m.Type == InventoryMovementType.ReturnIn)
            .SumAsync(m => (int?)m.Quantity) ?? 0;

        var summary = new {
            count        = totalCount,
            totalAmount  = totalAmount,
            totalReturnedItems = totalReturnedItems
        };

        if (excel) return ExcelReturns(rows, summary, from, to, "مرتجعات المبيعات");

        return Ok(new { 
            from, 
            to, 
            rows, 
            summary,
            pagination = new {
                totalCount,
                pageSize,
                currentPage = page,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            }
        });
    }

    // ══════════════════════════════════════════════════════
    // 8. مرتجعات المشتريات
    // GET /api/operationalreports/purchase-returns
    // ══════════════════════════════════════════════════════
    [HttpGet("purchase-returns")]
    [RequirePermission(ModuleKeys.ReportsMain + "," + ModuleKeys.Dashboard + "," + ModuleKeys.Pos + "," + ModuleKeys.InventoryGroup)]
    public async Task<IActionResult> PurchaseReturns(
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] int?      supplierId = null,
        [FromQuery] int?      categoryId = null,
        [FromQuery] int?      brandId    = null,
        [FromQuery] string?   color      = null,
        [FromQuery] string?   size       = null,
        [FromQuery] OrderSource? source     = null,
        [FromQuery] int       page       = 1,
        [FromQuery] int       pageSize   = 50,
        [FromQuery] bool      excel      = false,
        [FromQuery] int?      branchId   = null)
    {
        var from = fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var catIds = categoryId.HasValue && categoryId > 0 ? await FilterHelper.GetCategoryFamilyIds(_db, categoryId) : new List<int>();
        var brIds = brandId.HasValue && brandId > 0 ? await FilterHelper.GetBrandFamilyIds(_db, brandId) : new List<int>();

        var q = _db.PurchaseReturns.AsNoTracking()
            .Where(r => r.ReturnDate >= from && r.ReturnDate <= to);

        if (supplierId.HasValue) q = q.Where(r => r.SupplierId == supplierId.Value);
        if (source.HasValue) q = q.Where(r => r.CostCenter == source.Value);
        if (branchId.HasValue)
        {
            var branchWarehouseIds = await _db.Warehouses.Where(w => w.BranchId == branchId.Value).Select(w => w.Id).ToListAsync();
            if (branchWarehouseIds.Any()) q = q.Where(r => r.WarehouseId.HasValue && branchWarehouseIds.Contains(r.WarehouseId.Value));
            else q = q.Where(r => false);
        }

        if (catIds.Any())
            q = q.Where(r => r.Items.Any(it => it.Product != null && it.Product.CategoryId.HasValue && catIds.Contains(it.Product.CategoryId.Value)));

        if (brIds.Any())
            q = q.Where(r => r.Items.Any(it => it.Product != null && it.Product.BrandId.HasValue && brIds.Contains(it.Product.BrandId.Value)));

        if (!string.IsNullOrEmpty(color))
            q = q.Where(r => r.Items.Any(it => it.ProductVariant != null && (it.ProductVariant.Color == color || it.ProductVariant.ColorAr == color)));

        if (!string.IsNullOrEmpty(size))
            q = q.Where(r => r.Items.Any(it => it.ProductVariant != null && it.ProductVariant.Size == size));

        var returnsQuery = q
            .Select(r => new {
                r.ReturnNumber, r.ReturnDate,
                SupplierName = r.Supplier != null ? r.Supplier.Name : "N/A",
                SupplierPhone = r.Supplier != null ? r.Supplier.Phone : "",
                OriginalTotal = r.TotalAmount, r.Notes,
                InvoiceNumber = r.Invoice != null ? r.Invoice.InvoiceNumber : null,
                Items = r.Items
                    .Where(it => 
                        (!catIds.Any() || (it.Product != null && it.Product.CategoryId.HasValue && catIds.Contains(it.Product.CategoryId.Value))) &&
                        (!brIds.Any() || (it.Product != null && it.Product.BrandId.HasValue && brIds.Contains(it.Product.BrandId.Value))) &&
                        (string.IsNullOrEmpty(color) || (it.ProductVariant != null && (it.ProductVariant.Color == color || it.ProductVariant.ColorAr == color))) &&
                        (string.IsNullOrEmpty(size) || (it.ProductVariant != null && it.ProductVariant.Size == size))
                    )
                    .Select(it => new {
                        ProductSKU = it.Product != null ? it.Product.SKU : "",
                        ProductNameAr = it.Product != null ? it.Product.NameAr : "",
                        Size = it.ProductVariant != null ? it.ProductVariant.Size : "",
                        ColorAr = it.ProductVariant != null ? (it.ProductVariant.ColorAr ?? it.ProductVariant.Color) : "",
                        it.Quantity, it.UnitCost, it.TotalCost,
                        it.ProductId, it.ProductVariantId
                    }).ToList()
            })
            .OrderByDescending(r => r.ReturnDate);

        var returns = excel
            ? await returnsQuery.ToListAsync()
            : await returnsQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var totalCount = await q.CountAsync();

        var rows = returns.Select(r => {
            var itemsList = r.Items.Select(it => new ReportItemDto(
                it.ProductSKU, it.ProductNameAr, it.Size ?? "", it.ColorAr ?? "",
                it.Quantity, 0, it.UnitCost, 0, it.TotalCost,
                it.ProductId ?? 0, it.ProductVariantId ?? 0
            )).ToList();

            var itemsAmount = itemsList.Sum(it => it.LineTotal);

            return new ReturnRow(
                r.ReturnNumber, r.ReturnDate,
                r.SupplierName, r.SupplierPhone,
                (catIds.Any() || brIds.Any() || !string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(size)) ? itemsAmount : r.OriginalTotal, 
                r.Notes ?? (r.InvoiceNumber != null ? $"مرتجع للفاتورة #{r.InvoiceNumber}" : "مرتجع مستقل"),
                itemsList
            );
        }).ToList();

        var summary = new { count = totalCount, totalAmount = rows.Sum(r => r.Amount) };
        if (excel) return ExcelReturns(rows, summary, from, to, "مرتجعات المشتريات");

        return Ok(new { from, to, rows, summary, pagination = new { totalCount, pageSize, currentPage = page, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize) } });
    }

    // ══════════════════════════════════════════════════════
    // 9. عمليات المستخدمين (الكاشير)
    // GET /api/operationalreports/user-activity
    // ══════════════════════════════════════════════════════
    [HttpGet("user-activity")]
    public async Task<IActionResult> UserActivity(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] string?   userId   = null,
        [FromQuery] bool      excel    = false,
        [FromQuery] int?      branchId = null)
    {
        var from = fromDate ?? TimeHelper.GetEgyptTime().Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var q = _db.Orders.AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Where(o => o.Source == OrderSource.POS
                     && o.CreatedAt >= from && o.CreatedAt <= to
                     && !string.IsNullOrEmpty(o.SalesPersonId));

        if (!string.IsNullOrEmpty(userId)) q = q.Where(o => o.SalesPersonId == userId);
        if (branchId.HasValue) q = q.Where(o => o.BranchId == branchId.Value);

        var orders = await q.OrderByDescending(o => o.CreatedAt).ToListAsync();
        var personIds = orders.Select(o => o.SalesPersonId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var numericIds = personIds.Where(id => int.TryParse(id, out _)).Select(id => int.Parse(id!)).ToList();
        
        var empNames = await _db.Employees.Where(e => numericIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id.ToString(), e => e.Name);
        var remainingIds = personIds.Where(id => id != null && !empNames.ContainsKey(id)).ToList();
        var userNamesResult = await _db.Users.Where(u => remainingIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);

        var personNamesMap = empNames;
        foreach (var un in userNamesResult) if (!personNamesMap.ContainsKey(un.Key)) personNamesMap[un.Key] = un.Value;

        var byPerson = orders
            .GroupBy(o => o.SalesPersonId != null && personNamesMap.ContainsKey(o.SalesPersonId) ? o.SalesPersonId : "Unknown")
            .Select(g => {
                var ordersList = g.ToList();
                var grossSales = ordersList.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount + o.DiscountAmount + o.TemporalDiscount);
                decimal totalReturns = 0;
                foreach(var o in ordersList.Where(o => o.Status != OrderStatus.Cancelled)) {
                    if (o.Status == OrderStatus.Returned) totalReturns += o.TotalAmount;
                    else {
                        foreach(var item in o.Items.Where(i => i.ReturnedQuantity > 0)) {
                            totalReturns += (item.Quantity > 0) ? (item.TotalPrice / item.Quantity) * item.ReturnedQuantity : 0;
                        }
                    }
                }
                var totalDiscount = ordersList.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.DiscountAmount + o.TemporalDiscount);
                var netSales = grossSales - totalReturns - totalDiscount;
                return new UserActivityRow(g.Key, personNamesMap.GetValueOrDefault(g.Key, "System/Unknown"), ordersList.Count, grossSales, totalReturns, totalDiscount, netSales, ordersList.Count(o => o.Status == OrderStatus.Cancelled));
            }).OrderByDescending(r => r.NetSales).ToList();

        var detail = orders.Select(o => new {
            o.OrderNumber, o.CreatedAt, o.SalesPersonId,
            SalesPersonName = (o.SalesPersonId != null && personNamesMap.TryGetValue(o.SalesPersonId, out var name)) ? name : "Unknown",
            CustomerName = o.Customer?.FullName ?? "",
            o.TotalAmount, DiscountAmount = o.DiscountAmount + o.TemporalDiscount, Status = o.Status.ToString(),
            ItemCount = o.Items.Sum(i => i.Quantity),
            Items = o.Items.Select(it => new { it.Product?.SKU, ProductName = it.Product?.NameAr ?? it.ProductNameAr, it.Size, it.Color, it.Quantity, it.UnitPrice, it.DiscountAmount, it.TotalPrice }).ToList()
        }).ToList();

        if (excel) return ExcelUserActivity(byPerson, detail, from, to);
        return Ok(new { from, to, summary = byPerson, detail });
    }

    // ══════════════════════════════════════════════════════
    // 10. تقرير حركة الأصناف
    // GET /api/operationalreports/product-movement?productId=&fromDate=&toDate=
    // ══════════════════════════════════════════════════════
    [HttpGet("product-movement")]
    public async Task<IActionResult> ProductMovement(
        [FromQuery] int?      productId  = null,
        [FromQuery] string?   search     = null,
        [FromQuery] int?      categoryId = null,
        [FromQuery] int?      brandId    = null,
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] string?   color      = null,
        [FromQuery] string?   size       = null,
        [FromQuery] OrderSource? source  = null,
        [FromQuery] int       page       = 1,
        [FromQuery] int       pageSize   = 50,
        [FromQuery] bool      excel      = false,
        [FromQuery] int?      branchId   = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        try
        {
            var from = fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1).Date;
            var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

            // Handle search
            if (productId == null && !string.IsNullOrEmpty(search))
            {
                var p = await _db.Products
                    .Where(x => (x.NameAr.Contains(search) || x.SKU.Contains(search)))
                    .FirstOrDefaultAsync();
                if (p != null) productId = p.Id;
            }

            // Fetch Movements from InventoryMovements table
            var movementsQuery = _db.InventoryMovements
                .Include(m => m.Product)
                .Include(m => m.ProductVariant)
                .Where(m => (m.CreatedAt >= from && m.CreatedAt <= to) || m.Type == InventoryMovementType.OpeningBalance);

            if (productId > 0) movementsQuery = movementsQuery.Where(m => m.ProductId == productId);
            if (source.HasValue) movementsQuery = movementsQuery.Where(m => m.CostCenter == source.Value);
            if (branchId.HasValue)
            {
                var branchWarehouseIds = await _db.Warehouses.Where(w => w.BranchId == branchId.Value).Select(w => w.Id).ToListAsync();
                if (branchWarehouseIds.Any()) movementsQuery = movementsQuery.Where(m => m.WarehouseId.HasValue && branchWarehouseIds.Contains(m.WarehouseId.Value));
                else movementsQuery = movementsQuery.Where(m => false);
            }

            if (categoryId.HasValue && categoryId > 0)
            {
                var categoryIds = await FilterHelper.GetCategoryFamilyIds(_db, categoryId);
                movementsQuery = movementsQuery.Where(m => m.Product != null && m.Product.CategoryId.HasValue && categoryIds.Contains(m.Product.CategoryId.Value));
            }

            if (brandId.HasValue && brandId > 0)
            {
                var brandIds = await FilterHelper.GetBrandFamilyIds(_db, brandId);
                movementsQuery = movementsQuery.Where(m => m.Product != null && m.Product.BrandId.HasValue && brandIds.Contains(m.Product.BrandId.Value));
            }

            if (!string.IsNullOrEmpty(color))
                movementsQuery = movementsQuery.Where(m => m.ProductVariant != null && (m.ProductVariant.Color == color || m.ProductVariant.ColorAr == color));

            if (!string.IsNullOrEmpty(size))
                movementsQuery = movementsQuery.Where(m => m.ProductVariant != null && m.ProductVariant.Size == size);

            var totalCount = await movementsQuery.CountAsync();

            // ✅ FIX: Load ALL movements for the product sorted chronologically ASC
            // to compute the correct running product-level balance, then paginate.
            var allMovementsForBalance = await movementsQuery
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Id)
                .ToListAsync();

            // Compute cumulative product-level running balance per movement
            var productBalanceMap = new Dictionary<int, int>(); // movementId -> productRunningBalance
            var runningProductTotal = 0;
            foreach (var m in allMovementsForBalance)
            {
                runningProductTotal += (int)m.Quantity;
                productBalanceMap[m.Id] = runningProductTotal;
            }

            // Now paginate: take the page slice (ordered descending for display)
            var dbMovements = allMovementsForBalance
                .OrderByDescending(m => m.CreatedAt)
                .ThenByDescending(m => m.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var orderRefs = dbMovements.Where(m => m.Type == InventoryMovementType.Sale || m.Type == InventoryMovementType.ReturnIn).Select(m => m.Reference).Distinct().ToList();
            var purchaseRefs = dbMovements.Where(m => m.Type == InventoryMovementType.Purchase || m.Type == InventoryMovementType.ReturnOut).Select(m => m.Reference).Distinct().ToList();

            var orderMap = await _db.Orders.AsNoTracking().Where(o => orderRefs.Contains(o.OrderNumber)).ToDictionaryAsync(o => o.OrderNumber, o => o.Id);
            var purchaseMap = await _db.PurchaseInvoices.AsNoTracking().Where(i => purchaseRefs.Contains(i.InvoiceNumber)).ToDictionaryAsync(i => i.InvoiceNumber, i => i.Id);

            // 🛡️ REFINEMENT: Resolve staff names for the report
            var personIds = dbMovements.Select(m => m.CreatedByUserId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            var numericIds = personIds.Where(id => int.TryParse(id, out _)).Select(id => int.Parse(id!)).ToList();
            var empNames = await _db.Employees.Where(e => numericIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id.ToString(), e => e.Name);
            var remainingIds = personIds.Where(id => id != null && !empNames.ContainsKey(id)).ToList();
            var userNamesResult = await _db.Users.Where(u => remainingIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
            var personNamesMap = empNames;
            foreach (var un in userNamesResult) if (!personNamesMap.ContainsKey(un.Key)) personNamesMap[un.Key] = un.Value;

            var movements = dbMovements.Select(m => {
                int? sourceId = null;
                if ((m.Type == InventoryMovementType.Sale || m.Type == InventoryMovementType.ReturnIn) && m.Reference != null)
                {
                    if (orderMap.TryGetValue(m.Reference, out var orderId))
                        sourceId = orderId;
                }
                else if ((m.Type == InventoryMovementType.Purchase || m.Type == InventoryMovementType.ReturnOut) && m.Reference != null)
                {
                    if (purchaseMap.TryGetValue(m.Reference, out var purchaseId))
                        sourceId = purchaseId;
                }

                return new ProductMovementLine(
                    m.CreatedAt,
                    TranslateMovementType(m.Type),
                    m.Reference ?? "N/A",
                    m.Note ?? "-",
                    (m.ProductVariant != null ? (m.ProductVariant.Size + " / " + (m.ProductVariant.ColorAr ?? m.ProductVariant.Color ?? "")) : "أساسي"),
                    m.Quantity > 0 ? m.Quantity : 0,
                    m.Quantity < 0 ? Math.Abs(m.Quantity) : 0,
                    m.Quantity * m.UnitCost,
                    m.Product?.NameAr ?? "Deleted Product",
                    (m.CreatedByUserId != null && personNamesMap.TryGetValue(m.CreatedByUserId, out var creator)) ? creator : _t.Get("Common.System"),
                    _t.Get("Common.Completed"),
                    m.Product?.SKU ?? "N/A",
                    productBalanceMap.TryGetValue(m.Id, out var bal) ? bal : m.RemainingStock,
                    sourceId,
                    m.ProductVariant?.Size ?? "أساسي",
                    m.ProductVariant?.ColorAr ?? m.ProductVariant?.Color ?? "-"
                );
            }).ToList();

            // Summary
            decimal currentStock = 0;
            string productBrief = "الكل";

            if (productId > 0)
            {
                var product = await _db.Products
                    .Include(p => p.Variants)
                    .FirstOrDefaultAsync(p => p.Id == productId);
                if (product != null)
                {
                    currentStock = product.Variants?.Any() == true
                        ? product.Variants.Sum(v => v.StockQuantity)
                        : product.TotalStock;
                    productBrief = $"{product.NameAr} ({product.SKU})";
                }
            }
            else
            {
                currentStock = await _db.ProductVariants.SumAsync(v => (decimal)v.StockQuantity) 
                             + await _db.Products.Where(p => !p.Variants.Any()).SumAsync(p => (decimal)p.TotalStock);
            }

            var summary = new
            {
                totalIn         = movements.Sum(i => i.In),
                totalOut        = movements.Sum(i => i.Out),
                totalPurchased  = dbMovements.Where(m => m.Type == InventoryMovementType.Purchase).Sum(m => m.Quantity),
                totalSold       = dbMovements.Where(m => m.Type == InventoryMovementType.Sale).Sum(m => Math.Abs(m.Quantity)),
                salesRevenue    = 0, // Not applicable globally in this simplified view
                currentStock
            };

            if (excel) return ExcelProductMovement(productId == 0 ? null : await _db.Products.FindAsync(productId), movements, summary, from, to);

            var result = new
            {
                productId = productId,
                productName = productBrief,
                from,
                to,
                rows = movements,
                movements,
                summary,
                pagination = new
                {
                    totalCount,
                    pageSize,
                    currentPage = page,
                    totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                }
            };

            if (!excel) { /* result already computed */ }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProductMovement report failed");
            return StatusCode(500, new { message = "حدث خطأ أثناء تنفيذ التقرير. يرجى المحاولة مرة أخرى." });
        }
    }

    // ══════════════════════════════════════════════════════
    // 11. سجل حركات المخزن الشامل (Advanced Movement Ledger)
    // GET /api/operationalreports/stock-movements?productId=&fromDate=&toDate=&type=
    // ══════════════════════════════════════════════════════
    [HttpGet("stock-movement")]
    [HttpGet("stock-movements")] // Alias for different frontend versions
    public async Task<IActionResult> StockMovementsLedger(
        [FromQuery] int?      productId  = null,
        [FromQuery] int?      variantId = null,
        [FromQuery] int?      categoryId = null,
        [FromQuery] int?      brandId    = null,
        [FromQuery] DateTime? fromDate  = null,
        [FromQuery] DateTime? toDate    = null,
        [FromQuery] OrderSource? source = null,
        [FromQuery] InventoryMovementType? type = null,
        [FromQuery] int?      branchId   = null,
        [FromQuery] int       page       = 1,
        [FromQuery] int       pageSize   = 50,
        [FromQuery] bool      excel      = false)
    {
        var from = fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var q = _db.InventoryMovements.AsNoTracking()
            .Include(m => m.Product)
            .Include(m => m.ProductVariant)
            .Where(m => (m.CreatedAt >= from && m.CreatedAt <= to) || m.Type == InventoryMovementType.OpeningBalance)
            .AsQueryable();

        if (branchId.HasValue)
        {
            var branchWarehouseIds = await _db.Warehouses.Where(w => w.BranchId == branchId.Value).Select(w => w.Id).ToListAsync();
            if (branchWarehouseIds.Any()) q = q.Where(m => m.WarehouseId.HasValue && branchWarehouseIds.Contains(m.WarehouseId.Value));
            else q = q.Where(m => false);
        }

        if (productId.HasValue && productId > 0) q = q.Where(m => m.ProductId == productId.Value);
        if (variantId.HasValue && variantId > 0) q = q.Where(m => m.ProductVariantId == variantId.Value);
        if (source.HasValue) q = q.Where(m => m.CostCenter == source.Value);
        if (type.HasValue) q = q.Where(m => m.Type == type.Value);

        if (categoryId.HasValue && categoryId > 0)
        {
            var categoryIds = await FilterHelper.GetCategoryFamilyIds(_db, categoryId);
            q = q.Where(m => m.Product != null && m.Product.CategoryId.HasValue && categoryIds.Contains(m.Product.CategoryId.Value));
        }

        if (brandId.HasValue && brandId > 0)
        {
            var brandIds = await FilterHelper.GetBrandFamilyIds(_db, brandId);
            q = q.Where(m => m.Product != null && m.Product.BrandId.HasValue && brandIds.Contains(m.Product.BrandId.Value));
        }

        var totalCount = await q.CountAsync();
        
        var dbMovements = await q
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();


        // Mapping starts
        var rows = dbMovements.Select(m => new {
            m.Id,
            m.CreatedAt,
            productName = m.Product?.NameAr,
            sku         = m.Product?.SKU,
            variant     = m.ProductVariant != null ? m.ProductVariant.Size : "أساسي",
            color       = m.ProductVariant != null ? (m.ProductVariant.ColorAr ?? m.ProductVariant.Color) : "",
            colorAr     = m.ProductVariant != null ? m.ProductVariant.ColorAr : "",
            entryType   = m.Type.ToString(),
            entryTypeAr = GetMovementTypeAr(m.Type),
            m.Quantity,
            m.RemainingStock,
            m.Reference,
            m.Note,
            m.UnitCost,
            totalValue = m.Quantity * m.UnitCost
        }).ToList();

        return Ok(new { 
            from, 
            to, 
            rows, 
            movements = rows, // Duplicate for compatibility with different report versions
            pagination = new {
                totalCount,
                pageSize,
                currentPage = page,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            }
        });
    }

    private string GetMovementTypeAr(InventoryMovementType type) => type switch
    {
        InventoryMovementType.OpeningBalance => "رصيد أول المدة",
        InventoryMovementType.Purchase       => "مشتريات",
        InventoryMovementType.Sale           => "مبيعات",
        InventoryMovementType.ReturnIn       => "مرتجع مبيعات",
        InventoryMovementType.ReturnOut      => "مرتجع مشتريات",
        InventoryMovementType.Audit          => "جرد",
        InventoryMovementType.Adjustment     => "تسوية مخزنية",
        InventoryMovementType.TransferIn     => "تحويل للداخل",
        InventoryMovementType.TransferOut    => "تحويل للخارج",
        _ => type.ToString()
    };

    // ══════════════════════════════════════════════════════
    private IActionResult ExcelCustomerStatement(Customer c, List<CustomerStatementLine> lines, decimal invoiced, decimal paid, decimal outstanding, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(_t.Get("Reports.CustomerStatement"));
        ws.RightToLeft = true;
        ws.Cell(1,1).Value = _t.Get("Reports.CustomerStatementTitle", c.FullName ?? "", c.Phone ?? "");
        ws.Cell(1,1).Style.Font.Bold = true; ws.Cell(1,1).Style.Font.FontSize = 13;
        ws.Cell(2,1).Value = _t.Get("Reports.DateRange", from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"));

        string[] h = {_t.Get("Reports.DateHeader"), _t.Get("Reports.TypeHeader"), _t.Get("Reports.ReferenceHeader"), _t.Get("Reports.DescriptionHeader"), _t.Get("Reports.DebitHeader"), _t.Get("Reports.CreditHeader"), _t.Get("Reports.BalanceHeader")};
        for (int i=0;i<h.Length;i++){ws.Cell(3,i+1).Value=h[i];ws.Cell(3,i+1).Style.Font.Bold=true;ws.Cell(3,i+1).Style.Fill.BackgroundColor=XLColor.FromHtml("#1a237e");ws.Cell(3,i+1).Style.Font.FontColor=XLColor.White;}

        int r=4;
        foreach(var l in lines){ws.Cell(r,1).Value=l.Date.ToString("yyyy-MM-dd");ws.Cell(r,2).Value=l.Type;ws.Cell(r,3).Value=l.Reference;ws.Cell(r,4).Value=l.Description;ws.Cell(r,5).Value=l.Debit;ws.Cell(r,6).Value=l.Credit;ws.Cell(r,7).Value=l.Balance;for(int c2=5;c2<=7;c2++)ws.Cell(r,c2).Style.NumberFormat.Format="#,##0.00";r++;}

        ws.Cell(r,4).Value = _t.Get("Reports.Total"); ws.Cell(r,4).Style.Font.Bold=true;ws.Cell(r,5).Value=invoiced;ws.Cell(r,6).Value=paid;ws.Cell(r,7).Value=outstanding;for(int c2=5;c2<=7;c2++){ws.Cell(r,c2).Style.Font.Bold=true;ws.Cell(r,c2).Style.NumberFormat.Format="#,##0.00";}
        if (r > 4) ws.Range(3, 1, r - 1, h.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"customer_{c.Id}_{from:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelCustomerAging(List<CustomerAgingRow> rows, DateTime asOf)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(_t.Get("Reports.CustomerAging"));
        ws.RightToLeft = true;
        ws.Cell(1,1).Value = _t.Get("Reports.CustomerAgingTitle", asOf.ToString("yyyy-MM-dd"));
        ws.Cell(1,1).Style.Font.Bold = true; ws.Cell(1,1).Style.Font.FontSize = 13;
        ws.Range(1,1,1,7).Merge();

        string[] h = {_t.Get("Reports.CustomerHeader"), _t.Get("Reports.PhoneHeader"), _t.Get("Reports.TotalBucket"), _t.Get("Reports.CurrentBucket"), _t.Get("Reports.Days30Bucket"), _t.Get("Reports.Days60Bucket"), _t.Get("Reports.Over90Bucket")};
        for(int i=0;i<h.Length;i++){ws.Cell(2,i+1).Value=h[i];ws.Cell(2,i+1).Style.Font.Bold=true;ws.Cell(2,i+1).Style.Fill.BackgroundColor=XLColor.FromHtml("#1a237e");ws.Cell(2,i+1).Style.Font.FontColor=XLColor.White;}

        int r=3;
        foreach(var row in rows){ws.Cell(r,1).Value=row.Name;ws.Cell(r,2).Value=row.Phone;ws.Cell(r,3).Value=row.Total;ws.Cell(r,4).Value=row.Current;ws.Cell(r,5).Value=row.Days60;ws.Cell(r,6).Value=row.Days90;ws.Cell(r,7).Value=row.Over90;for(int c=3;c<=7;c++)ws.Cell(r,c).Style.NumberFormat.Format="#,##0.00";if(row.Over90>0)ws.Row(r).Style.Font.FontColor=XLColor.Red;r++;}
        if (r > 3) ws.Range(2, 1, r - 1, h.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"customer_aging_{asOf:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelSupplierAging(List<SupplierAgingRow> rows, DateTime asOf)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(_t.Get("Reports.SupplierAging"));
        ws.RightToLeft = true;
        ws.Cell(1,1).Value = _t.Get("Reports.SupplierAgingTitle", asOf.ToString("yyyy-MM-dd"));
        ws.Cell(1,1).Style.Font.Bold = true; ws.Cell(1,1).Style.Font.FontSize = 13;

        string[] h = {_t.Get("Reports.SupplierHeader"), _t.Get("Reports.PhoneHeader"), "Company", _t.Get("Reports.TotalBucket"), _t.Get("Reports.CurrentBucket"), _t.Get("Reports.Days30Bucket"), _t.Get("Reports.Days60Bucket"), _t.Get("Reports.Over90Bucket")};
        for(int i=0;i<h.Length;i++){ws.Cell(2,i+1).Value=h[i];ws.Cell(2,i+1).Style.Font.Bold=true;ws.Cell(2,i+1).Style.Fill.BackgroundColor=XLColor.FromHtml("#c62828");ws.Cell(2,i+1).Style.Font.FontColor=XLColor.White;}

        int r=3;
        foreach(var row in rows){ws.Cell(r,1).Value=row.Name;ws.Cell(r,2).Value=row.Phone;ws.Cell(r,3).Value=row.CompanyName;ws.Cell(r,4).Value=row.Total;ws.Cell(r,5).Value=row.Current;ws.Cell(r,6).Value=row.Days60;ws.Cell(r,7).Value=row.Days90;ws.Cell(r,8).Value=row.Over90;for(int c=4;c<=8;c++)ws.Cell(r,c).Style.NumberFormat.Format="#,##0.00";r++;}
        if (r > 3) ws.Range(2, 1, r - 1, h.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"supplier_aging_{asOf:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelSupplierStatement(Supplier s, List<CustomerStatementLine> lines, decimal invoiced, decimal paid, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(_t.Get("Reports.SupplierStatement"));
        ws.RightToLeft = true;
        ws.Cell(1,1).Value = _t.Get("Reports.SupplierStatementTitle", s.Name, s.Phone);
        ws.Cell(1,1).Style.Font.Bold = true;
        ws.Cell(2,1).Value = _t.Get("Reports.DateRange", from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"));

        string[] h = {_t.Get("Reports.DateHeader"), _t.Get("Reports.TypeHeader"), _t.Get("Reports.ReferenceHeader"), _t.Get("Reports.DescriptionHeader"), _t.Get("Reports.DebitHeader"), _t.Get("Reports.CreditHeader"), _t.Get("Reports.BalanceHeader")};
        for (int i=0;i<h.Length;i++){ws.Cell(3,i+1).Value=h[i];ws.Cell(3,i+1).Style.Font.Bold=true;ws.Cell(3,i+1).Style.Fill.BackgroundColor=XLColor.FromHtml("#c62828");ws.Cell(3,i+1).Style.Font.FontColor=XLColor.White;}

        int r=4;
        foreach(var l in lines){ws.Cell(r,1).Value=l.Date.ToString("yyyy-MM-dd");ws.Cell(r,2).Value=l.Type;ws.Cell(r,3).Value=l.Reference;ws.Cell(r,4).Value=l.Description;ws.Cell(r,5).Value=l.Debit;ws.Cell(r,6).Value=l.Credit;ws.Cell(r,7).Value=l.Balance;for(int c=5;c<=7;c++)ws.Cell(r,c).Style.NumberFormat.Format="#,##0.00";r++;}
        if (r > 4) ws.Range(3, 1, r - 1, h.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"supplier_statement_{s.Id}_{from:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelInventory(List<InventoryRow> rows, dynamic summary)
    {
        using var wb = new XLWorkbook();
        var ws1 = wb.Worksheets.Add(_t.Get("Reports.InventorySummarySheet"));
        ws1.RightToLeft = true;
        string[] h1 = {_t.Get("Reports.ProductNameHeader"), _t.Get("Reports.SkuHeader"), _t.Get("Reports.CategoryHeader"), _t.Get("Reports.PriceHeader"), _t.Get("Reports.PriceHeader"), _t.Get("Reports.StockHeader"), _t.Get("Reports.ValueHeader")};
        for(int i=0;i<h1.Length;i++){ws1.Cell(1,i+1).Value=h1[i];ws1.Cell(1,i+1).Style.Font.Bold=true;ws1.Cell(1,i+1).Style.Fill.BackgroundColor=XLColor.FromHtml("#1b5e20");ws1.Cell(1,i+1).Style.Font.FontColor=XLColor.White;}
        int r=2;
        foreach(var row in rows){ws1.Cell(r,1).Value=row.NameAr;ws1.Cell(r,2).Value=row.SKU;ws1.Cell(r,3).Value=row.CategoryName;ws1.Cell(r,4).Value=row.Price;ws1.Cell(r,5).Value=row.Price;ws1.Cell(r,6).Value=row.TotalStock;ws1.Cell(r,7).Value=row.TotalValue;ws1.Cell(r,4).Style.NumberFormat.Format="#,##0.00";ws1.Cell(r,5).Style.NumberFormat.Format="#,##0.00";ws1.Cell(r,7).Style.NumberFormat.Format="#,##0.00";if(row.TotalStock<=5)ws1.Row(r).Style.Fill.BackgroundColor=XLColor.FromHtml("#fff3e0");if(row.TotalStock==0)ws1.Row(r).Style.Fill.BackgroundColor=XLColor.FromHtml("#ffebee");r++;}
        if (r > 2) ws1.Range(1, 1, r - 1, h1.Length).SetAutoFilter();
        ws1.Columns().AdjustToContents();

        var ws2 = wb.Worksheets.Add(_t.Get("Reports.VariantDetailsSheet"));
        ws2.RightToLeft = true;
        string[] h2 = {_t.Get("Reports.ProductNameHeader"), _t.Get("Reports.SkuHeader"), _t.Get("Reports.SizeHeader"), _t.Get("Reports.ColorHeader"), _t.Get("Reports.StockHeader"), _t.Get("Reports.PriceHeader"), _t.Get("Reports.ValueHeader")};
        for(int i=0;i<h2.Length;i++){ws2.Cell(1,i+1).Value=h2[i];ws2.Cell(1,i+1).Style.Font.Bold=true;}
        int r2=2;
        foreach(var p in rows)foreach(var v in p.Variants){ws2.Cell(r2,1).Value=p.NameAr;ws2.Cell(r2,2).Value=p.SKU;ws2.Cell(r2,3).Value=v.Size;ws2.Cell(r2,4).Value=v.Color;ws2.Cell(r2,5).Value=v.StockQuantity;ws2.Cell(r2,6).Value=v.Price;ws2.Cell(r2,7).Value=v.Value;ws2.Cell(r2,6).Style.NumberFormat.Format="#,##0.00";ws2.Cell(r2,7).Style.NumberFormat.Format="#,##0.00";r2++;}
        if (r2 > 2) ws2.Range(1, 1, r2 - 1, h2.Length).SetAutoFilter();
        ws2.Columns().AdjustToContents();

        return ExcelResult(wb, $"inventory_{TimeHelper.GetEgyptTime():yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelSales(List<SalesRow> rows, dynamic summary, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(_t.Get("Reports.SalesDetailReport"));
        ws.RightToLeft = true;
        ws.Cell(1,1).Value = _t.Get("Reports.SalesDetailTitle", from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"));
        ws.Cell(1,1).Style.Font.Bold=true;
        
        string[] h={_t.Get("Reports.OrderNumberHeader"), _t.Get("Reports.DateHeader"), _t.Get("Reports.CustomerHeader"), _t.Get("Reports.PhoneHeader"), _t.Get("Reports.SourceHeader"), _t.Get("Reports.StatusHeader"), _t.Get("Reports.PaymentHeader"), _t.Get("Reports.PaymentDetailsHeader"), _t.Get("Reports.SkuHeader"), _t.Get("Reports.ProductNameHeader"), _t.Get("Reports.SizeHeader"), _t.Get("Reports.ColorHeader"), _t.Get("Reports.QtyHeader"), _t.Get("Reports.UnitPriceHeader"), _t.Get("Reports.ItemDiscountHeader"), _t.Get("Reports.ItemTotalHeader"), _t.Get("Reports.CouponDiscountHeader"), _t.Get("Reports.OrderTotalHeader")};
        for(int i=0;i<h.Length;i++){
            var cell = ws.Cell(2,i+1);
            cell.Value=h[i];
            cell.Style.Font.Bold=true;
            cell.Style.Fill.BackgroundColor=XLColor.FromHtml("#1a237e");
            cell.Style.Font.FontColor=XLColor.White;
        }

        int r=3;
        foreach(var order in rows)
        {
            if (order.Items == null || order.Items.Count == 0)
            {
                ws.Cell(r, 1).Value = order.OrderNumber;
                ws.Cell(r, 2).Value = order.Date.ToString("yyyy-MM-dd");
                ws.Cell(r, 3).Value = order.CustomerName;
                ws.Cell(r, 4).Value = order.Phone;
                ws.Cell(r, 5).Value = order.Source;
                ws.Cell(r, 6).Value = order.Status;
                ws.Cell(r, 7).Value = order.PaymentMethod;
                ws.Cell(r, 8).Value = order.PaymentDetails; // ✅ Added
                ws.Cell(r, 16).Value = order.TotalAmount;
                r++;
                continue;
            }

            foreach(var item in order.Items)
            {
                ws.Cell(r, 1).Value = order.OrderNumber;
                ws.Cell(r, 2).Value = order.Date.ToString("yyyy-MM-dd");
                ws.Cell(r, 3).Value = order.CustomerName;
                ws.Cell(r, 4).Value = order.Phone;
                ws.Cell(r, 5).Value = order.Source;
                ws.Cell(r, 6).Value = order.Status;
                ws.Cell(r, 7).Value = order.PaymentMethod;
                ws.Cell(r, 8).Value = order.PaymentDetails; // ✅ Added
                
                ws.Cell(r, 9).Value = item.SKU;
                ws.Cell(r, 10).Value = item.ProductName;
                ws.Cell(r, 11).Value = item.Size;
                ws.Cell(r, 12).Value = item.Color;
                ws.Cell(r, 13).Value = item.Quantity;
                ws.Cell(r, 14).Value = item.UnitPrice;
                ws.Cell(r, 15).Value = item.Discount * item.Quantity; // Total discount for this line
                ws.Cell(r, 16).Value = item.LineTotal;
                ws.Cell(r, 17).Value = order.DiscountAmount; // Total Order-level discount
                ws.Cell(r, 18).Value = order.TotalAmount; // Total Final for Order
                
                for(int c=14; c<=18; c++) ws.Cell(r,c).Style.NumberFormat.Format="#,##0.00";
                r++;
            }
        }
        if (r > 3) ws.Range(2, 1, r - 1, h.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"detailed_sales_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelPurchases(List<PurchaseRow> rows, dynamic summary, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(_t.Get("Reports.PurchasesDetailReport"));
        ws.RightToLeft = true;
        
        string[] h={_t.Get("Reports.InvoiceNumberHeader"), _t.Get("Reports.SupplierInvoiceHeader"), _t.Get("Reports.SupplierHeader"), _t.Get("Reports.DateHeader"), _t.Get("Reports.PaymentTermsHeader"), _t.Get("Reports.StatusHeader"), _t.Get("Reports.SkuHeader"), _t.Get("Reports.ProductNameHeader"), _t.Get("Reports.SizeHeader"), _t.Get("Reports.ColorHeader"), _t.Get("Reports.QtyHeader"), _t.Get("Reports.CostHeader"), _t.Get("Reports.ItemTotalHeader")};
        for(int i=0;i<h.Length;i++){
            var cell = ws.Cell(1,i+1);
            cell.Value=h[i];
            cell.Style.Font.Bold=true;
            cell.Style.Fill.BackgroundColor=XLColor.FromHtml("#e65100");
            cell.Style.Font.FontColor=XLColor.White;
        }

        int r=2;
        foreach(var inv in rows)
        {
            if (inv.Items == null || inv.Items.Count == 0)
            {
                ws.Cell(r, 1).Value = inv.InvoiceNumber;
                ws.Cell(r, 2).Value = inv.SupplierInvoiceNumber;
                ws.Cell(r, 3).Value = inv.SupplierName;
                ws.Cell(r, 4).Value = inv.InvoiceDate.ToString("yyyy-MM-dd");
                ws.Cell(r, 5).Value = inv.PaymentTerms;
                ws.Cell(r, 6).Value = inv.Status;
                ws.Cell(r, 13).Value = inv.TotalAmount;
                r++;
                continue;
            }

            foreach(var item in inv.Items)
            {
                ws.Cell(r, 1).Value = inv.InvoiceNumber;
                ws.Cell(r, 2).Value = inv.SupplierInvoiceNumber;
                ws.Cell(r, 3).Value = inv.SupplierName;
                ws.Cell(r, 4).Value = inv.InvoiceDate.ToString("yyyy-MM-dd");
                ws.Cell(r, 5).Value = inv.PaymentTerms;
                ws.Cell(r, 6).Value = inv.Status;

                ws.Cell(r, 7).Value = item.SKU;
                ws.Cell(r, 8).Value = item.ProductName;
                ws.Cell(r, 9).Value = item.Size;
                ws.Cell(r, 10).Value = item.Color;
                ws.Cell(r, 11).Value = item.Quantity;
                ws.Cell(r, 12).Value = item.UnitCost;
                ws.Cell(r, 13).Value = item.LineTotal;
                
                for(int c=12; c<=13; c++) ws.Cell(r,c).Style.NumberFormat.Format="#,##0.00";
                r++;
            }
        }
        if (r > 2) ws.Range(1, 1, r - 1, h.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"detailed_purchases_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelReturns(List<ReturnRow> rows, dynamic summary, DateTime from, DateTime to, string title)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(title);
        ws.RightToLeft = true;
        
        ws.Cell(1,1).Value = _t.Get("Reports.DetailedReturnsTitle", title, from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"));
        ws.Cell(1,1).Style.Font.Bold = true;
        ws.Cell(1,1).Style.Font.FontSize = 13;
        ws.Cell(1,1).Style.Font.FontColor = XLColor.FromHtml("#c62828");
        ws.Range(1, 1, 1, 13).Merge();
        
        string[] h = {
            _t.Get("Reports.ReferenceHeader"),
            _t.Get("Reports.DateHeader"),
            _t.Get("Reports.CustomerHeader"),
            _t.Get("Reports.PhoneHeader"),
            _t.Get("Reports.SkuHeader"),
            _t.Get("Reports.ProductNameHeader"),
            _t.Get("Reports.SizeHeader"),
            _t.Get("Reports.ColorHeader"),
            _t.Get("Reports.QtyHeader"),
            _t.Get("Reports.AmountHeader"),
            _t.Get("Reports.ReasonHeader"),
            "عامل الفاتورة",
            "مسؤول المرتجع"
        };
        for(int i=0;i<h.Length;i++){
            var cell = ws.Cell(2,i+1);
            cell.Value=h[i];
            cell.Style.Font.Bold=true;
            cell.Style.Font.FontSize = 11;
            cell.Style.Fill.BackgroundColor=XLColor.FromHtml("#37474f"); // Dark Slate Gray
            cell.Style.Font.FontColor=XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cfd8dc");
        }
        ws.Row(2).Height = 25;

        int r=3;
        bool alternateColor = false;
        foreach(var ret in rows)
        {
            int startRow = r;
            if (ret.Items == null || ret.Items.Count == 0)
            {
                ws.Cell(r, 1).Value = ret.Reference;
                ws.Cell(r, 2).Value = ret.Date.ToString("yyyy-MM-dd");
                ws.Cell(r, 3).Value = ret.Name;
                ws.Cell(r, 4).Value = ret.Phone;
                ws.Cell(r, 5).Value = "-";
                ws.Cell(r, 6).Value = "-";
                ws.Cell(r, 7).Value = "-";
                ws.Cell(r, 8).Value = "-";
                ws.Cell(r, 9).Value = 0;
                ws.Cell(r, 10).Value = ret.Amount;
                ws.Cell(r, 11).Value = GetCleanReason(ret.Reason);
                ws.Cell(r, 12).Value = ret.CreatorName;
                ws.Cell(r, 13).Value = ret.ReturnerName;

                for (int c = 1; c <= 13; c++)
                {
                    var cell = ws.Cell(r, c);
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cfd8dc");
                    
                    if (c == 3 || c == 6 || c == 11 || c == 12 || c == 13)
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    else
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    if (c == 10) cell.Style.NumberFormat.Format = "#,##0.00";
                    if (alternateColor) cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#f7f9fa");
                }
                r++;
            }
            else
            {
                foreach(var item in ret.Items)
                {
                    ws.Cell(r, 1).Value = ret.Reference;
                    ws.Cell(r, 2).Value = ret.Date.ToString("yyyy-MM-dd");
                    ws.Cell(r, 3).Value = ret.Name;
                    ws.Cell(r, 4).Value = ret.Phone;

                    ws.Cell(r, 5).Value = item.SKU;
                    ws.Cell(r, 6).Value = item.ProductName;
                    ws.Cell(r, 7).Value = item.Size;
                    ws.Cell(r, 8).Value = item.Color;
                    ws.Cell(r, 9).Value = item.Quantity;
                    ws.Cell(r, 10).Value = item.LineTotal;
                    ws.Cell(r, 11).Value = GetCleanReason(ret.Reason);
                    ws.Cell(r, 12).Value = ret.CreatorName;
                    ws.Cell(r, 13).Value = ret.ReturnerName;

                    for (int c = 1; c <= 13; c++)
                    {
                        var cell = ws.Cell(r, c);
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cfd8dc");
                        
                        if (c == 3 || c == 6 || c == 11 || c == 12 || c == 13)
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        else
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        if (c == 10) cell.Style.NumberFormat.Format = "#,##0.00";
                        if (alternateColor) cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#f7f9fa");
                    }
                    r++;
                }

                int endRow = r - 1;
                if (endRow > startRow)
                {
                    int[] columnsToMerge = { 1, 2, 3, 4, 11, 12, 13 };
                    foreach (int col in columnsToMerge)
                    {
                        var range = ws.Range(startRow, col, endRow, col);
                        range.Merge();
                        
                        if (col == 3 || col == 11 || col == 12 || col == 13)
                            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        else
                            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    }
                }
            }
            alternateColor = !alternateColor;
        }

        // Totals Row
        ws.Cell(r, 1).Value = "الإجمالي";
        ws.Cell(r, 1).Style.Font.Bold = true;
        ws.Cell(r, 9).Value = rows.Sum(x => x.Items?.Sum(it => it.Quantity) ?? 0);
        ws.Cell(r, 10).Value = rows.Sum(x => x.Amount);

        for (int col = 1; col <= 13; col++)
        {
            var cell = ws.Cell(r, col);
            cell.Style.Font.Bold = true;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#cfd8dc");
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#eceff1");
            if (col == 10) cell.Style.NumberFormat.Format = "#,##0.00";
        }
        ws.Row(r).Height = 22;
        if (r > 3) ws.Range(2, 1, r - 1, h.Length).SetAutoFilter();

        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"detailed_returns_{from:yyyyMMdd}.xlsx");
    }

    private static string GetCleanReason(string reason)
    {
        if (string.IsNullOrEmpty(reason)) return "—";
        
        string[] validReasons = {
            "منتج تالف",
            "صنف خاطئ",
            "مقاس غير مناسب",
            "جودة غير مرضية",
            "تغيير رأي",
            "سبب آخر"
        };
        
        foreach (var r in validReasons)
        {
            if (reason.Contains(r)) return r;
        }
        
        return reason
            .Replace("Direct Return: ", "")
            .Replace("Direct Return:", "")
            .Trim();
    }

    private IActionResult ExcelUserActivity(List<UserActivityRow> summary, dynamic detail, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        
        // Unified Report Sheet
        var ws = wb.Worksheets.Add(_t.Get("Reports.CashierPerformanceSheet"));
        ws.RightToLeft = true;
        
        ws.Cell(1,1).Value = _t.Get("Reports.CashierPerformanceTitle", from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"));
        ws.Cell(1,1).Style.Font.Bold=true;

        string[] h = { _t.Get("Reports.EmployeeHeader"), _t.Get("Reports.OrderNumberHeader"), _t.Get("Reports.TimeHeader"), _t.Get("Reports.CustomerHeader"), _t.Get("Reports.StatusHeader"), _t.Get("Reports.SkuHeader"), _t.Get("Reports.ProductNameHeader"), _t.Get("Reports.SizeHeader"), _t.Get("Reports.ColorHeader"), _t.Get("Reports.QtyHeader"), _t.Get("Reports.UnitPriceHeader"), _t.Get("Reports.Discount"), _t.Get("Reports.TotalBucket") };
        for (int i = 0; i < h.Length; i++) { 
            var cell = ws.Cell(2, i + 1);
            cell.Value = h[i]; 
            cell.Style.Font.Bold = true; 
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a237e"); 
            cell.Style.Font.FontColor = XLColor.White; 
        }

        int r = 3;
        var detailList = (IEnumerable<dynamic>)detail;

        foreach (var user in summary)
        {
            // Cashier Header Row
            ws.Cell(r, 1).Value = user.UserName;
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#e8eaf6");
            ws.Range(r, 1, r, 5).Merge();
            
            ws.Cell(r, 6).Value = _t.Get("Reports.SummaryLabel");
            ws.Cell(r, 7).Value = _t.Get("Reports.SummaryDetails", user.GrossSales, user.TotalReturns, user.TotalDiscount, user.NetSales);
            ws.Range(r, 7, r, 13).Merge();
            ws.Row(r).Style.Font.Bold = true;
            r++;

            var userOrders = detailList.Where(d => d.SalesPersonId == user.UserId);
            foreach (var d in userOrders)
            {
                var items = (IEnumerable<dynamic>)d.Items;
                if (items == null || !items.Any())
                {
                    ws.Cell(r, 2).Value = d.OrderNumber;
                    ws.Cell(r, 3).Value = ((DateTime)d.CreatedAt).ToString("yyyy-MM-dd HH:mm");
                    ws.Cell(r, 4).Value = d.CustomerName;
                    ws.Cell(r, 5).Value = d.Status;
                    ws.Cell(r, 12).Value = d.DiscountAmount;
                    ws.Cell(r, 13).Value = d.TotalAmount;
                    ws.Cell(r, 12).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(r, 13).Style.NumberFormat.Format = "#,##0.00";
                    r++;
                }
                else
                {
                    foreach(var it in items)
                    {
                        ws.Cell(r, 2).Value = d.OrderNumber;
                        ws.Cell(r, 3).Value = ((DateTime)d.CreatedAt).ToString("yyyy-MM-dd HH:mm");
                        ws.Cell(r, 4).Value = d.CustomerName;
                        ws.Cell(r, 5).Value = d.Status;

                        ws.Cell(r, 6).Value = it.SKU;
                        ws.Cell(r, 7).Value = it.ProductName;
                        ws.Cell(r, 8).Value = it.Size;
                        ws.Cell(r, 9).Value = it.Color;
                        ws.Cell(r, 10).Value = it.Quantity;
                        ws.Cell(r, 11).Value = it.UnitPrice;
                        ws.Cell(r, 12).Value = it.DiscountAmount;
                        ws.Cell(r, 13).Value = it.TotalPrice;

                        for (int c = 11; c <= 13; c++) ws.Cell(r, c).Style.NumberFormat.Format = "#,##0.00";
                        r++;
                    }
                }
            }
            r++; // Space between cashiers
        }

        if (r > 3) ws.Range(2, 1, r - 1, h.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"cashier_performance_{from:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelProductMovement(Product? p, List<ProductMovementLine> movements, dynamic summary, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(_t.Get("Reports.ProductMovementSheet"));
        ws.RightToLeft = true;
        
        var title = p != null ? _t.Get("Reports.ProductMovementTitle", p.NameAr, p.SKU) : _t.Get("Reports.AllProductsMovementTitle");
        ws.Cell(1,1).Value = title;
        ws.Cell(1,1).Style.Font.Bold = true;
        ws.Cell(1,1).Style.Font.FontSize = 14;

        string[] h = { _t.Get("Reports.DateHeader"), _t.Get("Reports.TypeHeader"), _t.Get("Reports.ReferenceHeader"), _t.Get("Reports.ProductNameHeader"), _t.Get("Reports.EntityNameHeader"), _t.Get("Reports.DetailsHeader"), _t.Get("Reports.InHeader"), _t.Get("Reports.OutHeader"), _t.Get("Reports.AmountHeader") };
        for (int i = 0; i < h.Length; i++)
        {
            ws.Cell(3, i + 1).Value = h[i];
            ws.Cell(3, i + 1).Style.Font.Bold = true;
            ws.Cell(3, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1a237e");
            ws.Cell(3, i + 1).Style.Font.FontColor = XLColor.White;
        }

        int r = 4;
        foreach (var m in movements)
        {
            ws.Cell(r, 1).Value = m.Date.ToString("yyyy-MM-dd HH:mm");
            ws.Cell(r, 2).Value = m.Type;
            ws.Cell(r, 3).Value = m.Reference;
            ws.Cell(r, 4).Value = m.ProductName;
            ws.Cell(r, 5).Value = m.EntityName;
            ws.Cell(r, 6).Value = m.Details;
            ws.Cell(r, 7).Value = m.In > 0 ? m.In : 0;
            ws.Cell(r, 8).Value = m.Out > 0 ? m.Out : 0;
            ws.Cell(r, 9).Value = m.Amount;
            ws.Cell(r, 9).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }

        if (r > 4) ws.Range(3, 1, r - 1, h.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();
        var fileName = p != null ? $"product_{p.SKU}_{from:yyyyMMdd}.xlsx" : $"all_products_movement_{from:yyyyMMdd}.xlsx";
        return ExcelResult(wb, fileName);
    }

    private static FileStreamResult ExcelResult(XLWorkbook wb, string filename)
    {
        var stream = new MemoryStream();
        Sportive.API.Utils.ExcelThemeHelper.ApplyElegantTheme(wb);
        wb.SaveAs(stream);
        stream.Position = 0;
        return new FileStreamResult(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet") { FileDownloadName = filename };
    }    // ══════════════════════════════════════════════════════
    // 12.(Stock Aging)
    // GET /api/operationalreports/inventory-aging?days=60
    // ══════════════════════════════════════════════════════
    [HttpGet("inventory-aging")]
    [HttpGet("/api/inventory/aging")] // Alias for legacy/varying client paths
    public async Task<IActionResult> InventoryAging(
        [FromQuery] int     days       = 60,
        [FromQuery] int?    categoryId = null,
        [FromQuery] int?    brandId    = null,
        [FromQuery] string? color      = null,
        [FromQuery] string? size       = null,
        [FromQuery] int?    branchId   = null,
        [FromQuery] bool    excel      = false)
    {
        if (branchId.HasValue)
        {
            var branch = await _db.Branches.FirstOrDefaultAsync(b => b.Id == branchId.Value);
            if (branch != null && branch.LinkedWarehouseId.HasValue)
            {
                var mainBranchId = await _db.Warehouses.Where(w => w.Id == branch.LinkedWarehouseId.Value).Select(w => w.BranchId).FirstOrDefaultAsync();
                if (mainBranchId > 0) branchId = mainBranchId;
            }
        }

        var cutoff = TimeHelper.GetEgyptTime().AddDays(-days);

        // 1. Get filtered product list first
        var query = _db.Products
            .Where(p => p.Status == ProductStatus.Active || p.Status == ProductStatus.OutOfStock || p.Status == ProductStatus.Hidden);

        // 2. Apply Filters (Category/Brand family IDs)
        if (categoryId.HasValue && categoryId > 0)
        {
            var catIds = await FilterHelper.GetCategoryFamilyIds(_db, categoryId);
            query = query.Where(p => p.CategoryId.HasValue && catIds.Contains(p.CategoryId.Value));
        }
        if (brandId.HasValue && brandId > 0)
        {
            var brandIds = await FilterHelper.GetBrandFamilyIds(_db, brandId);
            query = query.Where(p => p.BrandId.HasValue && brandIds.Contains(p.BrandId.Value));
        }
        if (!string.IsNullOrEmpty(color))
        {
            query = query.Where(p => p.Variants.Any(v => v.Color == color || v.ColorAr == color));
        }
        if (!string.IsNullOrEmpty(size))
        {
            query = query.Where(p => p.Variants.Any(v => v.Size == size));
        }

        List<int>? branchWarehouseIds = null;
        if (branchId.HasValue)
        {
            branchWarehouseIds = await _db.Warehouses.Where(w => w.BranchId == branchId.Value).Select(w => w.Id).ToListAsync();
        }

        // 3. Project data and LastSaleDate
        var productsData = await query
            .Select(p => new {
                p.Id, p.NameAr, p.SKU, p.Price, p.CreatedAt,
                TotalStock = branchId.HasValue
                    ? (p.Variants.Any()
                        ? (_db.ProductWarehouseStocks.Where(w => w.ProductVariant.ProductId == p.Id && branchWarehouseIds!.Contains(w.WarehouseId)).Sum(w => (int?)w.Quantity) ?? 0)
                        : (_db.InventoryMovements.Where(m => m.ProductId == p.Id && !m.ProductVariantId.HasValue && m.WarehouseId.HasValue && branchWarehouseIds!.Contains(m.WarehouseId.Value)).Sum(m => (int?)m.Quantity) ?? 0))
                    : p.TotalStock,
                LastSaleDate = _db.InventoryMovements
                    .Where(m => m.ProductId == p.Id && m.Type == InventoryMovementType.Sale && (!branchId.HasValue || (m.WarehouseId.HasValue && branchWarehouseIds!.Contains(m.WarehouseId.Value))))
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => (DateTime?)m.CreatedAt)
                    .FirstOrDefault()
            })
            .Where(p => p.TotalStock > 0)
            .ToListAsync();

        // 4. Filter by Aging Cutoff
        var agingRows = productsData
            .Where(p => (p.LastSaleDate ?? p.CreatedAt) <= cutoff)
            .Select(p => {
                var effectiveDate = p.LastSaleDate ?? p.CreatedAt;
                return new {
                    p.Id, 
                    p.NameAr, 
                    p.SKU, 
                    p.TotalStock, 
                    p.Price,
                    DaysSinceLastSale = p.LastSaleDate == null ? 999 : (int)(TimeHelper.GetEgyptTime() - effectiveDate).TotalDays,
                    Value = p.TotalStock * p.Price
                };
            })
            .OrderByDescending(p => p.DaysSinceLastSale)
            .ToList();

        if (excel)
        {
            var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Slow Moving Items");
            ws.RightToLeft = true;

            var headers = new[] { "الكود / SKU", "اسم الصنف / Product", "المخزون المتوفر / Stock", "مدة الركود (أيام) / Days Idle", "قيمة المخزون / Stock Value" };
            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(1, i + 1).Value = headers[i];
                ws.Cell(1, i + 1).Style.Font.Bold = true;
                ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
            }

            int r = 2;
            foreach (var item in agingRows)
            {
                ws.Cell(r, 1).Value = item.SKU;
                ws.Cell(r, 2).Value = item.NameAr;
                ws.Cell(r, 3).Value = item.TotalStock;
                ws.Cell(r, 4).Value = item.DaysSinceLastSale == 999 ? "لم يُباع أبداً" : item.DaysSinceLastSale.ToString();
                ws.Cell(r, 5).Value = item.Value;
                r++;
            }

            ws.Columns().AdjustToContents();
            var fileName = $"slow_moving_items_{TimeHelper.GetEgyptTime():yyyyMMdd}.xlsx";
            return ExcelResult(wb, fileName);
        }

        return Ok(new {
            days,
            count = agingRows.Count,
            totalValue = agingRows.Sum(r => r.Value),
            rows = agingRows
        });
    }

    // ══════════════════════════════════════════════════════
    // 13. التقرير اليومي الشامل (Daily Summary Report)
    // GET /api/operationalreports/daily-report?fromDate=&toDate=&source=
    // ══════════════════════════════════════════════════════
    [HttpGet("daily-report")]
    [RequirePermission(ModuleKeys.ReportsMain + "," + ModuleKeys.Dashboard + "," + ModuleKeys.Pos + "," + ModuleKeys.InventoryGroup)]
    public async Task<IActionResult> DailyReport(
        [FromQuery] DateTime?    fromDate   = null,
        [FromQuery] DateTime?    toDate     = null,
        [FromQuery] OrderSource? source     = null,
        [FromQuery] int?         branchId   = null,
        [FromQuery] bool         excel      = false)
    {
        var now = TimeHelper.GetEgyptTime();
        var from = (fromDate ?? now).Date.AddHours(TimeHelper.GetBusinessDayEndHour());
        var to   = (toDate ?? now).Date.AddDays(1).AddHours(TimeHelper.GetBusinessDayEndHour()).AddTicks(-1);

        // 🏗️ Mappings Lookup
        var maps = await _db.AccountSystemMappings.ToDictionaryAsync(m => m.Key.ToLower(), m => m.AccountId);
        var posCashAccId = maps.GetValueOrDefault("poscashaccountid");
        var posBankAccId = maps.GetValueOrDefault("posbankaccountid");
        var posVodaAccId = maps.GetValueOrDefault("posvodafoneaccountid");
        var posInstaAccId = maps.GetValueOrDefault("posinstapayaccountid");

        var webCashAccId = maps.GetValueOrDefault("webcashaccountid");
        var webBankAccId = maps.GetValueOrDefault("webbankaccountid");
        var webVodaAccId = maps.GetValueOrDefault("webvodafoneaccountid");
        var webInstaAccId = maps.GetValueOrDefault("webinstapayaccountid");

        var cashAccId = maps.GetValueOrDefault("cashaccountid");

        // 1. Sales Details
        var salesQuery = _db.Orders.AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Payments)
            .Where(o => o.CreatedAt >= from && o.CreatedAt <= to && o.Status != OrderStatus.Cancelled);

        if (branchId.HasValue)
        {
            salesQuery = salesQuery.Where(o => o.BranchId == branchId.Value);
        }

        if (source.HasValue)
        {
            salesQuery = salesQuery.Where(o => o.Source == source.Value);
        }

        var orders = await salesQuery.OrderByDescending(o => o.CreatedAt).ToListAsync();

        var salesRows = orders.Select(o => new DailyReportSale(
            o.Id,
            o.OrderNumber,
            o.CreatedAt,
            o.Customer != null ? o.Customer.FullName : "Walk-in",
            o.Source.ToString(),
            o.PaymentMethod.ToString(),
            o.TotalAmount,
            o.PaidAmount
        )).ToList();

        var totalSalesAmount = salesRows.Sum(s => s.TotalAmount);

        // Calculate immediate order payments
        var immediatePaymentsList = new List<dynamic>();
        decimal orderCash = 0, orderCard = 0, orderVoda = 0, orderInsta = 0, orderCredit = 0;

        foreach (var o in orders)
        {
            if (o.Payments != null && o.Payments.Any())
            {
                foreach (var p in o.Payments)
                {
                    decimal amt = p.Amount;
                    if (p.Method == PaymentMethod.Cash)
                    {
                        orderCash += amt;
                        immediatePaymentsList.Add(new { Reference = o.OrderNumber, Date = o.CreatedAt, CustomerName = o.Customer?.FullName ?? "Walk-in", Amount = amt, Method = "Cash", Description = "دفعة نقدي للطلب" });
                    }
                    else if (p.Method == PaymentMethod.CreditCard || p.Method == PaymentMethod.Bank)
                    {
                        orderCard += amt;
                        immediatePaymentsList.Add(new { Reference = o.OrderNumber, Date = o.CreatedAt, CustomerName = o.Customer?.FullName ?? "Walk-in", Amount = amt, Method = "Card/Bank", Description = "دفعة فيزا للطلب" });
                    }
                    else if (p.Method == PaymentMethod.Vodafone)
                    {
                        orderVoda += amt;
                        immediatePaymentsList.Add(new { Reference = o.OrderNumber, Date = o.CreatedAt, CustomerName = o.Customer?.FullName ?? "Walk-in", Amount = amt, Method = "Vodafone Cash", Description = "دفعة فودافون كاش للطلب" });
                    }
                    else if (p.Method == PaymentMethod.InstaPay)
                    {
                        orderInsta += amt;
                        immediatePaymentsList.Add(new { Reference = o.OrderNumber, Date = o.CreatedAt, CustomerName = o.Customer?.FullName ?? "Walk-in", Amount = amt, Method = "InstaPay", Description = "دفعة إنستاباي للطلب" });
                    }
                    else if (p.Method == PaymentMethod.Credit)
                    {
                        orderCredit += amt;
                    }
                }
                var remaining = o.TotalAmount - o.PaidAmount;
                if (remaining > 0.01M)
                {
                    orderCredit += remaining;
                }
            }
            else
            {
                decimal amt = o.PaidAmount;
                if (amt > 0)
                {
                    if (o.PaymentMethod == PaymentMethod.Cash)
                    {
                        orderCash += amt;
                        immediatePaymentsList.Add(new { Reference = o.OrderNumber, Date = o.CreatedAt, CustomerName = o.Customer?.FullName ?? "Walk-in", Amount = amt, Method = "Cash", Description = "سداد نقدي للطلب" });
                    }
                    else if (o.PaymentMethod == PaymentMethod.CreditCard || o.PaymentMethod == PaymentMethod.Bank)
                    {
                        orderCard += amt;
                        immediatePaymentsList.Add(new { Reference = o.OrderNumber, Date = o.CreatedAt, CustomerName = o.Customer?.FullName ?? "Walk-in", Amount = amt, Method = "Card/Bank", Description = "سداد فيزا للطلب" });
                    }
                    else if (o.PaymentMethod == PaymentMethod.Vodafone)
                    {
                        orderVoda += amt;
                        immediatePaymentsList.Add(new { Reference = o.OrderNumber, Date = o.CreatedAt, CustomerName = o.Customer?.FullName ?? "Walk-in", Amount = amt, Method = "Vodafone Cash", Description = "سداد فودافون كاش للطلب" });
                    }
                    else if (o.PaymentMethod == PaymentMethod.InstaPay)
                    {
                        orderInsta += amt;
                        immediatePaymentsList.Add(new { Reference = o.OrderNumber, Date = o.CreatedAt, CustomerName = o.Customer?.FullName ?? "Walk-in", Amount = amt, Method = "InstaPay", Description = "سداد إنستاباي للطلب" });
                    }
                    else if (o.PaymentMethod == PaymentMethod.Mixed)
                    {
                        orderCash += amt;
                        immediatePaymentsList.Add(new { Reference = o.OrderNumber, Date = o.CreatedAt, CustomerName = o.Customer?.FullName ?? "Walk-in", Amount = amt, Method = "Cash", Description = "سداد نقدي للطلب (متنوع)" });
                    }
                }
                
                if (o.PaymentMethod == PaymentMethod.Credit || o.TotalAmount - o.PaidAmount > 0.01M)
                {
                    orderCredit += (o.TotalAmount - o.PaidAmount);
                }
            }
        }

        // 2. Returns Details (Sales returns)
        var returnsQuery = _db.JournalEntries.AsNoTracking()
            .Include(j => j.Order)
                .ThenInclude(o => o!.Customer)
            .Include(j => j.Lines)
                .ThenInclude(l => l.Account)
            .Where(j => j.Type == JournalEntryType.SalesReturn 
                     && j.EntryDate >= from && j.EntryDate <= to
                     && j.Status == JournalEntryStatus.Posted);

        if (branchId.HasValue)
        {
            returnsQuery = returnsQuery.Where(j => (j.Order != null && j.Order.BranchId == branchId.Value) || j.Lines.Any(l => l.BranchId == branchId.Value));
        }

        if (source.HasValue)
        {
            returnsQuery = returnsQuery.Where(j => j.Order != null && j.Order.Source == source.Value);
        }

        var returnsData = await returnsQuery.ToListAsync();

        var salesReturnAccId = maps.GetValueOrDefault("salesreturnaccountid");
        var vatOutputAccId = maps.GetValueOrDefault("vatoutputaccountid");
        var salesDiscountAccId = maps.GetValueOrDefault("salesdiscountaccountid");

        decimal returnCash = 0, returnCard = 0, returnVoda = 0, returnInsta = 0;
        var returnsRows = new List<DailyReportReturn>();

        foreach (var j in returnsData)
        {
            decimal debitAmt = j.Lines
                .Where(l => l.Debit > 0 && (
                    l.AccountId == salesReturnAccId || 
                    l.AccountId == vatOutputAccId || 
                    (l.Account != null && (l.Account.Code.StartsWith("4103") || l.Account.Code.StartsWith("2105") || l.Account.Code.StartsWith("1202")))
                ))
                .Sum(l => l.Debit);

            decimal discountCredit = j.Lines
                .Where(l => l.Credit > 0 && (l.AccountId == salesDiscountAccId || (l.Account != null && l.Account.Code.StartsWith("4102"))))
                .Sum(l => l.Credit);

            decimal amt = debitAmt - discountCredit;
            
            // Determine return outflow payment method
            var creditedLine = j.Lines.FirstOrDefault(l => l.Credit > 0);
            if (creditedLine != null)
            {
                var accId = creditedLine.AccountId;
                var code = creditedLine.Account?.Code ?? "";
                
                if (accId == posCashAccId || accId == webCashAccId || accId == cashAccId || code.StartsWith("1101"))
                    returnCash += amt;
                else if (accId == posBankAccId || accId == webBankAccId || code.StartsWith("1102"))
                    returnCard += amt;
                else if (accId == posVodaAccId || accId == webVodaAccId || code.StartsWith("110701") || code.StartsWith("110702"))
                    returnVoda += amt;
                else if (accId == posInstaAccId || accId == webInstaAccId || code.StartsWith("110703") || code.StartsWith("110704"))
                    returnInsta += amt;
                else
                    returnCash += amt;
            }
            else
            {
                returnCash += amt;
            }

            returnsRows.Add(new DailyReportReturn(
                j.Reference ?? j.EntryNumber,
                j.EntryDate,
                j.Order != null && j.Order.Customer != null ? j.Order.Customer.FullName : "Walk-in",
                amt,
                j.Description ?? "مرتجع مبيعات"
            ));
        }

        var totalReturnsAmount = returnsRows.Sum(r => r.Amount);

        // 3. Collections Details (Receipt Vouchers)
        var receiptsQuery = _db.ReceiptVouchers.AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.FromAccount)
            .Where(r => r.VoucherDate >= from && r.VoucherDate <= to);

        if (branchId.HasValue)
        {
            receiptsQuery = receiptsQuery.Where(r => r.BranchId == branchId.Value);
        }

        if (source.HasValue)
        {
            receiptsQuery = receiptsQuery.Where(r => r.CostCenter == source.Value);
        }

        var receiptsData = await receiptsQuery.OrderByDescending(r => r.VoucherDate).ToListAsync();

        decimal receiptCash = 0, receiptCard = 0, receiptVoda = 0, receiptInsta = 0;
        var receiptsRows = new List<DailyReportCollection>();

        foreach (var r in receiptsData)
        {
            decimal amt = r.Amount;
            string methodLabel = "Cash";
            if (r.PaymentMethod == VoucherPaymentMethod.Cash) { receiptCash += amt; methodLabel = "Cash"; }
            else if (r.PaymentMethod == VoucherPaymentMethod.BankTransfer || r.PaymentMethod == VoucherPaymentMethod.Check) { receiptCard += amt; methodLabel = "Card/Bank"; }
            else if (r.PaymentMethod == VoucherPaymentMethod.Vodafone) { receiptVoda += amt; methodLabel = "Vodafone Cash"; }
            else if (r.PaymentMethod == VoucherPaymentMethod.InstaPay) { receiptInsta += amt; methodLabel = "InstaPay"; }

            receiptsRows.Add(new DailyReportCollection(
                r.VoucherNumber,
                r.VoucherDate,
                r.Customer != null ? r.Customer.FullName : (r.FromAccount != null ? r.FromAccount.NameAr : "Walk-in"),
                amt,
                methodLabel,
                r.Reference,
                r.Description ?? "سند قبض / تحصيل عميل"
            ));
        }

        var totalReceiptsAmount = receiptsRows.Sum(r => r.Amount);
        var totalCollectionsAmount = totalReceiptsAmount + (orderCash + orderCard + orderVoda + orderInsta);

        // 4. Settlements Details (Supplier Payments)
        var settlementsQuery = _db.PaymentVouchers.AsNoTracking()
            .Include(pv => pv.Supplier)
            .Where(pv => pv.VoucherDate >= from && pv.VoucherDate <= to && pv.SupplierId != null);

        if (branchId.HasValue)
        {
            settlementsQuery = settlementsQuery.Where(pv => pv.BranchId == branchId.Value);
        }

        if (source.HasValue)
        {
            settlementsQuery = settlementsQuery.Where(pv => pv.CostCenter == source.Value);
        }

        var settlementsData = await settlementsQuery.OrderByDescending(pv => pv.VoucherDate).ToListAsync();

        decimal settlementCash = 0, settlementCard = 0, settlementVoda = 0, settlementInsta = 0;
        var settlementsRows = new List<DailyReportSettlement>();

        foreach (var pv in settlementsData)
        {
            decimal amt = pv.Amount;
            string methodLabel = "Cash";
            if (pv.PaymentMethod == VoucherPaymentMethod.Cash) { settlementCash += amt; methodLabel = "Cash"; }
            else if (pv.PaymentMethod == VoucherPaymentMethod.BankTransfer || pv.PaymentMethod == VoucherPaymentMethod.Check) { settlementCard += amt; methodLabel = "Card/Bank"; }
            else if (pv.PaymentMethod == VoucherPaymentMethod.Vodafone) { settlementVoda += amt; methodLabel = "Vodafone Cash"; }
            else if (pv.PaymentMethod == VoucherPaymentMethod.InstaPay) { settlementInsta += amt; methodLabel = "InstaPay"; }

            settlementsRows.Add(new DailyReportSettlement(
                pv.VoucherNumber,
                pv.VoucherDate,
                pv.Supplier != null ? pv.Supplier.Name : "N/A",
                amt,
                methodLabel,
                pv.Reference,
                pv.Description ?? "سداد مورد"
            ));
        }

        var totalSettlementsAmount = settlementsRows.Sum(s => s.Amount);

        // 5. Expenses Details
        var expensesQuery = _db.PaymentVouchers.AsNoTracking()
            .Include(pv => pv.ToAccount)
            .Where(pv => pv.VoucherDate >= from && pv.VoucherDate <= to && pv.SupplierId == null);

        if (branchId.HasValue)
        {
            expensesQuery = expensesQuery.Where(pv => pv.BranchId == branchId.Value);
        }

        if (source.HasValue)
        {
            expensesQuery = expensesQuery.Where(pv => pv.CostCenter == source.Value);
        }

        var expensesData = await expensesQuery.OrderByDescending(pv => pv.VoucherDate).ToListAsync();

        decimal expenseCash = 0, expenseCard = 0, expenseVoda = 0, expenseInsta = 0;
        var expensesRows = new List<DailyReportExpense>();

        foreach (var pv in expensesData)
        {
            decimal amt = pv.Amount;
            string methodLabel = "Cash";
            if (pv.PaymentMethod == VoucherPaymentMethod.Cash) { expenseCash += amt; methodLabel = "Cash"; }
            else if (pv.PaymentMethod == VoucherPaymentMethod.BankTransfer || pv.PaymentMethod == VoucherPaymentMethod.Check) { expenseCard += amt; methodLabel = "Card/Bank"; }
            else if (pv.PaymentMethod == VoucherPaymentMethod.Vodafone) { expenseVoda += amt; methodLabel = "Vodafone Cash"; }
            else if (pv.PaymentMethod == VoucherPaymentMethod.InstaPay) { expenseInsta += amt; methodLabel = "InstaPay"; }

            expensesRows.Add(new DailyReportExpense(
                pv.VoucherNumber,
                pv.VoucherDate,
                amt,
                pv.ToAccount != null ? pv.ToAccount.NameAr : "مصروفات عامة",
                methodLabel,
                pv.Reference,
                pv.Description ?? "سند صرف مصروفات"
            ));
        }

        var totalExpensesAmount = expensesRows.Sum(e => e.Amount);

        // Show only receipt vouchers in collections details to avoid duplication with sales
        var allCollectionsRows = receiptsRows;

        // 6. Payment Methods Reconciliation
        var paymentMethodsSummary = new List<DailyReportPaymentMethod>
        {
            new DailyReportPaymentMethod(
                "Cash",
                "نقدي",
                "Cash",
                orderCash + receiptCash,
                returnCash + settlementCash + expenseCash,
                (orderCash + receiptCash) - (returnCash + settlementCash + expenseCash)
            ),
            new DailyReportPaymentMethod(
                "Card",
                "فيزا / بنك",
                "Card/Bank",
                orderCard + receiptCard,
                returnCard + settlementCard + expenseCard,
                (orderCard + receiptCard) - (returnCard + settlementCard + expenseCard)
            ),
            new DailyReportPaymentMethod(
                "Vodafone",
                "فودافون كاش",
                "Vodafone Cash",
                orderVoda + receiptVoda,
                returnVoda + settlementVoda + expenseVoda,
                (orderVoda + receiptVoda) - (returnVoda + settlementVoda + expenseVoda)
            ),
            new DailyReportPaymentMethod(
                "InstaPay",
                "إنستاباي",
                "InstaPay",
                orderInsta + receiptInsta,
                returnInsta + settlementInsta + expenseInsta,
                (orderInsta + receiptInsta) - (returnInsta + settlementInsta + expenseInsta)
            ),
            new DailyReportPaymentMethod(
                "Credit",
                "آجل / دين",
                "Credit / Deferred",
                // Inflow = مبيعات على الآجل (amount not yet collected from customers)
                orderCredit,
                // Outflow = 0 (تُحسب عند السداد كتحصيل)
                0m,
                orderCredit
            )
        };

        salesDiscountAccId = maps.GetValueOrDefault("salesdiscountaccountid");
        decimal periodDiscountReturned = 0;
        if (salesDiscountAccId != null)
        {
            var discountReturnsQuery = _db.JournalEntries
                .Where(e => e.Type == JournalEntryType.SalesReturn && e.EntryDate >= from && e.EntryDate <= to && e.Status == JournalEntryStatus.Posted);

            if (branchId.HasValue)
            {
                discountReturnsQuery = discountReturnsQuery.Where(e => (e.Order != null && e.Order.BranchId == branchId.Value) || e.Lines.Any(l => l.BranchId == branchId.Value));
            }

            if (source.HasValue)
            {
                discountReturnsQuery = discountReturnsQuery.Where(e => e.CostCenter == source.Value);
            }

            periodDiscountReturned = await discountReturnsQuery
                .SelectMany(e => e.Lines)
                .Where(l => l.Credit > 0 && l.AccountId == salesDiscountAccId)
                .SumAsync(l => (decimal?)l.Credit) ?? 0;
        }

        decimal totalDiscounts = orders.Sum(o => o.DiscountAmount + o.TemporalDiscount) - periodDiscountReturned;
        var summary = new DailyReportSummary(
            totalSalesAmount,
            totalReturnsAmount,
            totalCollectionsAmount,
            totalSettlementsAmount,
            totalExpensesAmount,
            orderCredit,
            totalCollectionsAmount - totalSettlementsAmount - totalExpensesAmount - totalReturnsAmount,
            totalDiscounts
        );

        var result = new {
            from,
            to,
            summary,
            paymentMethods = paymentMethodsSummary,
            sales = salesRows,
            returns = returnsRows,
            collections = allCollectionsRows,
            settlements = settlementsRows,
            expenses = expensesRows
        };

        if (excel)
        {
            return ExcelDailyReport(
                summary,
                paymentMethodsSummary,
                salesRows,
                returnsRows,
                allCollectionsRows,
                settlementsRows,
                expensesRows,
                from, to);
        }

        return Ok(result);
    }

    private IActionResult ExcelDailyReport(
        DailyReportSummary summary,
        IEnumerable<DailyReportPaymentMethod> paymentMethods,
        IEnumerable<DailyReportSale> sales,
        IEnumerable<DailyReportReturn> returns,
        IEnumerable<DailyReportCollection> collections,
        IEnumerable<DailyReportSettlement> settlements,
        IEnumerable<DailyReportExpense> expenses,
        DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();

        // ── Sheet 1: الملخص اليومي (Daily Summary) ───────────────────
        var wsSum = wb.Worksheets.Add("الملخص اليومي");
        wsSum.RightToLeft = true;

        // Title
        wsSum.Cell(1, 1).Value = $"التقرير اليومي الشامل — من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";
        wsSum.Cell(1, 1).Style.Font.Bold = true;
        wsSum.Cell(1, 1).Style.Font.FontSize = 13;
        wsSum.Range(1, 1, 1, 4).Merge();

        // Section 1: المؤشرات المالية (Financial Metrics)
        wsSum.Cell(3, 1).Value = "المؤشر المالي";
        wsSum.Cell(3, 2).Value = "القيمة";
        wsSum.Range(3, 1, 3, 2).Style.Font.Bold = true;
        wsSum.Range(3, 1, 3, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#1a237e");
        wsSum.Range(3, 1, 3, 2).Style.Font.FontColor = XLColor.White;

        var metrics = new (string Name, decimal Value)[]
        {
            ("المبيعات قبل الخصم", (decimal)(summary.totalSales + summary.totalDiscounts)),
            ("إجمالي الخصومات", (decimal)summary.totalDiscounts),
            ("إجمالي المبيعات",      (decimal)summary.totalSales),
            ("إجمالي المرتجعات",     (decimal)summary.totalReturns),
            ("إجمالي المقبوضات والتحصيلات", (decimal)summary.totalCollections),
            ("إجمالي المدفوعات للموردين",  (decimal)summary.totalSettlements),
            ("إجمالي المصروفات",     (decimal)summary.totalExpenses),
            ("إجمالي المبيعات الآجلة", (decimal)summary.totalCredit),
            ("صافي التدفق النقدي",   (decimal)summary.netCashflow)
        };

        int r = 4;
        foreach (var m in metrics)
        {
            wsSum.Cell(r, 1).Value = m.Name;
            wsSum.Cell(r, 2).Value = m.Value;
            wsSum.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            if (m.Name.Contains("صافي") || m.Name.Contains("إجمالي"))
            {
                wsSum.Cell(r, 1).Style.Font.Bold = true;
                wsSum.Cell(r, 2).Style.Font.Bold = true;
            }
            wsSum.Cell(r, 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            wsSum.Cell(r, 2).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            wsSum.Cell(r, 1).Style.Border.OutsideBorderColor = XLColor.FromHtml("#cfd8dc");
            wsSum.Cell(r, 2).Style.Border.OutsideBorderColor = XLColor.FromHtml("#cfd8dc");
            r++;
        }

        // Section 2: تسوية طرق الدفع (Payment Methods)
        r += 2;
        wsSum.Cell(r, 1).Value = "تسوية طرق الدفع";
        wsSum.Cell(r, 1).Style.Font.Bold = true;
        wsSum.Cell(r, 1).Style.Font.FontSize = 11;
        wsSum.Range(r, 1, r, 4).Merge();
        r++;

        string[] pmHeaders = { "طريقة الدفع", "المقبوضات (الداخل)", "المدفوعات (الخارج)", "الصافي" };
        for (int i = 0; i < pmHeaders.Length; i++)
        {
            var cell = wsSum.Cell(r, i + 1);
            cell.Value = pmHeaders[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#37474f");
            cell.Style.Font.FontColor = XLColor.White;
        }
        r++;

        foreach (var pm in paymentMethods)
        {
            wsSum.Cell(r, 1).Value = pm.NameAr;
            wsSum.Cell(r, 2).Value = pm.Inflow;
            wsSum.Cell(r, 3).Value = pm.Outflow;
            wsSum.Cell(r, 4).Value = pm.Net;

            for (int col = 2; col <= 4; col++)
            {
                wsSum.Cell(r, col).Style.NumberFormat.Format = "#,##0.00";
            }

            for (int col = 1; col <= 4; col++)
            {
                wsSum.Cell(r, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                wsSum.Cell(r, col).Style.Border.OutsideBorderColor = XLColor.FromHtml("#cfd8dc");
            }
            r++;
        }
        wsSum.Columns().AdjustToContents();

        // ── Sheet 2: تفاصيل المبيعات (Sales Details) ──────────────────
        var wsSales = wb.Worksheets.Add("تفاصيل المبيعات");
        wsSales.RightToLeft = true;
        wsSales.Cell(1, 1).Value = "تفاصيل المبيعات اليومية";
        wsSales.Cell(1, 1).Style.Font.Bold = true;
        wsSales.Cell(1, 1).Style.Font.FontSize = 12;

        string[] salesH = { "رقم الطلب", "التاريخ", "العميل", "القناة", "طريقة الدفع", "إجمالي الطلب", "المدفوع" };
        for (int i = 0; i < salesH.Length; i++)
        {
            var cell = wsSales.Cell(3, i + 1);
            cell.Value = salesH[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a237e");
            cell.Style.Font.FontColor = XLColor.White;
        }

        int sr = 4;
        foreach (var s in sales)
        {
            wsSales.Cell(sr, 1).Value = s.OrderNumber;
            wsSales.Cell(sr, 2).Value = s.Date.ToString("yyyy-MM-dd HH:mm");
            wsSales.Cell(sr, 3).Value = s.CustomerName;
            wsSales.Cell(sr, 4).Value = s.Source;
            wsSales.Cell(sr, 5).Value = s.PaymentMethod;
            wsSales.Cell(sr, 6).Value = s.TotalAmount;
            wsSales.Cell(sr, 7).Value = s.PaidAmount;

            wsSales.Cell(sr, 6).Style.NumberFormat.Format = "#,##0.00";
            wsSales.Cell(sr, 7).Style.NumberFormat.Format = "#,##0.00";

            for (int col = 1; col <= 7; col++)
            {
                wsSales.Cell(sr, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                wsSales.Cell(sr, col).Style.Border.OutsideBorderColor = XLColor.FromHtml("#cfd8dc");
            }
            sr++;
        }
        if (sr > 4) wsSales.Range(3, 1, sr - 1, salesH.Length).SetAutoFilter();
        wsSales.Columns().AdjustToContents();

        // ── Sheet 3: تفاصيل المرتجعات (Returns Details) ────────────────
        var wsReturns = wb.Worksheets.Add("تفاصيل المرتجعات");
        wsReturns.RightToLeft = true;
        wsReturns.Cell(1, 1).Value = "تفاصيل مرتجعات المبيعات اليومية";
        wsReturns.Cell(1, 1).Style.Font.Bold = true;
        wsReturns.Cell(1, 1).Style.Font.FontSize = 12;

        string[] returnsH = { "المرجع", "التاريخ", "العميل", "القيمة", "البيان / السبب" };
        for (int i = 0; i < returnsH.Length; i++)
        {
            var cell = wsReturns.Cell(3, i + 1);
            cell.Value = returnsH[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#c62828");
            cell.Style.Font.FontColor = XLColor.White;
        }

        int rr = 4;
        foreach (var ret in returns)
        {
            wsReturns.Cell(rr, 1).Value = ret.Reference;
            wsReturns.Cell(rr, 2).Value = ret.Date.ToString("yyyy-MM-dd HH:mm");
            wsReturns.Cell(rr, 3).Value = ret.CustomerName;
            wsReturns.Cell(rr, 4).Value = ret.Amount;
            wsReturns.Cell(rr, 5).Value = GetCleanReason(ret.Description);

            wsReturns.Cell(rr, 4).Style.NumberFormat.Format = "#,##0.00";

            for (int col = 1; col <= 5; col++)
            {
                wsReturns.Cell(rr, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                wsReturns.Cell(rr, col).Style.Border.OutsideBorderColor = XLColor.FromHtml("#cfd8dc");
            }
            rr++;
        }
        if (rr > 4) wsReturns.Range(3, 1, rr - 1, returnsH.Length).SetAutoFilter();
        wsReturns.Columns().AdjustToContents();

        // ── Sheet 4: تفاصيل التحصيلات (Collections Details) ──────────────
        var wsColl = wb.Worksheets.Add("تفاصيل التحصيلات");
        wsColl.RightToLeft = true;
        wsColl.Cell(1, 1).Value = "تفاصيل المقبوضات والتحصيلات اليومية";
        wsColl.Cell(1, 1).Style.Font.Bold = true;
        wsColl.Cell(1, 1).Style.Font.FontSize = 12;

        string[] collH = { "رقم السند / الطلب", "التاريخ", "العميل / الحساب", "القيمة", "طريقة التحصيل", "المرجع", "البيان" };
        for (int i = 0; i < collH.Length; i++)
        {
            var cell = wsColl.Cell(3, i + 1);
            cell.Value = collH[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1b5e20");
            cell.Style.Font.FontColor = XLColor.White;
        }

        int cr = 4;
        foreach (var c in collections)
        {
            wsColl.Cell(cr, 1).Value = c.VoucherNumber;
            wsColl.Cell(cr, 2).Value = c.Date.ToString("yyyy-MM-dd HH:mm");
            wsColl.Cell(cr, 3).Value = c.CustomerName;
            wsColl.Cell(cr, 4).Value = c.Amount;
            wsColl.Cell(cr, 5).Value = c.PaymentMethod;
            wsColl.Cell(cr, 6).Value = c.Reference;
            wsColl.Cell(cr, 7).Value = c.Description;

            wsColl.Cell(cr, 4).Style.NumberFormat.Format = "#,##0.00";

            for (int col = 1; col <= 7; col++)
            {
                wsColl.Cell(cr, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                wsColl.Cell(cr, col).Style.Border.OutsideBorderColor = XLColor.FromHtml("#cfd8dc");
            }
            cr++;
        }
        if (cr > 4) wsColl.Range(3, 1, cr - 1, collH.Length).SetAutoFilter();
        wsColl.Columns().AdjustToContents();

        // ── Sheet 5: سداد الموردين (Settlements Details) ─────────────────
        var wsSett = wb.Worksheets.Add("سداد الموردين");
        wsSett.RightToLeft = true;
        wsSett.Cell(1, 1).Value = "تفاصيل مدفوعات الموردين اليومية";
        wsSett.Cell(1, 1).Style.Font.Bold = true;
        wsSett.Cell(1, 1).Style.Font.FontSize = 12;

        string[] settH = { "رقم السند", "التاريخ", "المورد", "القيمة", "طريقة الدفع", "المرجع", "البيان" };
        for (int i = 0; i < settH.Length; i++)
        {
            var cell = wsSett.Cell(3, i + 1);
            cell.Value = settH[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#b71c1c");
            cell.Style.Font.FontColor = XLColor.White;
        }

        int srow = 4;
        foreach (var s in settlements)
        {
            wsSett.Cell(srow, 1).Value = s.VoucherNumber;
            wsSett.Cell(srow, 2).Value = s.Date.ToString("yyyy-MM-dd HH:mm");
            wsSett.Cell(srow, 3).Value = s.SupplierName;
            wsSett.Cell(srow, 4).Value = s.Amount;
            wsSett.Cell(srow, 5).Value = s.PaymentMethod;
            wsSett.Cell(srow, 6).Value = s.Reference;
            wsSett.Cell(srow, 7).Value = s.Description;

            wsSett.Cell(srow, 4).Style.NumberFormat.Format = "#,##0.00";

            for (int col = 1; col <= 7; col++)
            {
                wsSett.Cell(srow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                wsSett.Cell(srow, col).Style.Border.OutsideBorderColor = XLColor.FromHtml("#cfd8dc");
            }
            srow++;
        }
        if (srow > 4) wsSett.Range(3, 1, srow - 1, settH.Length).SetAutoFilter();
        wsSett.Columns().AdjustToContents();

        // ── Sheet 6: تفاصيل المصروفات (Expenses Details) ─────────────────
        var wsExp = wb.Worksheets.Add("تفاصيل المصروفات");
        wsExp.RightToLeft = true;
        wsExp.Cell(1, 1).Value = "تفاصيل سندات صرف المصروفات اليومية";
        wsExp.Cell(1, 1).Style.Font.Bold = true;
        wsExp.Cell(1, 1).Style.Font.FontSize = 12;

        string[] expH = { "رقم السند", "التاريخ", "بند المصروف", "القيمة", "طريقة الدفع", "المرجع", "البيان" };
        for (int i = 0; i < expH.Length; i++)
        {
            var cell = wsExp.Cell(3, i + 1);
            cell.Value = expH[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#4a148c");
            cell.Style.Font.FontColor = XLColor.White;
        }

        int er = 4;
        foreach (var e in expenses)
        {
            wsExp.Cell(er, 1).Value = e.VoucherNumber;
            wsExp.Cell(er, 2).Value = e.Date.ToString("yyyy-MM-dd HH:mm");
            wsExp.Cell(er, 3).Value = e.ExpenseCategory;
            wsExp.Cell(er, 4).Value = e.Amount;
            wsExp.Cell(er, 5).Value = e.PaymentMethod;
            wsExp.Cell(er, 6).Value = e.Reference;
            wsExp.Cell(er, 7).Value = e.Description;

            wsExp.Cell(er, 4).Style.NumberFormat.Format = "#,##0.00";

            for (int col = 1; col <= 7; col++)
            {
                wsExp.Cell(er, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                wsExp.Cell(er, col).Style.Border.OutsideBorderColor = XLColor.FromHtml("#cfd8dc");
            }
            er++;
        }
        if (er > 4) wsExp.Range(3, 1, er - 1, expH.Length).SetAutoFilter();
        wsExp.Columns().AdjustToContents();

        return ExcelResult(wb, $"daily_report_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }

    // ══════════════════════════════════════════════════════
    // 14. تقرير ربحية الفواتير (Invoice Profitability Report)
    // GET /api/operationalreports/invoice-profitability
    // ══════════════════════════════════════════════════════
    [HttpGet("invoice-profitability")]
    [RequirePermission(ModuleKeys.Profitability)]
    public async Task<IActionResult> InvoiceProfitability(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] bool excel = false,
        [FromQuery] int? branchId = null)
    {
        var now = TimeHelper.GetEgyptTime();
        var from = (fromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var to = (toDate ?? now).Date.AddDays(1).AddTicks(-1);

        var ordersQuery = _db.Orders.AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .Where(o => o.CreatedAt >= from && o.CreatedAt <= to && o.Status != OrderStatus.Cancelled);

        if (branchId.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.BranchId == branchId.Value);
        }

        var orders = await ordersQuery.ToListAsync();

        var invoiceRows = new List<Sportive.API.DTOs.Reports.InvoiceProfitabilityDto>();

        foreach (var order in orders)
        {
            decimal revenue = order.TotalAmount - order.TotalVatAmount; // net revenue without tax
            decimal cost = 0;

            foreach (var item in order.Items)
            {
                decimal itemCost = item.Product?.CostPrice ?? 0;
                cost += itemCost * item.Quantity; // Full quantity sold (returns handled as separate rows)
            }

            decimal profit = revenue - cost;
            decimal margin = revenue > 0 ? (profit / revenue) * 100 : 0;

            invoiceRows.Add(new Sportive.API.DTOs.Reports.InvoiceProfitabilityDto {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                Date = order.CreatedAt,
                CustomerName = order.Customer?.FullName ?? "Walk-in",
                Revenue = revenue,
                Cost = cost,
                Profit = profit,
                Margin = margin
            });
        }

        // Fetch SalesReturn journal entries in the period to represent all kinds of returns (including direct and partial ones)
        var maps = await _db.AccountSystemMappings.ToDictionaryAsync(m => m.Key.ToLower(), m => m.AccountId);
        var salesReturnAccId = maps.GetValueOrDefault("salesreturnaccountid");
        var salesDiscountAccId = maps.GetValueOrDefault("salesdiscountaccountid");
        var cogsAccId = maps.GetValueOrDefault("costofgoodssoldaccountid");

        if (salesReturnAccId.HasValue)
        {
            var returnEntriesQ = _db.JournalEntries.AsNoTracking()
                .Include(j => j.Lines)
                    .ThenInclude(l => l.Account)
                .Where(j => j.Type == JournalEntryType.SalesReturn 
                         && j.EntryDate >= from && j.EntryDate <= to
                         && j.Status == JournalEntryStatus.Posted);

            if (branchId.HasValue)
            {
                returnEntriesQ = returnEntriesQ.Where(j => (j.Order != null && j.Order.BranchId == branchId.Value) || j.Lines.Any(l => l.BranchId == branchId.Value));
            }
            var returnEntries = await returnEntriesQ.ToListAsync();

            var customerIds = returnEntries.SelectMany(e => e.Lines)
                .Where(l => l.CustomerId.HasValue)
                .Select(l => l.CustomerId!.Value)
                .Distinct()
                .ToList();
            var customersMap = await _db.Customers.AsNoTracking()
                .Where(c => customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id);

            foreach (var j in returnEntries)
            {
                decimal salesReturnDebit = j.Lines
                    .Where(l => l.Debit > 0 && l.AccountId == salesReturnAccId)
                    .Sum(l => l.Debit);

                decimal discountCredit = salesDiscountAccId.HasValue
                    ? j.Lines
                        .Where(l => l.Credit > 0 && l.AccountId == salesDiscountAccId)
                        .Sum(l => l.Credit)
                    : 0;

                decimal netReturn = salesReturnDebit - discountCredit;

                decimal costReturn = cogsAccId.HasValue
                    ? j.Lines
                        .Where(l => l.Credit > 0 && l.AccountId == cogsAccId)
                        .Sum(l => l.Credit)
                    : 0;

                int? customerId = j.Lines.FirstOrDefault(l => l.CustomerId.HasValue)?.CustomerId;
                string customerName = "Walk-in";
                if (customerId.HasValue && customersMap.TryGetValue(customerId.Value, out var cust))
                {
                    customerName = cust.FullName;
                }
                else
                {
                    // Fallback to parsing from description or default to Walk-in
                    customerName = j.Description?.Contains(" - ") == true 
                        ? j.Description.Split(" - ").Last() 
                        : "Walk-in";
                }

                decimal profit = -netReturn - (-costReturn);
                decimal margin = netReturn > 0 ? (profit / -netReturn) * 100 : 0;

                invoiceRows.Add(new Sportive.API.DTOs.Reports.InvoiceProfitabilityDto {
                    OrderId = j.OrderId ?? 0,
                    OrderNumber = j.Reference ?? j.EntryNumber,
                    Date = j.EntryDate,
                    CustomerName = customerName,
                    Revenue = -netReturn, // negative revenue
                    Cost = -costReturn,   // negative cost
                    Profit = profit,
                    Margin = margin
                });
            }
        }

        invoiceRows = invoiceRows.OrderByDescending(x => x.Date).ToList();

        var totalInvoices = invoiceRows.Count;
        var totalRevenue = invoiceRows.Sum(x => x.Revenue);
        var totalCost = invoiceRows.Sum(x => x.Cost);
        var totalProfit = totalRevenue - totalCost;
        var averageMargin = totalRevenue > 0 ? (totalProfit / totalRevenue) * 100 : 0;

        if (excel)
        {
            return ExcelInvoiceProfitability(invoiceRows, totalInvoices, totalRevenue, totalCost, totalProfit, averageMargin, from, to);
        }

        pageSize = Math.Clamp(pageSize, 1, 100);
        var paginatedRows = invoiceRows.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Ok(new {
            FromDate = from,
            ToDate = to,
            TotalInvoices = totalInvoices,
            TotalRevenue = totalRevenue,
            TotalCost = totalCost,
            TotalProfit = totalProfit,
            AverageMargin = averageMargin,
            Invoices = paginatedRows,
            Pagination = new {
                TotalCount = totalInvoices,
                PageSize = pageSize,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(totalInvoices / (double)pageSize)
            }
        });
    }

    private IActionResult ExcelInvoiceProfitability(
        List<Sportive.API.DTOs.Reports.InvoiceProfitabilityDto> rows,
        int totalInvoices, decimal totalRevenue, decimal totalCost, decimal totalProfit, decimal averageMargin,
        DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();

        // --- Sheet 1: تفاصيل ربحية الفواتير ----------------------------
        var ws = wb.Worksheets.Add("ربحية الفواتير");
        ws.RightToLeft = true;

        // Title
        ws.Cell(1, 1).Value = $"تقرير ربحية الفواتير — من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Range(1, 1, 1, 7).Merge();

        // Headers
        string[] h = {
            "رقم الفاتورة", "التاريخ", "العميل",
            "الإيراد", "التكلفة", "الربح", "هامش الربح %"
        };
        for (int c = 0; c < h.Length; c++)
        {
            var cell = ws.Cell(2, c + 1);
            cell.Value = h[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f3460");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int r = 3;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.OrderNumber;
            ws.Cell(r, 2).Value = row.Date.ToString("yyyy-MM-dd HH:mm");
            ws.Cell(r, 3).Value = row.CustomerName;
            ws.Cell(r, 4).Value = row.Revenue;
            ws.Cell(r, 5).Value = row.Cost;
            ws.Cell(r, 6).Value = row.Profit;
            ws.Cell(r, 7).Value = row.Margin;

            // تنسيق الأرقام
            foreach (int col in new[] { 4, 5, 6 })
            {
                ws.Cell(r, col).Style.NumberFormat.Format = "#,##0.00";
            }

            ws.Cell(r, 7).Style.NumberFormat.Format = "0.0\"%\"";

            // تلوين حسب الهامش
            var bg = row.Margin >= 30 ? XLColor.FromHtml("#e8f5e9")
                   : row.Margin >= 10 ? XLColor.FromHtml("#fff8e1")
                   : XLColor.FromHtml("#ffebee");
            ws.Cell(r, 7).Style.Fill.BackgroundColor = bg;
            r++;
        }

        // Totals row
        ws.Cell(r, 1).Value = "الإجمالي";
        ws.Cell(r, 1).Style.Font.Bold = true;
        ws.Cell(r, 4).Value = totalRevenue;
        ws.Cell(r, 5).Value = totalCost;
        ws.Cell(r, 6).Value = totalProfit;
        ws.Cell(r, 7).Value = averageMargin;

        for (int col = 4; col <= 7; col++)
        {
            ws.Cell(r, col).Style.Font.Bold = true;
            if (col < 7) 
                ws.Cell(r, col).Style.NumberFormat.Format = "#,##0.00";
            else 
                ws.Cell(r, col).Style.NumberFormat.Format = "0.0\"%\"";
        }
        ws.Row(r).Style.Fill.BackgroundColor = XLColor.FromHtml("#e8f5e9");

        if (r > 3) ws.Range(2, 1, r - 1, h.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();

        // --- Sheet 2: الملخص التنفيذي ----------------------------
        var ws2 = wb.Worksheets.Add("الملخص التنفيذي");
        ws2.RightToLeft = true;

        ws2.Cell(1, 1).Value = "الملخص التنفيذي";
        ws2.Cell(1, 1).Style.Font.Bold = true;
        ws2.Cell(1, 1).Style.Font.FontSize = 14;

        var summaryRows = new (string Label, object Value)[]
        {
            ("إجمالي عدد الفواتير",       totalInvoices),
            ("إجمالي الإيرادات",          totalRevenue),
            ("إجمالي التكاليف",           totalCost),
            ("إجمالي الأرباح",            totalProfit),
            ("متوسط هامش الربح %",       averageMargin)
        };

        int sr = 3;
        foreach (var (label, val) in summaryRows)
        {
            ws2.Cell(sr, 1).Value = label;
            ws2.Cell(sr, 2).Value = XLCellValue.FromObject(val);
            if (val is decimal d && (label.Contains("الإيرادات") || label.Contains("التكاليف") || label.Contains("الأرباح")))
                ws2.Cell(sr, 2).Style.NumberFormat.Format = "#,##0.00";
            if (label.Contains("%"))
                ws2.Cell(sr, 2).Style.NumberFormat.Format = "0.0\"%\"";
            sr++;
        }
        ws2.Columns().AdjustToContents();

        return ExcelResult(wb, $"invoice_profitability_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }

    private string TranslateMovementType(InventoryMovementType type) => type switch
    {
        InventoryMovementType.Purchase => _t.Get("Reports.PurchasePlus"),
        InventoryMovementType.Sale => _t.Get("Reports.SaleMinus"),
        InventoryMovementType.ReturnOut => _t.Get("Reports.ReturnOutMinus"),
        InventoryMovementType.ReturnIn => _t.Get("Reports.ReturnInPlus"),
        InventoryMovementType.Adjustment => _t.Get("Reports.AdjustmentHeader"),
        InventoryMovementType.Audit => _t.Get("Reports.AuditHeader"),
        InventoryMovementType.TransferIn => _t.Get("Reports.TransferInPlus"),
        InventoryMovementType.TransferOut => _t.Get("Reports.TransferOutMinus"),
        InventoryMovementType.OpeningBalance => _t.Get("Reports.OpeningBalancePlus"),
        _ => type.ToString()
    };
    [HttpGet("partners-comprehensive")]
    public async Task<IActionResult> PartnersComprehensiveReport(
        [FromQuery] DateTime? date = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] int? branchId = null)
    {
        var targetDate = (date ?? TimeHelper.GetEgyptBusinessDayStart()).Date;
        // fromDate defaults to targetDate (single-day mode) if not supplied
        var fromDateVal = fromDate.HasValue ? fromDate.Value.Date : targetDate;

        var dayStart = fromDateVal.AddHours(TimeHelper.GetBusinessDayEndHour());
        var dayEnd   = targetDate.AddDays(1).AddHours(TimeHelper.GetBusinessDayEndHour()).AddTicks(-1);

        // Month Start & End
        var monthStart = new DateTime(targetDate.Year, targetDate.Month, 1).AddHours(TimeHelper.GetBusinessDayEndHour());
        var endHour = TimeHelper.GetBusinessDayEndHour();

        var salesReturnAccId = await _db.AccountSystemMappings
            .Where(m => m.Key == "salesReturnAccountID")
            .Select(m => (int?)m.AccountId)
            .FirstOrDefaultAsync();

        var salesDiscountAccId = await _db.AccountSystemMappings
            .Where(m => m.Key == "salesDiscountAccountID")
            .Select(m => (int?)m.AccountId)
            .FirstOrDefaultAsync();

        var discountReturnAccId = await _db.Accounts
            .Where(a => a.Code == "410101")
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync();

        var cogsAccId = await _db.AccountSystemMappings
            .Where(m => m.Key == "costOfGoodsSoldAccountID")
            .Select(m => (int?)m.AccountId)
            .FirstOrDefaultAsync();

        // --- 1. Sales (Strict Accounting) ---
        var salesQuery = _db.Orders.AsNoTracking().Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned);
        if (branchId.HasValue) salesQuery = salesQuery.Where(o => o.BranchId == branchId.Value);

        var posDailySales = await salesQuery.Where(o => o.Source == OrderSource.POS && o.CreatedAt >= dayStart && o.CreatedAt <= dayEnd).SumAsync(o => o.TotalAmount);
        var posTotalSales = await salesQuery.Where(o => o.Source == OrderSource.POS && o.CreatedAt <= dayEnd).SumAsync(o => o.TotalAmount);
        
        var webDailySales = await salesQuery.Where(o => o.Source == OrderSource.Website && o.CreatedAt >= dayStart && o.CreatedAt <= dayEnd).SumAsync(o => o.TotalAmount);
        var webTotalSales = await salesQuery.Where(o => o.Source == OrderSource.Website && o.CreatedAt <= dayEnd).SumAsync(o => o.TotalAmount);

        // MTD Gross Sales
        var posMtdSales = await salesQuery.Where(o => o.Source == OrderSource.POS && o.CreatedAt >= monthStart && o.CreatedAt <= dayEnd).SumAsync(o => o.TotalAmount);
        var webMtdSales = await salesQuery.Where(o => o.Source == OrderSource.Website && o.CreatedAt >= monthStart && o.CreatedAt <= dayEnd).SumAsync(o => o.TotalAmount);

        // Subtract Returns (from JournalEntries because OrderItems returned might be partial)
        var salesReturnsQuery = _db.JournalEntries.AsNoTracking().Where(j => j.Type == JournalEntryType.SalesReturn);
        if (branchId.HasValue) salesReturnsQuery = salesReturnsQuery.Where(j => j.Lines.Any(l => l.BranchId == branchId.Value));

        // Returns usually debit Sales and credit Customer/Cash. To get gross return including VAT, we sum the Credit on Asset accounts.
        var dailyPosReturns = await salesReturnsQuery.Where(j => j.EntryDate >= dayStart && j.EntryDate <= dayEnd && j.Order != null && j.Order.Source == OrderSource.POS)
            .SelectMany(j => j.Lines).Where(l => l.Account.Type == AccountType.Asset).SumAsync(l => l.Credit);
        var totalPosReturns = await salesReturnsQuery.Where(j => j.EntryDate <= dayEnd && j.Order != null && j.Order.Source == OrderSource.POS)
            .SelectMany(j => j.Lines).Where(l => l.Account.Type == AccountType.Asset).SumAsync(l => l.Credit);
            
        var dailyWebReturns = await salesReturnsQuery.Where(j => j.EntryDate >= dayStart && j.EntryDate <= dayEnd && j.Order != null && j.Order.Source == OrderSource.Website)
            .SelectMany(j => j.Lines).Where(l => l.Account.Type == AccountType.Asset).SumAsync(l => l.Credit);
        var totalWebReturns = await salesReturnsQuery.Where(j => j.EntryDate <= dayEnd && j.Order != null && j.Order.Source == OrderSource.Website)
            .SelectMany(j => j.Lines).Where(l => l.Account.Type == AccountType.Asset).SumAsync(l => l.Credit);

        // MTD Returns
        var mtdPosReturns = await salesReturnsQuery.Where(j => j.EntryDate >= monthStart && j.EntryDate <= dayEnd && j.Order != null && j.Order.Source == OrderSource.POS)
            .SelectMany(j => j.Lines).Where(l => l.Account.Type == AccountType.Asset).SumAsync(l => l.Credit);
        var mtdWebReturns = await salesReturnsQuery.Where(j => j.EntryDate >= monthStart && j.EntryDate <= dayEnd && j.Order != null && j.Order.Source == OrderSource.Website)
            .SelectMany(j => j.Lines).Where(l => l.Account.Type == AccountType.Asset).SumAsync(l => l.Credit);

        posDailySales -= dailyPosReturns;
        posTotalSales -= totalPosReturns;
        webDailySales -= dailyWebReturns;
        webTotalSales -= totalWebReturns;

        var mtdNetSales = (posMtdSales - mtdPosReturns) + (webMtdSales - mtdWebReturns);

        // --- 2. Cash Flow (Collections & Payments from Cash Accounts) ---
        // Exclude receivables (1107), employee advances (1105), inventory differences (110106), and clearing/audit accounts
        var cashAccTypes = new[] { "1101", "1102", "1103" };
        var cashAccounts = await _db.Accounts.AsNoTracking()
            .Where(a => cashAccTypes.Any(c => a.Code.StartsWith(c)) 
                     && a.IsLeaf 
                     && a.Code != "110106" // Exclude فرق جرد المخزون
                     && !a.NameAr.Contains("جرد") 
                     && !a.NameAr.Contains("مخزون") 
                     && !a.NameAr.Contains("عجز") 
                     && !a.NameAr.Contains("زيادة") 
                     && !a.NameAr.Contains("تقفيل"))
            .Select(a => a.Id)
            .ToListAsync();

        var jlQuery = _db.JournalLines.AsNoTracking().Where(l => cashAccounts.Contains(l.AccountId));
        if (branchId.HasValue) jlQuery = jlQuery.Where(l => l.BranchId == branchId.Value);

        var dailyCollections = await jlQuery.Where(l => l.JournalEntry.Type != JournalEntryType.Manual && l.JournalEntry.EntryDate >= dayStart && l.JournalEntry.EntryDate <= dayEnd).SumAsync(l => l.Debit);
        var totalCollections = await jlQuery.Where(l => l.JournalEntry.Type != JournalEntryType.Manual && l.JournalEntry.EntryDate <= dayEnd).SumAsync(l => l.Debit);
        var mtdCollections = await jlQuery.Where(l => l.JournalEntry.Type != JournalEntryType.Manual && l.JournalEntry.EntryDate >= monthStart && l.JournalEntry.EntryDate <= dayEnd).SumAsync(l => l.Debit);

        // Redefining dailyExpenses and totalExpenses as cash outflows (credits to cash accounts)
        var dailyExpenses = await jlQuery.Where(l => l.JournalEntry.Type != JournalEntryType.OpeningBalance && l.JournalEntry.EntryDate >= dayStart && l.JournalEntry.EntryDate <= dayEnd).SumAsync(l => l.Credit);
        var totalExpenses = await jlQuery.Where(l => l.JournalEntry.Type != JournalEntryType.OpeningBalance && l.JournalEntry.EntryDate <= dayEnd).SumAsync(l => l.Credit);
        var mtdCashOutflows = await jlQuery.Where(l => l.JournalEntry.Type != JournalEntryType.OpeningBalance && l.JournalEntry.EntryDate >= monthStart && l.JournalEntry.EntryDate <= dayEnd).SumAsync(l => l.Credit);

        var dailyNetCashFlow = await jlQuery.Where(l => l.JournalEntry.EntryDate >= dayStart && l.JournalEntry.EntryDate <= dayEnd).SumAsync(l => l.Debit - l.Credit);
        var totalNetCashFlow = await jlQuery.Where(l => l.JournalEntry.EntryDate <= dayEnd).SumAsync(l => l.Debit - l.Credit);

        // --- 3. True Expenses (For Expense Breakdown) ---
        var expenseAccounts = await _db.Accounts.AsNoTracking().Where(a => a.Type == AccountType.Expense).Select(a => a.Id).ToListAsync();
        var expQuery = _db.JournalLines.AsNoTracking().Where(l => expenseAccounts.Contains(l.AccountId));
        if (branchId.HasValue) expQuery = expQuery.Where(l => l.BranchId == branchId.Value);

        var expensesBreakdownQuery = await expQuery.Where(l => l.JournalEntry.EntryDate >= dayStart && l.JournalEntry.EntryDate <= dayEnd)
            .GroupBy(l => l.Account.NameAr)
            .Select(g => new { Category = g.Key, Amount = g.Sum(l => l.Debit - l.Credit) })
            .ToListAsync();
        var expensesBreakdown = expensesBreakdownQuery.Select(x => new PartnersReportExpenseCategory(x.Category, x.Amount)).ToList();

        // --- 4. Detailed Cash Outflows Table ---
        var userNames = await _db.Users.AsNoTracking()
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        var outflowsQuery = _db.JournalLines.AsNoTracking()
            .Include(l => l.JournalEntry)
            .Include(l => l.Account)
            .Include(l => l.Branch)
            .Where(l => cashAccounts.Contains(l.AccountId) 
                        && l.Credit > 0 
                        && l.JournalEntry.EntryDate >= dayStart 
                        && l.JournalEntry.EntryDate <= dayEnd
                        && l.JournalEntry.Type != JournalEntryType.OpeningBalance
                        && (string.IsNullOrEmpty(l.JournalEntry.Reference) || !l.JournalEntry.Reference.StartsWith("SHIFT-CLOSE")));
        if (branchId.HasValue) outflowsQuery = outflowsQuery.Where(l => l.BranchId == branchId.Value);

        var outflowsList = await outflowsQuery.ToListAsync();
        var outflowEntryIds = outflowsList.Select(l => l.JournalEntryId).Distinct().ToList();
        var outflowDebitedLines = await _db.JournalLines.AsNoTracking()
            .Include(l => l.Account)
            .Where(l => outflowEntryIds.Contains(l.JournalEntryId) && l.Debit > 0)
            .ToListAsync();

        var cashOutflows = outflowsList.Select(l => {
            var entryDebits = outflowDebitedLines.Where(d => d.JournalEntryId == l.JournalEntryId).ToList();

            bool isSupplier = entryDebits.Any(d => d.Account.Code.StartsWith("2101"));
            bool isExpense = entryDebits.Any(d => d.Account.Type == AccountType.Expense 
                                               || d.Account.Code.StartsWith("5")
                                               || (salesReturnAccId.HasValue && d.AccountId == salesReturnAccId.Value)
                                               || (salesDiscountAccId.HasValue && d.AccountId == salesDiscountAccId.Value)
                                               || (discountReturnAccId.HasValue && d.AccountId == discountReturnAccId.Value));

            string category = "Accounts";
            if (isSupplier)
            {
                category = "Supplier";
            }
            else if (isExpense)
            {
                category = "Operating";
            }

            return new PartnersReportCashOutflow(
                l.JournalEntryId,
                l.JournalEntry.Reference ?? l.JournalEntry.EntryNumber,
                l.JournalEntry.EntryDate,
                l.JournalEntry.Description ?? "",
                l.Account.NameAr,
                l.Credit,
                userNames.TryGetValue(l.JournalEntry.CreatedByUserId ?? "", out var name) ? name : (l.JournalEntry.CreatedByUserId ?? ""),
                l.Branch?.Name ?? "",
                category
            );
        }).ToList();

        // --- 5. Grouped Operational Balances ---
        var balanceAccounts = await _db.Accounts.AsNoTracking()
            .Include(a => a.Branch)
            .Where(a => a.IsLeaf && (
                a.Code.StartsWith("1101") || 
                a.Code.StartsWith("1102") || 
                a.Code.StartsWith("1103") ||
                a.NameAr.Contains("جاري الشريك") ||
                a.NameAr.Contains("جاري الشركاء") ||
                a.Code.StartsWith("3105") ||
                a.Code.StartsWith("3205")
            ))
            .ToListAsync();

        // Exclude inventory (1106), clearing/audit accounts, employee advances, receivables, and deficits/surpluses
        balanceAccounts = balanceAccounts.Where(a => 
            a.Code != "1106" && 
            a.Code != "110104" && // Exclude العجز والزيادة
            a.Code != "110106" && // Exclude فرق جرد المخزون
            !a.Code.StartsWith("1105") && // Exclude سلف الموظفين
            !a.Code.StartsWith("1107") && // Exclude العملاء
            !a.NameAr.Contains("مخزون") &&
            !a.NameAr.Contains("جرد") &&
            !a.NameAr.Contains("عجز") &&
            !a.NameAr.Contains("زيادة") &&
            (!a.NameAr.Contains("تقفيل") || a.Code == "110105")
        ).ToList();

        var accInfos = new List<PartnersReportAccountInfo>();
        foreach (var acc in balanceAccounts)
        {
            var lq = _db.JournalLines.AsNoTracking().Where(l => l.AccountId == acc.Id);
            if (branchId.HasValue) lq = lq.Where(l => l.BranchId == branchId.Value);

            bool isCreditNature = acc.Nature == AccountNature.Credit || acc.Type == AccountType.Liability || acc.Type == AccountType.Equity || acc.NameAr.Contains("جاري") || acc.Code.StartsWith("3105") || acc.Code.StartsWith("2105");

            decimal dailyNet = 0;
            decimal cumulative = 0;

            if (isCreditNature)
            {
                dailyNet = await lq.Where(l => l.JournalEntry.EntryDate >= dayStart && l.JournalEntry.EntryDate <= dayEnd).SumAsync(l => l.Credit - l.Debit);
                cumulative = await lq.Where(l => l.JournalEntry.EntryDate <= dayEnd).SumAsync(l => l.Credit - l.Debit);
                cumulative += acc.OpeningBalance;
            }
            else
            {
                dailyNet = await lq.Where(l => l.JournalEntry.EntryDate >= dayStart && l.JournalEntry.EntryDate <= dayEnd).SumAsync(l => l.Debit - l.Credit);
                cumulative = await lq.Where(l => l.JournalEntry.EntryDate <= dayEnd).SumAsync(l => l.Debit - l.Credit);
                cumulative += acc.OpeningBalance;
            }

            // Determine category
            string category = "GeneralCash";
            if (acc.NameAr.Contains("جاري") || acc.NameAr.Contains("شريك") || acc.Code.StartsWith("3105") || acc.Code.StartsWith("3205"))
            {
                category = "PartnerCurrent";
            }
            else if (acc.BranchId != null)
            {
                if (acc.Code.StartsWith("1102"))
                {
                    category = "BranchNetworks";
                }
                else
                {
                    category = "BranchCash";
                }
            }
            else
            {
                if (acc.Code.StartsWith("1102"))
                {
                    category = "BankAndClosures";
                }
                else
                {
                    category = "GeneralCash";
                }
            }

            accInfos.Add(new PartnersReportAccountInfo(
                acc.Id, 
                acc.NameAr, 
                dailyNet, 
                cumulative,
                category,
                acc.BranchId,
                acc.Branch?.Name
            ));
        }

        // --- 6. Debts and Inventory (Real-Time Cost Valuation) ---
        // Use accounting ledger directly for accurate, authoritative balances
        // Account 1107 = العملاء (Asset, Debit-natured): Debit - Credit > 0 means customers owe us (لنا)
        var customerAccountIds = await _db.Accounts.AsNoTracking()
            .Where(a => a.Code.StartsWith("1107") && a.IsLeaf)
            .Select(a => a.Id)
            .ToListAsync();
        var customerLedgerNet = await _db.JournalLines.AsNoTracking()
            .Where(l => customerAccountIds.Contains(l.AccountId))
            .SumAsync(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0m;
        // Positive = لنا عند العملاء (receivables), Negative = علينا للعملاء (credit balances)
        var totalCustomerDebt = customerLedgerNet;

        // Account 2101 = الموردون (Liability, Credit-natured): Credit - Debit > 0 means we owe suppliers (علينا)
        var supplierAccountIds = await _db.Accounts.AsNoTracking()
            .Where(a => a.Code.StartsWith("2101") && a.IsLeaf)
            .Select(a => a.Id)
            .ToListAsync();
        var supplierLedgerNet = await _db.JournalLines.AsNoTracking()
            .Where(l => supplierAccountIds.Contains(l.AccountId))
            .SumAsync(l => (decimal?)l.Credit - (decimal?)l.Debit) ?? 0m;
        // Positive = علينا للموردين (payables), Negative = لنا عند الموردين (debit/prepaid balances)
        var totalSupplierDebt = supplierLedgerNet;

        // Account 210201 = الزكاة المستحقة (Liability, Credit-natured): رصيد الزكاة الحالي
        var zakatAccount = await _db.Accounts.AsNoTracking()
            .Where(a => a.Code == "210201")
            .FirstOrDefaultAsync();
        decimal zakatAccountBalance = 0;
        string zakatAccountName = "الزكاة المستحقة";
        int? zakatAccountId = null;
        if (zakatAccount != null)
        {
            zakatAccountId = zakatAccount.Id;
            zakatAccountName = zakatAccount.NameAr;
            zakatAccountBalance = await _db.JournalLines.AsNoTracking()
                .Where(l => l.AccountId == zakatAccount.Id)
                .SumAsync(l => (decimal?)l.Credit - (decimal?)l.Debit) ?? 0m;
            zakatAccountBalance += zakatAccount.OpeningBalance;
        }

        // Dynamic cost valuation using inventory movements up to dayEnd
        var movementsQuery = _db.InventoryMovements.AsNoTracking().Where(m => m.CreatedAt <= dayEnd);
        if (branchId.HasValue) movementsQuery = movementsQuery.Where(m => m.BranchId == branchId.Value);

        var productStocks = await movementsQuery
            .GroupBy(m => m.ProductId)
            .Select(g => new { ProductId = g.Key, Stock = g.Sum(m => m.Quantity) })
            .ToListAsync();

        var productsCost = await _db.Products.AsNoTracking()
            .Select(p => new { p.Id, p.CostPrice })
            .ToDictionaryAsync(p => p.Id, p => p.CostPrice);

        decimal totalInventoryValue = 0;
        foreach (var item in productStocks)
        {
            if (item.ProductId.HasValue && productsCost.TryGetValue(item.ProductId.Value, out var costPrice))
            {
                totalInventoryValue += item.Stock * costPrice.GetValueOrDefault();
            }
        }

        // --- 7. Split Mixed Payment Methods ---
        var dayOrders = await salesQuery
            .Where(o => o.CreatedAt >= dayStart && o.CreatedAt <= dayEnd)
            .Select(o => new { o.Id, o.PaymentMethod, o.TotalAmount })
            .ToListAsync();

        var paymentTotals = new Dictionary<PaymentMethod, decimal>();
        var mixedOrderIds = dayOrders.Where(o => o.PaymentMethod == PaymentMethod.Mixed).Select(o => o.Id).ToList();

        var mixedPayments = await _db.OrderPayments.AsNoTracking()
            .Where(p => mixedOrderIds.Contains(p.OrderId))
            .ToListAsync();

        var paymentsByOrder = mixedPayments.GroupBy(p => p.OrderId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var order in dayOrders)
        {
            if (order.PaymentMethod == PaymentMethod.Mixed)
            {
                if (paymentsByOrder.TryGetValue(order.Id, out var lines) && lines.Count > 0)
                {
                    var nonMixedLines = lines.Where(l => l.Method != PaymentMethod.Mixed).ToList();
                    if (nonMixedLines.Count > 0)
                    {
                        foreach (var line in nonMixedLines)
                        {
                            paymentTotals[line.Method] = paymentTotals.GetValueOrDefault(line.Method) + line.Amount;
                        }
                    }
                    else
                    {
                        var mixedSum = lines.Sum(l => l.Amount);
                        var amountToUse = mixedSum > 0 ? mixedSum : order.TotalAmount;
                        paymentTotals[PaymentMethod.Cash] = paymentTotals.GetValueOrDefault(PaymentMethod.Cash) + amountToUse;
                    }
                }
                else
                {
                    paymentTotals[PaymentMethod.Cash] = paymentTotals.GetValueOrDefault(PaymentMethod.Cash) + order.TotalAmount;
                }
            }
            else
            {
                paymentTotals[order.PaymentMethod] = paymentTotals.GetValueOrDefault(order.PaymentMethod) + order.TotalAmount;
            }
        }

        var salesByPayment = paymentTotals.Select(x => new PartnersReportPaymentMethodSale(x.Key.ToString(), x.Value)).ToList();

        // --- 8. Trends and Charts ---
        var sevenDaysAgo = targetDate.AddDays(-6).Date.AddHours(TimeHelper.GetBusinessDayEndHour());
        var salesTrendQuery = await salesQuery.Where(o => o.CreatedAt >= sevenDaysAgo && o.CreatedAt <= dayEnd)
            .GroupBy(o => o.CreatedAt.AddHours(-endHour).Date)
            .Select(g => new { Date = g.Key, Amount = g.Sum(o => o.TotalAmount) })
            .ToListAsync();
        var salesTrend = salesTrendQuery.Select(x => new PartnersReportSalesTrend(x.Date, x.Amount)).ToList();

        // Calculate income/expenses by month for the period (Income Statement General Ledger logic)
        int monthDiff = (targetDate.Year - fromDateVal.Year) * 12 + targetDate.Month - fromDateVal.Month;
        DateTime startPeriodCal;
        int numMonths;
        if (monthDiff <= 1) // same month or adjacent months, let's show last 12 months up to targetDate
        {
            startPeriodCal = new DateTime(targetDate.Year, targetDate.Month, 1).AddMonths(-11);
            numMonths = 12;
        }
        else
        {
            startPeriodCal = new DateTime(fromDateVal.Year, fromDateVal.Month, 1);
            numMonths = monthDiff + 1;
        }

        var startPeriod = startPeriodCal.AddHours(endHour);
        var endPeriod = targetDate.AddDays(1).AddHours(endHour).AddTicks(-1);

        // Query monthly revenue from general ledger (Income Statement logic)
        var monthlyRevenueQuery = _db.JournalLines.AsNoTracking()
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate >= startPeriod
                     && l.JournalEntry.EntryDate <= endPeriod
                     && (l.Account.Type == AccountType.Revenue || l.Account.Code.StartsWith("4")));
        if (branchId.HasValue)
        {
            monthlyRevenueQuery = monthlyRevenueQuery.Where(l => l.BranchId == branchId.Value);
        }
        var monthlyRevenue = await monthlyRevenueQuery
            .GroupBy(l => new { 
                Year = l.JournalEntry.EntryDate.AddHours(-endHour).Year, 
                Month = l.JournalEntry.EntryDate.AddHours(-endHour).Month 
            })
            .Select(g => new {
                g.Key.Year,
                g.Key.Month,
                Revenue = g.Sum(l => l.Credit - l.Debit)
            })
            .ToListAsync();

        // Query monthly expenses from general ledger (Income Statement logic)
        var monthlyExpensesQuery = _db.JournalLines.AsNoTracking()
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate >= startPeriod
                     && l.JournalEntry.EntryDate <= endPeriod
                     && (l.Account.Type == AccountType.Expense || l.Account.Code.StartsWith("5"))
                     && !l.Account.Code.StartsWith("4"));
        if (branchId.HasValue)
        {
            monthlyExpensesQuery = monthlyExpensesQuery.Where(l => l.BranchId == branchId.Value);
        }
        var monthlyExpenses = await monthlyExpensesQuery
            .GroupBy(l => new { 
                Year = l.JournalEntry.EntryDate.AddHours(-endHour).Year, 
                Month = l.JournalEntry.EntryDate.AddHours(-endHour).Month 
            })
            .Select(g => new {
                g.Key.Year,
                g.Key.Month,
                Expenses = g.Sum(l => l.Debit - l.Credit)
            })
            .ToListAsync();

        var incomeTrend = Enumerable.Range(0, numMonths).Select(i => {
            var date = startPeriodCal.AddMonths(i);
            var revData = monthlyRevenue.FirstOrDefault(r => r.Year == date.Year && r.Month == date.Month);
            var expData = monthlyExpenses.FirstOrDefault(e => e.Year == date.Year && e.Month == date.Month);
            return new PartnersReportIncomeTrendItem(
                date.ToString("MM/yyyy"),
                revData?.Revenue ?? 0,
                expData?.Expenses ?? 0
            );
        }).ToList();

        // --- 9. Split Expenses: Daily Expenses vs Accounts Outflows ---

        var expenseLinesQuery = _db.JournalLines.AsNoTracking()
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted
                     && (l.Account.Type == AccountType.Expense 
                         || l.Account.Code.StartsWith("5")
                         || (salesReturnAccId.HasValue && l.AccountId == salesReturnAccId.Value)
                         || (salesDiscountAccId.HasValue && l.AccountId == salesDiscountAccId.Value)
                         || (discountReturnAccId.HasValue && l.AccountId == discountReturnAccId.Value))
                     && !l.Account.Code.StartsWith("5101")
                     && !(cogsAccId.HasValue && l.AccountId == cogsAccId.Value)
                     && (string.IsNullOrEmpty(l.JournalEntry.Reference) || !l.JournalEntry.Reference.StartsWith("SHIFT-CLOSE")));

        if (branchId.HasValue)
        {
            expenseLinesQuery = expenseLinesQuery.Where(l => l.BranchId == branchId.Value);
        }

        // Base operational expenses (income statement expenses, returns, discounts)
        var dailyIncomeExpensesBase = await expenseLinesQuery
            .Where(l => l.JournalEntry.EntryDate >= dayStart && l.JournalEntry.EntryDate <= dayEnd)
            .SumAsync(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0m;

        var mtdIncomeExpensesBase = await expenseLinesQuery
            .Where(l => l.JournalEntry.EntryDate >= monthStart && l.JournalEntry.EntryDate <= dayEnd)
            .SumAsync(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0m;

        // Query cash outflow lines (credits to cash/bank accounts) filtered by branch if selected
        var dailyCashOutflowQuery = _db.JournalLines.AsNoTracking()
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted
                     && cashAccounts.Contains(l.AccountId)
                     && l.Credit > 0
                     && l.JournalEntry.EntryDate >= dayStart 
                     && l.JournalEntry.EntryDate <= dayEnd
                     && (string.IsNullOrEmpty(l.JournalEntry.Reference) || !l.JournalEntry.Reference.StartsWith("SHIFT-CLOSE")));

        var mtdCashOutflowQuery = _db.JournalLines.AsNoTracking()
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted
                     && cashAccounts.Contains(l.AccountId)
                     && l.Credit > 0
                     && l.JournalEntry.EntryDate >= monthStart 
                     && l.JournalEntry.EntryDate <= dayEnd
                     && (string.IsNullOrEmpty(l.JournalEntry.Reference) || !l.JournalEntry.Reference.StartsWith("SHIFT-CLOSE")));

        if (branchId.HasValue)
        {
            dailyCashOutflowQuery = dailyCashOutflowQuery.Where(l => l.BranchId == branchId.Value);
            mtdCashOutflowQuery = mtdCashOutflowQuery.Where(l => l.BranchId == branchId.Value);
        }

        var dailyCashOutflowLines = await dailyCashOutflowQuery
            .Include(l => l.JournalEntry)
            .Include(l => l.Account)
            .ToListAsync();

        var mtdCashOutflowLines = await mtdCashOutflowQuery
            .Include(l => l.JournalEntry)
            .Include(l => l.Account)
            .ToListAsync();

        // Daily categorization
        var dailyOutflowEntryIds = dailyCashOutflowLines.Select(l => l.JournalEntryId).Distinct().ToList();
        var dailyDebitedLines = await _db.JournalLines.AsNoTracking()
            .Include(l => l.Account)
            .Where(l => dailyOutflowEntryIds.Contains(l.JournalEntryId) && l.Debit > 0)
            .ToListAsync();

        decimal dailySupplierPayments = 0;
        decimal dailyAccountOutflows = 0;

        foreach (var outflowLine in dailyCashOutflowLines)
        {
            var entryDebits = dailyDebitedLines.Where(l => l.JournalEntryId == outflowLine.JournalEntryId).ToList();

            // 1. Skip internal transfers (debits to another cash/bank/closure account starting with 1101, 1102, 1103)
            bool isInternalTransfer = entryDebits.Any(d => d.Account.Code.StartsWith("1101") 
                                                        || d.Account.Code.StartsWith("1102") 
                                                        || d.Account.Code.StartsWith("1103"));
            if (isInternalTransfer)
            {
                continue;
            }

            // 2. Supplier Payment (debits a supplier account starting with 2101)
            bool isSupplierPayment = entryDebits.Any(d => d.Account.Code.StartsWith("2101"));
            if (isSupplierPayment)
            {
                dailySupplierPayments += outflowLine.Credit;
            }
            else
            {
                // 3. Other Balance Sheet Outflows (exclude if already accounted for in general operational expenses base)
                bool isExpense = entryDebits.Any(d => d.Account.Type == AccountType.Expense 
                                                   || d.Account.Code.StartsWith("5")
                                                   || (salesReturnAccId.HasValue && d.AccountId == salesReturnAccId.Value)
                                                   || (salesDiscountAccId.HasValue && d.AccountId == salesDiscountAccId.Value)
                                                   || (discountReturnAccId.HasValue && d.AccountId == discountReturnAccId.Value));
                if (!isExpense)
                {
                    dailyAccountOutflows += outflowLine.Credit;
                }
            }
        }

        // MTD categorization
        var mtdOutflowEntryIds = mtdCashOutflowLines.Select(l => l.JournalEntryId).Distinct().ToList();
        var mtdDebitedLines = await _db.JournalLines.AsNoTracking()
            .Include(l => l.Account)
            .Where(l => mtdOutflowEntryIds.Contains(l.JournalEntryId) && l.Debit > 0)
            .ToListAsync();

        decimal mtdSupplierPayments = 0;
        decimal mtdAccountOutflows = 0;

        foreach (var outflowLine in mtdCashOutflowLines)
        {
            var entryDebits = mtdDebitedLines.Where(l => l.JournalEntryId == outflowLine.JournalEntryId).ToList();

            bool isInternalTransfer = entryDebits.Any(d => d.Account.Code.StartsWith("1101") 
                                                        || d.Account.Code.StartsWith("1102") 
                                                        || d.Account.Code.StartsWith("1103"));
            if (isInternalTransfer)
            {
                continue;
            }

            bool isSupplierPayment = entryDebits.Any(d => d.Account.Code.StartsWith("2101"));
            if (isSupplierPayment)
            {
                mtdSupplierPayments += outflowLine.Credit;
            }
            else
            {
                bool isExpense = entryDebits.Any(d => d.Account.Type == AccountType.Expense 
                                                   || d.Account.Code.StartsWith("5")
                                                   || (salesReturnAccId.HasValue && d.AccountId == salesReturnAccId.Value)
                                                   || (salesDiscountAccId.HasValue && d.AccountId == salesDiscountAccId.Value)
                                                   || (discountReturnAccId.HasValue && d.AccountId == discountReturnAccId.Value));
                if (!isExpense)
                {
                    mtdAccountOutflows += outflowLine.Credit;
                }
            }
        }

        // Final results: Supplier payments are excluded completely from both cards as requested
        var dailyIncomeExpenses = dailyIncomeExpensesBase;
        var mtdIncomeExpenses = mtdIncomeExpensesBase;

        // ── NEW: Detailed Expense Lines (مصروفات اليوم التفصيلية) ──────────────
        // Any JournalLine on Expense accounts + sales return account for the selected date range
        var detailedExpenseQuery = _db.JournalLines.AsNoTracking()
            .Include(l => l.JournalEntry)
            .Include(l => l.Account)
            .Where(l =>
                l.JournalEntry.EntryDate >= dayStart && l.JournalEntry.EntryDate <= dayEnd
                && l.JournalEntry.Status == JournalEntryStatus.Posted
                && l.Debit > 0
                && (l.Account.Type == AccountType.Expense
                    || l.Account.Code.StartsWith("5")
                    || (salesReturnAccId.HasValue  && l.AccountId == salesReturnAccId.Value)
                    || (salesDiscountAccId.HasValue && l.AccountId == salesDiscountAccId.Value)
                    || (discountReturnAccId.HasValue && l.AccountId == discountReturnAccId.Value))
                && !(cogsAccId.HasValue && l.AccountId == cogsAccId.Value)
                && !l.Account.Code.StartsWith("5101")
                && (string.IsNullOrEmpty(l.JournalEntry.Reference) || !l.JournalEntry.Reference.StartsWith("SHIFT-CLOSE")));

        if (branchId.HasValue)
            detailedExpenseQuery = detailedExpenseQuery.Where(l => l.BranchId == branchId.Value);

        var detailedExpenseLines = await detailedExpenseQuery
            .OrderByDescending(l => l.JournalEntry.EntryDate)
            .ToListAsync();

        // Load parent account names for better display
        var expenseParentIds = detailedExpenseLines.Where(l => l.Account.ParentId.HasValue).Select(l => l.Account.ParentId!.Value).Distinct().ToList();
        var expenseParentNames = await _db.Accounts.AsNoTracking()
            .Where(a => expenseParentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.NameAr);

        var detailedExpenses = detailedExpenseLines.Select(l => new {
            journalEntryId = l.JournalEntryId,
            orderId      = l.JournalEntry.OrderId,
            purchaseInvoiceId = l.JournalEntry.PurchaseInvoiceId,
            reference   = l.JournalEntry.Reference ?? l.JournalEntry.EntryNumber,
            date        = l.JournalEntry.EntryDate,
            accountCode = l.Account.Code,
            accountName = l.Account.NameAr,
            parentAccountName = l.Account.ParentId.HasValue && expenseParentNames.TryGetValue(l.Account.ParentId.Value, out var pn) ? pn : (string?)null,
            entryType   = l.JournalEntry.Type.ToString(),
            debit       = l.Debit,
            credit      = l.Credit,
            description = !string.IsNullOrWhiteSpace(l.JournalEntry.Description) ? l.JournalEntry.Description : l.Description
        }).ToList();

        // ── NEW: Account Movements (مصروفات الحسابات اليومية) ─────────────────
        // Any JournalLine on ALL accounts EXCEPT expense accounts and sales return account
        var allowedAccountIds = balanceAccounts.Select(a => a.Id).ToList();

        var detailedAccountMovQuery = _db.JournalLines.AsNoTracking()
            .Include(l => l.JournalEntry)
            .Include(l => l.Account)
            .Where(l =>
                l.JournalEntry.EntryDate >= dayStart && l.JournalEntry.EntryDate <= dayEnd
                && l.JournalEntry.Status == JournalEntryStatus.Posted
                && (allowedAccountIds.Contains(l.AccountId) || l.Account.NameAr.Contains("أرباح") || l.Account.NameAr.Contains("ارباح") || l.Account.NameAr.Contains("مبقاة"))
                && (string.IsNullOrEmpty(l.JournalEntry.Reference) || !l.JournalEntry.Reference.StartsWith("SHIFT-CLOSE")));

        if (branchId.HasValue)
            detailedAccountMovQuery = detailedAccountMovQuery.Where(l => l.BranchId == branchId.Value);

        var detailedAccountMovLines = await detailedAccountMovQuery
            .OrderByDescending(l => l.JournalEntry.EntryDate)
            .ToListAsync();

        var detailedAccountMovements = detailedAccountMovLines.Select(l => new {
            journalEntryId = l.JournalEntryId,
            orderId      = l.JournalEntry.OrderId,
            purchaseInvoiceId = l.JournalEntry.PurchaseInvoiceId,
            reference   = l.JournalEntry.Reference ?? l.JournalEntry.EntryNumber,
            date        = l.JournalEntry.EntryDate,
            accountCode = l.Account.Code,
            accountName = l.Account.NameAr,
            accountType = l.Account.Type.ToString(),
            entryType   = l.JournalEntry.Type.ToString(),
            debit       = l.Debit,
            credit      = l.Credit,
            description = !string.IsNullOrWhiteSpace(l.JournalEntry.Description) ? l.JournalEntry.Description : (!string.IsNullOrWhiteSpace(l.Description) ? l.Description : l.Account.NameAr)
        }).ToList();

        var summary = new PartnersReportSummary(
            posDailySales, posTotalSales,
            webDailySales, webTotalSales,
            dailyExpenses, totalExpenses,
            dailyCollections, totalCollections,
            dailyNetCashFlow, totalNetCashFlow,
            totalCustomerDebt, totalSupplierDebt,
            totalInventoryValue,
            mtdNetSales,
            mtdCollections,
            mtdCashOutflows,
            dailyIncomeExpenses,
            mtdIncomeExpenses,
            dailyAccountOutflows,
            mtdAccountOutflows
        );

        return Ok(new {
            date          = targetDate,
            summary,
            accounts      = accInfos,
            expensesBreakdown,
            salesByPaymentMethod = salesByPayment,
            salesTrend,
            cashOutflows,
            incomeTrend,
            // 🆕 Detailed ledger movements
            detailedExpenses,
            detailedAccountMovements,
            // 🆕 Zakat account balance (210201)
            zakatAccountBalance,
            zakatAccountName,
            zakatAccountId
        });
    }

}

//  Report DTOs 
public record CustomerStatementLine(DateTime Date, string Type, string Reference, string Description, decimal Debit, decimal Credit, decimal Balance);
public record CustomerAgingRow(int CustomerId, string Name, string Phone, decimal Total, decimal Current, decimal Days60, decimal Days90, decimal Over90);
public record SupplierAgingRow(int SupplierId, string Name, string Phone, string CompanyName, decimal Total, decimal Current, decimal Days60, decimal Days90, decimal Over90);
public record InventoryRow(int Id, string NameAr, string NameEn, string SKU, string CategoryName, decimal Price, decimal? DiscountPrice, decimal CostPrice, int TotalStock, decimal TotalValue, decimal TotalCostValue, List<VariantInventoryRow> Variants);
public record VariantInventoryRow(int Id, string Size, string Color, string ColorAr, int StockQuantity, decimal Price, decimal Value);
public record SalesRow(int Id, string OrderNumber, DateTime Date, string CustomerName, string Phone, string Source, string Status, string PaymentMethod, decimal SubTotal, decimal DiscountAmount, decimal TotalAmount, int ItemCount, List<ReportItemDto>? Items = null, string? PaymentDetails = null);
public record PurchaseRow(int Id, string InvoiceNumber, string SupplierInvoiceNumber, string SupplierName, DateTime InvoiceDate, string PaymentTerms, string Status, decimal SubTotal, decimal TaxAmount, decimal TotalAmount, decimal ReturnedAmount, decimal PaidAmount, decimal RemainingAmount, List<ReportItemDto>? Items = null);
public record ReturnRow(string Reference, DateTime Date, string Name, string Phone, decimal Amount, string Reason, List<ReportItemDto>? Items = null, int? OrderId = null, string CreatorName = "", string ReturnerName = "", string RefundMethod = "", string OrderStatus = "");
public record ReportItemDto(string SKU, string ProductName, string Size, string Color, decimal Quantity, decimal UnitPrice = 0, decimal UnitCost = 0, decimal Discount = 0, decimal LineTotal = 0, int? ProductId = null, int? ProductVariantId = null);
public record UserActivityRow(string UserId, string UserName, int OrderCount, decimal GrossSales, decimal TotalReturns, decimal TotalDiscount, decimal NetSales, int Cancellations);
public record ProductMovementLine(DateTime Date, string Type, string Reference, string EntityName, string Details, int In, int Out, decimal Amount, string ProductName = "", string Source = "", string Status = "", string SKU = "", int Balance = 0, int? SourceId = null, string Size = "", string Color = "");

// Daily Report DTOs
public record DailyReportSummary(decimal totalSales, decimal totalReturns, decimal totalCollections, decimal totalSettlements, decimal totalExpenses, decimal totalCredit, decimal netCashflow, decimal totalDiscounts = 0);
public record DailyReportPaymentMethod(string Key, string NameAr, string NameEn, decimal Inflow, decimal Outflow, decimal Net);
public record DailyReportSale(int Id, string OrderNumber, DateTime Date, string CustomerName, string Source, string PaymentMethod, decimal TotalAmount, decimal PaidAmount);
public record DailyReportReturn(string Reference, DateTime Date, string CustomerName, decimal Amount, string Description);
public record DailyReportCollection(string VoucherNumber, DateTime Date, string CustomerName, decimal Amount, string PaymentMethod, string? Reference, string Description);
public record DailyReportSettlement(string VoucherNumber, DateTime Date, string SupplierName, decimal Amount, string PaymentMethod, string? Reference, string Description);
public record DailyReportExpense(string VoucherNumber, DateTime Date, decimal Amount, string ExpenseCategory, string PaymentMethod, string? Reference, string Description);



public record PartnersReportExpenseCategory(string Category, decimal Amount);
public record PartnersReportPaymentMethodSale(string PaymentMethod, decimal Amount);
public record PartnersReportSalesTrend(DateTime Date, decimal Amount);

public record PartnersReportSummary(
    decimal PosDailySales, decimal PosTotalSales, 
    decimal WebDailySales, decimal WebTotalSales, 
    decimal DailyExpenses, decimal TotalExpenses, 
    decimal DailyCollections, decimal TotalCollections,
    decimal DailyNetCashFlow, decimal TotalNetCashFlow,
    decimal TotalCustomerDebt, decimal TotalSupplierDebt,
    decimal TotalInventoryValue,
    decimal MtdNetSales,
    decimal MtdCollections,
    decimal MtdCashOutflows,
    decimal DailyIncomeExpenses,
    decimal MtdIncomeExpenses,
    decimal DailyAccountOutflows,
    decimal MtdAccountOutflows
);

public record PartnersReportAccountInfo(
    int AccountId, 
    string AccountName, 
    decimal DailyChange, 
    decimal CumulativeBalance,
    string Category,
    int? BranchId = null,
    string? BranchName = null
);

public record PartnersReportCashOutflow(
    int JournalEntryId,
    string Reference,
    DateTime Date,
    string Description,
    string CashAccountName,
    decimal Amount,
    string CreatorName,
    string BranchName,
    string Category
);

public record PartnersReportIncomeTrendItem(
    string Label,
    decimal Revenue,
    decimal Expenses
);

public record PartnersComprehensiveReportResponse(
    DateTime Date, 
    PartnersReportSummary Summary, 
    IEnumerable<PartnersReportAccountInfo> Accounts,
    IEnumerable<PartnersReportExpenseCategory> ExpenseBreakdown,
    IEnumerable<PartnersReportPaymentMethodSale> SalesByPaymentMethod,
    IEnumerable<PartnersReportSalesTrend> SalesTrend,
    IEnumerable<PartnersReportCashOutflow> CashOutflows,
    IEnumerable<PartnersReportIncomeTrendItem> IncomeTrend
);




