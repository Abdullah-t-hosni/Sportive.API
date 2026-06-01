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

    [HttpGet("fix-invoice-issue")]
    [AllowAnonymous]
    public async Task<IActionResult> FixInvoiceIssue()
    {
        const int maxRetries = 5;
        int attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                var orderNum = "POS-2605-0325";

                // Reload fresh context each retry to avoid stale tracking state
                var order = await _db.Orders
                    .Include(o => o.Items)
                        .ThenInclude(i => i.Product)
                    .Include(o => o.Payments)
                    .FirstOrDefaultAsync(o => o.OrderNumber == orderNum);

                if (order == null)
                    return NotFound(new { message = $"Order {orderNum} not found." });

                // 1. Set Order Status → Returned, PaymentStatus → Refunded
                order.Status = OrderStatus.Returned;
                order.PaymentStatus = PaymentStatus.Refunded;

                // 2. Mark all items as fully returned
                foreach (var item in order.Items)
                    item.ReturnedQuantity = item.Quantity;

                await _db.SaveChangesAsync();

                // 3. Get the SalesAccountingService
                var salesAcctService = HttpContext.RequestServices
                    .GetRequiredService<Sportive.API.Services.SalesAccountingService>();

                // 4. Remove any stale journal entries for this order
                var existingEntries = await _db.JournalEntries
                    .Include(e => e.Lines)
                    .Where(e => e.OrderId == order.Id
                             || e.Reference == orderNum
                             || (e.Reference != null && e.Reference.StartsWith(orderNum)))
                    .ToListAsync();

                if (existingEntries.Any())
                {
                    _db.JournalLines.RemoveRange(existingEntries.SelectMany(e => e.Lines));
                    _db.JournalEntries.RemoveRange(existingEntries);
                    await _db.SaveChangesAsync();
                }

                // 5. Post Sales Journal Entry  (قيد المبيعات)
                await salesAcctService.PostSalesOrderAsync(order);

                // 6. Post Sales Return Journal Entry (قيد المرتجع)
                await salesAcctService.PostSalesReturnAsync(order);

                // Verify the entries were created
                var createdEntries = await _db.JournalEntries
                    .Where(e => e.OrderId == order.Id)
                    .Select(e => new { e.Type, e.EntryNumber, e.Reference })
                    .ToListAsync();

                return Ok(new {
                    message = $"Success on attempt {attempt}. Order and Journal Entries updated.",
                    orderId = order.Id,
                    status = order.Status.ToString(),
                    paymentStatus = order.PaymentStatus.ToString(),
                    journalEntries = createdEntries,
                    items = order.Items.Select(i => new { i.ProductNameAr, i.Quantity, i.ReturnedQuantity })
                });
            }
            catch (Exception ex) when (
                attempt < maxRetries &&
                (ex.Message.Contains("Deadlock") || ex.Message.Contains("deadlock") ||
                 ex.InnerException?.Message.Contains("Deadlock") == true ||
                 ex.InnerException?.Message.Contains("deadlock") == true))
            {
                _logger.LogWarning("FixInvoiceIssue: Deadlock on attempt {Attempt}/{Max}. Retrying in 2s...", attempt, maxRetries);
                await Task.Delay(TimeSpan.FromSeconds(2));
                // Detach all tracked entities to get a clean state
                _db.ChangeTracker.Clear();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, attempt, stackTrace = ex.StackTrace });
            }
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
            .Select(p => new { p.Id, p.NameAr, p.SKU })
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
        [FromQuery] int       pageSize   = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var cacheKey = $"CustStatement_{customerId}_{search}_{fromDate}_{toDate}_{unpaidOnly}_{page}_{pageSize}";
        if (!excel && _cache.TryGetValue(cacheKey, out var cachedData))
            return Ok(cachedData);
        // 🕒 BUSINESS DAY OFFSET: The day ends at 2 AM.
        var from = (fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1)).Date.AddHours(2);
        var to   = (toDate ?? TimeHelper.GetEgyptTime()).Date.AddDays(1).AddHours(2).AddTicks(-1);

        if (customerId == null && !string.IsNullOrEmpty(search))
        {
            var found = await _db.Customers
                .Where(c => c.FullName.Contains(search) || (c.Phone != null && c.Phone.Contains(search)))
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
        decimal priorBalance = await _db.JournalLines
            .Where(l => l.CustomerId == customerId && l.JournalEntry.EntryDate < from && l.JournalEntry.Status == JournalEntryStatus.Posted)
            .SumAsync(l => (decimal?)(l.Debit - l.Credit)) ?? 0;

        // 2. ديون العملاء (عمر الدين)
        var entries = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.CustomerId == customerId && l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to && l.JournalEntry.Status == JournalEntryStatus.Posted)
            .OrderBy(l => l.JournalEntry.EntryDate)
            .ToListAsync();

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
        [FromQuery] int       pageSize   = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var cacheKey = $"SuppStatement_{supplierId}_{search}_{fromDate}_{toDate}_{unpaidOnly}_{page}_{pageSize}";
        if (!excel && _cache.TryGetValue(cacheKey, out var cachedData))
            return Ok(cachedData);
        // 🕒 BUSINESS DAY OFFSET: The day ends at 2 AM.
        var from = (fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1)).Date.AddHours(2);
        var to   = (toDate ?? TimeHelper.GetEgyptTime()).Date.AddDays(1).AddHours(2).AddTicks(-1);

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
        decimal priorBalance = await _db.JournalLines
            .Where(l => l.SupplierId == supplierId && l.JournalEntry.EntryDate < from && l.JournalEntry.Status == JournalEntryStatus.Posted)
            .SumAsync(l => (decimal?)(l.Credit - l.Debit)) ?? 0;

        var entries = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.SupplierId == supplierId && l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to && l.JournalEntry.Status == JournalEntryStatus.Posted)
            .OrderBy(l => l.JournalEntry.EntryDate)
            .ToListAsync();

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
        [FromQuery] string?   search  = null,
        [FromQuery] DateTime? asOfDate = null,
        [FromQuery] bool      excel   = false)
    {
        // 🕒 BUSINESS DAY OFFSET: The day ends at 2 AM.
        var asOf = (asOfDate ?? TimeHelper.GetEgyptTime()).Date.AddDays(1).AddHours(2).AddTicks(-1);

        var customersQuery = _db.Customers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(search))
            customersQuery = customersQuery.Where(c => c.FullName.Contains(search) || (c.Phone != null && c.Phone.Contains(search)));

        var customers = await customersQuery
            .Select(c => new {
                c.Id,
                c.FullName,
                c.Phone,
                Orders = c.Orders
                    .Where(o => o.Status != OrderStatus.Cancelled 
                             && o.CreatedAt <= asOf
                             && (o.PaymentMethod == PaymentMethod.Credit || o.PaymentMethod == PaymentMethod.Mixed || (o.TotalAmount - o.PaidAmount) > 0))
                    .Select(o => new {
                        o.CreatedAt,
                        o.TotalAmount,
                        o.PaidAmount,
                        ReturnedAmount = o.Items.Sum(i => i.ReturnedQuantity * i.UnitPrice)
                    }).ToList()
            })
            .ToListAsync();

        // ✅ FIX: Use Ledger (JournalLines) to get all movements accurately
        var ledgerBalances = await _db.JournalLines
            .Where(l => (l.Account.Code.StartsWith("1104") || l.Account.Code.StartsWith("1201")))
            .Where(l => l.JournalEntry.EntryDate <= asOf && (l.JournalEntry.Status == JournalEntryStatus.Posted))
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
        [FromQuery] bool      excel    = false)
    {
        // 🕒 BUSINESS DAY OFFSET: The day ends at 2 AM.
        var asOf = (asOfDate ?? TimeHelper.GetEgyptTime()).Date.AddDays(1).AddHours(2).AddTicks(-1);
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
            var ledgerBalances = await _db.JournalLines
                .AsNoTracking()
                .Where(l => l.SupplierId != null && l.Account.Code.StartsWith("2101"))
                .Where(l => l.JournalEntry.EntryDate <= asOf && l.JournalEntry.Status == JournalEntryStatus.Posted)
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
        [FromQuery] bool    excel       = false)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);

        var cacheKey = $"Inventory_{search}_{categoryId}_{brandId}_{color}_{size}_{lowStock}_{stockStatus}_{page}_{pageSize}_{source}_{toDate}";
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

        if (toDate.HasValue)
        {
            var limit = toDate.Value.Date.AddDays(1).AddHours(2).AddTicks(-1);
            var movementQuery = _db.InventoryMovements.Where(m => m.CreatedAt <= limit);
            if (source.HasValue)
            {
                movementQuery = movementQuery.Where(m => m.CostCenter == source.Value);
            }

            var variantStocks = await movementQuery
                .Where(m => m.ProductVariantId.HasValue)
                .GroupBy(m => m.ProductVariantId!.Value)
                .Select(g => new { VariantId = g.Key, Stock = g.Sum(m => m.Quantity) })
                .ToDictionaryAsync(x => x.VariantId, x => x.Stock);

            var simpleProductStocks = await movementQuery
                .Where(m => !m.ProductVariantId.HasValue && m.ProductId.HasValue)
                .GroupBy(m => m.ProductId!.Value)
                .Select(g => new { ProductId = g.Key, Stock = g.Sum(m => m.Quantity) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Stock);

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
                    .Where(l => l.AccountId == inventoryAccId && l.JournalEntry.Status == JournalEntryStatus.Posted && l.JournalEntry.EntryDate <= limit);
                
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
        [FromQuery] bool         excel      = false)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var cacheKey = $"Sales_{fromDate}_{toDate}_{source}_{categoryId}_{brandId}_{color}_{size}_{page}_{pageSize}";
        if (!excel && _cache.TryGetValue(cacheKey, out var cachedData))
            return Ok(cachedData);

        var from = (fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1)).Date.AddHours(2);
        var to   = (toDate ?? TimeHelper.GetEgyptTime()).Date.AddDays(1).AddHours(2).AddTicks(-1);

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
                CustomerPhone = o.Customer != null ? o.Customer.Phone : null,
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
                        i.DiscountAmount,
                        i.TotalPrice
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
                o.CustomerPhone ?? "",
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
                    0, 
                    i.DiscountAmount / (i.Quantity > 0 ? i.Quantity : 1),
                    i.TotalPrice
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
        var totalDiscount = summaryData.Sum(o => o.DiscountAmount);
        var totalUnits = summaryData.Sum(o => o.ItemCount);

        var summary = new {
            totalOrders   = totalOrdersCount,
            totalGrossRevenue  = totalGrossRevenue,
            totalDiscount      = totalDiscount,
            totalUnits         = totalUnits,
            totalReturns       = ledgerReturns,
            totalNetRevenue    = totalGrossRevenue - ledgerReturns,
            avgOrder      = totalOrdersCount > 0 ? (totalGrossRevenue - ledgerReturns) / totalOrdersCount : 0,
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
        [FromQuery] bool      excel      = false)
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
                            it.Quantity, it.UnitCost, it.TotalCost
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
                        it.Quantity, 0, it.UnitCost, 0, it.TotalCost
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
        [FromQuery] OrderSource? source     = null,
        [FromQuery] bool      excel      = false)
    {
        var from = fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var catIds = categoryId.HasValue && categoryId > 0 ? await FilterHelper.GetCategoryFamilyIds(_db, categoryId) : new List<int>();
        var brIds = brandId.HasValue && brandId > 0 ? await FilterHelper.GetBrandFamilyIds(_db, brandId) : new List<int>();

        // Get all SalesReturn journal entries
        var returnsQ = _db.JournalEntries.AsNoTracking()
            .Where(j => j.Type == JournalEntryType.SalesReturn 
                     && j.EntryDate >= from && j.EntryDate <= to);

        if (source.HasValue)
        {
            returnsQ = returnsQ.Where(j => j.Order != null && j.Order.Source == source.Value);
        }

        if (catIds.Any())
            returnsQ = returnsQ.Where(j => j.Order != null && j.Order.Items.Any(it => it.Product != null && it.Product.CategoryId.HasValue && catIds.Contains(it.Product.CategoryId.Value)));

        if (brIds.Any())
            returnsQ = returnsQ.Where(j => j.Order != null && j.Order.Items.Any(it => it.Product != null && it.Product.BrandId.HasValue && brIds.Contains(it.Product.BrandId.Value)));

        if (!string.IsNullOrEmpty(color))
            returnsQ = returnsQ.Where(j => j.Order != null && j.Order.Items.Any(it => it.Color == color || (it.Color == null && it.Product != null && it.Product.Variants.Any(v => (v.Color == color || v.ColorAr == color)))));

        if (!string.IsNullOrEmpty(size))
            returnsQ = returnsQ.Where(j => j.Order != null && j.Order.Items.Any(it => it.Size == size || (it.Size == null && it.Product != null && it.Product.Variants.Any(v => v.Size == size))));

        var returns = await returnsQ
            .Select(j => new {
                j.Reference, j.EntryNumber, j.EntryDate,
                OrderId = j.Order != null ? (int?)j.Order.Id : null,
                CustomerName = j.Order != null && j.Order.Customer != null ? j.Order.Customer.FullName : "Walk-in",
                CustomerPhone = j.Order != null && j.Order.Customer != null ? j.Order.Customer.Phone : "",
                OriginalAmount = j.Lines.Where(l => l.Debit > 0).Sum(l => l.Debit),
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
                        i.Size, i.Color, i.ReturnedQuantity, i.UnitPrice, i.Quantity, i.DiscountAmount
                    }).ToList() : null
            })
            .OrderByDescending(j => j.EntryDate).ToListAsync();

        var directReturnRefs = returns.Where(r => r.Items == null).Select(r => r.Reference ?? r.EntryNumber).ToList();
        var movements = await _db.InventoryMovements
            .Include(m => m.Product)
            .Include(m => m.ProductVariant)
            .Where(m => directReturnRefs.Contains(m.Reference) && m.Type == InventoryMovementType.ReturnIn)
            .ToListAsync();

        var movementsMap = movements.GroupBy(m => m.Reference).ToDictionary(g => g.Key, g => g.ToList());

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
            List<ReportItemDto> itemsList = null;
            if (j.Items != null)
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
                    (i.UnitPrice - (i.DiscountAmount / (i.Quantity > 0 ? i.Quantity : 1))) * i.ReturnedQuantity
                )).ToList();
            }
            else if (movementsMap.TryGetValue(j.Reference ?? j.EntryNumber, out var movs))
            {
                itemsList = movs.Select(m => new ReportItemDto(
                    m.Product?.SKU ?? "",
                    m.Product?.NameAr ?? "",
                    m.ProductVariant?.Size ?? "",
                    m.ProductVariant?.ColorAr ?? m.ProductVariant?.Color ?? "",
                    Math.Abs(m.Quantity),
                    m.UnitCost,
                    0,
                    0,
                    Math.Abs(m.Quantity) * m.UnitCost
                )).ToList();
            }

            var itemsAmount = itemsList?.Sum(it => it.LineTotal) ?? 0;

            return new ReturnRow(
                j.Reference ?? j.EntryNumber, j.EntryDate,
                j.CustomerName,
                j.CustomerPhone ?? "",
                (catIds.Any() || brIds.Any() || !string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(size)) ? itemsAmount : j.OriginalAmount,
                j.Description ?? "",
                itemsList,
                j.OrderId,
                (j.SalesPersonId != null && personNamesMap.TryGetValue(j.SalesPersonId, out var creator)) ? creator : "System/Unknown",
                (j.CreatedByUserId != null && personNamesMap.TryGetValue(j.CreatedByUserId, out var returner)) ? returner : "System/Unknown"
            );
        }).ToList();

        var summary = new {
            count        = rows.Count,
            totalAmount  = rows.Sum(r => r.Amount),
            totalReturnedItems = rows.Sum(r => r.Items?.Sum(it => it.Quantity) ?? 0)
        };

        if (excel) return ExcelReturns(rows, summary, from, to, "مرتجعات المبيعات");

        return Ok(new { from, to, rows, summary });
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
        [FromQuery] bool      excel      = false)
    {
        var from = fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var catIds = categoryId.HasValue && categoryId > 0 ? await FilterHelper.GetCategoryFamilyIds(_db, categoryId) : new List<int>();
        var brIds = brandId.HasValue && brandId > 0 ? await FilterHelper.GetBrandFamilyIds(_db, brandId) : new List<int>();

        var q = _db.PurchaseReturns.AsNoTracking()
            .Where(r => r.ReturnDate >= from && r.ReturnDate <= to);

        if (supplierId.HasValue) q = q.Where(r => r.SupplierId == supplierId.Value);
        if (source.HasValue) q = q.Where(r => r.CostCenter == source.Value);

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
                        it.Quantity, it.UnitCost, it.TotalCost
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
                it.Quantity, 0, it.UnitCost, 0, it.TotalCost
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
        [FromQuery] bool      excel    = false)
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
        [FromQuery] bool      excel      = false)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var cacheKey = $"ProdMovement_{productId}_{search}_{categoryId}_{brandId}_{fromDate}_{toDate}_{color}_{size}_{source}_{page}_{pageSize}";
        if (!excel && _cache.TryGetValue(cacheKey, out var cachedData))
            return Ok(cachedData);
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

            // Logic: If productId is 0 or null, we might either show choices OR show all movements.
            // Let's change it so that if it's called with productId=0 or similar, it fetches all.
            if (productId == null && !string.IsNullOrEmpty(search))
            {
                // handled above
            }

            // Fetch Movements from InventoryMovements table
            var movementsQuery = _db.InventoryMovements
                .Include(m => m.Product)
                .Include(m => m.ProductVariant)
                .Where(m => (m.CreatedAt >= from && m.CreatedAt <= to) || m.Type == InventoryMovementType.OpeningBalance);

            if (productId > 0) movementsQuery = movementsQuery.Where(m => m.ProductId == productId);
            if (source.HasValue) movementsQuery = movementsQuery.Where(m => m.CostCenter == source.Value);

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
            var dbMovements = await movementsQuery
                .OrderByDescending(m => m.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

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
                    m.RemainingStock,
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

            if (!excel) _cache.Set(cacheKey, result, TimeSpan.FromMinutes(2));

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
            movements = rows // Duplicate for compatibility with different report versions
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
        [FromQuery] string? size       = null)
    {
        var cutoff = TimeHelper.GetEgyptTime().AddDays(-days);

        // 1. Get filtered product list first
        var query = _db.Products
            .Where(p => p.TotalStock > 0 && (p.Status == ProductStatus.Active || p.Status == ProductStatus.OutOfStock || p.Status == ProductStatus.Hidden));

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

        // 3. Project data and LastSaleDate
        var productsData = await query
            .Select(p => new {
                p.Id, p.NameAr, p.SKU, p.TotalStock, p.Price, p.CreatedAt,
                LastSaleDate = _db.InventoryMovements
                    .Where(m => m.ProductId == p.Id && m.Type == InventoryMovementType.Sale)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => (DateTime?)m.CreatedAt)
                    .FirstOrDefault()
            })
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
                    DaysSinceLastSale = (int)(TimeHelper.GetEgyptTime() - effectiveDate).TotalDays,
                    Value = p.TotalStock * p.Price
                };
            })
            .OrderByDescending(p => p.DaysSinceLastSale)
            .ToList();

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
        [FromQuery] bool         excel      = false)
    {
        var now = TimeHelper.GetEgyptTime();
        var from = (fromDate ?? now).Date.AddHours(2);
        var to   = (toDate ?? now).Date.AddDays(1).AddHours(2).AddTicks(-1);

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
                .ThenInclude(o => o.Customer)
            .Include(j => j.Lines)
                .ThenInclude(l => l.Account)
            .Where(j => j.Type == JournalEntryType.SalesReturn 
                     && j.EntryDate >= from && j.EntryDate <= to
                     && j.Status == JournalEntryStatus.Posted);

        if (source.HasValue)
        {
            returnsQuery = returnsQuery.Where(j => j.Order != null && j.Order.Source == source.Value);
        }

        var returnsData = await returnsQuery.ToListAsync();

        var salesReturnAccId = maps.GetValueOrDefault("salesreturnaccountid");
        var vatOutputAccId = maps.GetValueOrDefault("vatoutputaccountid");

        decimal returnCash = 0, returnCard = 0, returnVoda = 0, returnInsta = 0;
        var returnsRows = new List<DailyReportReturn>();

        foreach (var j in returnsData)
        {
            decimal amt = j.Lines
                .Where(l => l.Debit > 0 && (
                    l.AccountId == salesReturnAccId || 
                    l.AccountId == vatOutputAccId || 
                    (l.Account != null && (l.Account.Code.StartsWith("4103") || l.Account.Code.StartsWith("2105") || l.Account.Code.StartsWith("1202")))
                ))
                .Sum(l => l.Debit);
            
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

        var summary = new DailyReportSummary(
            totalSalesAmount,
            totalReturnsAmount,
            totalCollectionsAmount,
            totalSettlementsAmount,
            totalExpensesAmount,
            orderCredit,
            totalCollectionsAmount - totalSettlementsAmount - totalExpensesAmount - totalReturnsAmount
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
        [FromQuery] bool excel = false)
    {
        var now = TimeHelper.GetEgyptTime();
        var from = (fromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var to = (toDate ?? now).Date.AddDays(1).AddTicks(-1);

        var orders = await _db.Orders.AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .Where(o => o.CreatedAt >= from && o.CreatedAt <= to && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned)
            .ToListAsync();

        var invoiceRows = new List<Sportive.API.DTOs.Reports.InvoiceProfitabilityDto>();

        foreach (var order in orders)
        {
            decimal revenue = order.TotalAmount - order.TotalVatAmount; // net revenue without tax
            decimal cost = 0;
            decimal totalReturnedValue = 0;

            foreach (var item in order.Items)
            {
                decimal itemCost = item.Product?.CostPrice ?? 0;
                // Deduct cost of returned items
                int netQty = item.Quantity - item.ReturnedQuantity;
                cost += itemCost * Math.Max(0, netQty);

                if (item.ReturnedQuantity > 0)
                {
                    // Calculate returned ratio and its share of the pre-tax item total
                    decimal returnedRatio = (decimal)item.ReturnedQuantity / item.Quantity;
                    decimal returnedLineTotal = item.TotalPrice * returnedRatio;

                    // Subtract prorated order-level coupon discount if applicable
                    decimal returnedDiscountShare = 0;
                    if (order.SubTotal > 0 && order.DiscountAmount > 0)
                    {
                        returnedDiscountShare = (item.TotalPrice / order.SubTotal) * order.DiscountAmount * returnedRatio;
                    }

                    // Pre-tax net value of returned items to be subtracted from the pre-tax order revenue
                    decimal netItemReturn = returnedLineTotal - returnedDiscountShare;
                    totalReturnedValue += netItemReturn;
                }
            }

            // Deduct the returned pre-tax value from the order's net revenue
            revenue = Math.Max(0, revenue - totalReturnedValue);

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

        return Ok(new Sportive.API.DTOs.Reports.InvoiceProfitabilityReportDto {
            FromDate = from,
            ToDate = to,
            TotalInvoices = totalInvoices,
            TotalRevenue = totalRevenue,
            TotalCost = totalCost,
            TotalProfit = totalProfit,
            AverageMargin = averageMargin,
            Invoices = invoiceRows
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
}

//  Report DTOs 
public record CustomerStatementLine(DateTime Date, string Type, string Reference, string Description, decimal Debit, decimal Credit, decimal Balance);
public record CustomerAgingRow(int CustomerId, string Name, string Phone, decimal Total, decimal Current, decimal Days60, decimal Days90, decimal Over90);
public record SupplierAgingRow(int SupplierId, string Name, string Phone, string CompanyName, decimal Total, decimal Current, decimal Days60, decimal Days90, decimal Over90);
public record InventoryRow(int Id, string NameAr, string NameEn, string SKU, string CategoryName, decimal Price, decimal? DiscountPrice, decimal CostPrice, int TotalStock, decimal TotalValue, decimal TotalCostValue, List<VariantInventoryRow> Variants);
public record VariantInventoryRow(int Id, string Size, string Color, string ColorAr, int StockQuantity, decimal Price, decimal Value);
public record SalesRow(int Id, string OrderNumber, DateTime Date, string CustomerName, string Phone, string Source, string Status, string PaymentMethod, decimal SubTotal, decimal DiscountAmount, decimal TotalAmount, int ItemCount, List<ReportItemDto>? Items = null, string? PaymentDetails = null);
public record PurchaseRow(int Id, string InvoiceNumber, string SupplierInvoiceNumber, string SupplierName, DateTime InvoiceDate, string PaymentTerms, string Status, decimal SubTotal, decimal TaxAmount, decimal TotalAmount, decimal ReturnedAmount, decimal PaidAmount, decimal RemainingAmount, List<ReportItemDto>? Items = null);
public record ReturnRow(string Reference, DateTime Date, string Name, string Phone, decimal Amount, string Reason, List<ReportItemDto>? Items = null, int? OrderId = null, string CreatorName = "", string ReturnerName = "");
public record ReportItemDto(string SKU, string ProductName, string Size, string Color, decimal Quantity, decimal UnitPrice = 0, decimal UnitCost = 0, decimal Discount = 0, decimal LineTotal = 0);
public record UserActivityRow(string UserId, string UserName, int OrderCount, decimal GrossSales, decimal TotalReturns, decimal TotalDiscount, decimal NetSales, int Cancellations);
public record ProductMovementLine(DateTime Date, string Type, string Reference, string EntityName, string Details, int In, int Out, decimal Amount, string ProductName = "", string Source = "", string Status = "", string SKU = "", int Balance = 0, int? SourceId = null, string Size = "", string Color = "");

// Daily Report DTOs
public record DailyReportSummary(decimal totalSales, decimal totalReturns, decimal totalCollections, decimal totalSettlements, decimal totalExpenses, decimal totalCredit, decimal netCashflow);
public record DailyReportPaymentMethod(string Key, string NameAr, string NameEn, decimal Inflow, decimal Outflow, decimal Net);
public record DailyReportSale(int Id, string OrderNumber, DateTime Date, string CustomerName, string Source, string PaymentMethod, decimal TotalAmount, decimal PaidAmount);
public record DailyReportReturn(string Reference, DateTime Date, string CustomerName, decimal Amount, string Description);
public record DailyReportCollection(string VoucherNumber, DateTime Date, string CustomerName, decimal Amount, string PaymentMethod, string? Reference, string Description);
public record DailyReportSettlement(string VoucherNumber, DateTime Date, string SupplierName, decimal Amount, string PaymentMethod, string? Reference, string Description);
public record DailyReportExpense(string VoucherNumber, DateTime Date, decimal Amount, string ExpenseCategory, string PaymentMethod, string? Reference, string Description);


