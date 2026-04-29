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
[Authorize(Roles = "Admin,Manager,Accountant,Staff")]
public class OperationalReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<OperationalReportsController> _logger;
    private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;
    public OperationalReportsController(AppDbContext db, ILogger<OperationalReportsController> logger, Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    {
        _db = db;
        _logger = logger;
        _cache = cache;
    }
    
    [HttpPost("reset-supplier-balances")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ResetSupplierBalances()
    {
        try 
        {
            var suppliers = await _db.Suppliers.ToListAsync();
            foreach(var s in suppliers) s.OpeningBalance = 0;
            await _db.SaveChangesAsync();
            return Ok(new { message = "All supplier opening balances reset to 0" });
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
            .Where(p => p.Status == ProductStatus.Active || p.Status == ProductStatus.OutOfStock)
            .OrderBy(p => p.NameAr)
            .Select(p => new { p.Id, p.NameAr, p.SKU })
            .ToListAsync();

        return Ok(new { colors, sizes, products });
    }

    // ══════════════════════════════════════════════════════
    // 1. كشف حساب عميل
    // GET /api/operationalreports/customer-statement?customerId=&fromDate=&toDate=
    // ══════════════════════════════════════════════════════
    [HttpGet("customer-statement")]
    [Authorize(Roles = "Admin,Manager,Accountant")]
    public async Task<IActionResult> CustomerStatement(
        [FromQuery] int?      customerId = null,
        [FromQuery] string?   search     = null,
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] bool      excel      = false,
        [FromQuery] bool      unpaidOnly = false)
    {
        var from = fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        if (customerId == null && !string.IsNullOrEmpty(search))
        {
            var found = await _db.Customers
                .Where(c => c.FullName.Contains(search) || (c.Phone != null && c.Phone.Contains(search)))
                .Select(c => c.Id)
                .FirstOrDefaultAsync();
            if (found > 0) customerId = found;
        }

        if (customerId == null)
            return Ok(new { customers = await _db.Customers.Select(c => new { c.Id, c.FullName, c.Phone, c.Email }).ToListAsync() });

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId);
        if (customer == null) return NotFound();

        // ✅ REFACTORED TO LEDGER-BASED (Source of Truth)
        // 1. Calculate Prior Balance
        decimal priorBalance = await _db.JournalLines
            .Where(l => l.CustomerId == customerId && l.JournalEntry.EntryDate < from && l.JournalEntry.Status == JournalEntryStatus.Posted)
            .SumAsync(l => (decimal?)(l.Debit - l.Credit)) ?? 0;

        // 2. Fetch Movements in Period
        var entries = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.CustomerId == customerId && l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to && l.JournalEntry.Status == JournalEntryStatus.Posted)
            .OrderBy(l => l.JournalEntry.EntryDate)
            .ToListAsync();

        var lines = new List<CustomerStatementLine>();
        decimal balance = priorBalance;

        if (balance != 0)
        {
            lines.Add(new CustomerStatementLine(from.AddSeconds(-1), "رصيد", "OPENING", "رصيد مرحّل من الفترة السابقة", balance > 0 ? balance : 0, balance < 0 ? Math.Abs(balance) : 0, balance));
        }

        foreach (var l in entries)
        {
            balance += (l.Debit - l.Credit);
            var typeStr = l.JournalEntry.Type switch {
                JournalEntryType.Sales => "فاتورة",
                JournalEntryType.ReceiptVoucher => "سند قبض",
                JournalEntryType.SalesReturn => "مرتجع",
                _ => "قيد"
            };
            lines.Add(new CustomerStatementLine(
                l.JournalEntry.EntryDate, typeStr, l.JournalEntry.Reference ?? l.JournalEntry.EntryNumber,
                l.Description ?? l.JournalEntry.Description ?? "حركة حساب",
                l.Debit, l.Credit, balance));
        }

        if (unpaidOnly) {
            lines = lines.Where(l => l.Debit > 0 && l.Balance > 0).ToList();
        }

        var totalDebit  = lines.Where(l => l.Reference != "OPENING").Sum(l => l.Debit);
        var totalCredit = lines.Where(l => l.Reference != "OPENING").Sum(l => l.Credit);
        
        if (excel) return ExcelCustomerStatement(customer, lines, totalDebit, totalCredit, balance, from, to);

        return Ok(new {
            customer = new { customer.Id, customer.FullName, customer.Phone, customer.Email },
            from, to, lines,
            totalDebit, totalCredit, outstanding = balance,
            hasBalance = Math.Abs(balance) > 0.01M
        });
    }

    // ══════════════════════════════════════════════════════
    // 1.5 كشف حساب مورد
    // GET /api/operationalreports/supplier-statement?supplierId=&fromDate=&toDate=
    // ══════════════════════════════════════════════════════
    [HttpGet("supplier-statement")]
    [Authorize(Roles = "Admin,Manager,Accountant")]
    public async Task<IActionResult> SupplierStatement(
        [FromQuery] int?      supplierId = null,
        [FromQuery] string?   search     = null,
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] bool      excel      = false,
        [FromQuery] bool      unpaidOnly = false)
    {
        var from = fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        if (supplierId == null && !string.IsNullOrEmpty(search))
        {
            supplierId = await _db.Suppliers
                .Where(s => s.Name.Contains(search) || s.Phone.Contains(search))
                .Select(s => s.Id)
                .FirstOrDefaultAsync();
        }

        if (supplierId == null)
            return Ok(new { items = await _db.Suppliers.Select(s => new { s.Id, s.Name, s.Phone }).ToListAsync() });

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
            lines.Add(new CustomerStatementLine(from.AddSeconds(-1), "رصيد", "OPENING", "رصيد مرحّل", balance > 0 ? balance : 0, balance < 0 ? Math.Abs(balance) : 0, balance));
        
        foreach (var l in entries)
        {
            balance += (l.Credit - l.Debit); // For suppliers, Credit increases balance (debt to them)
            var typeStr = l.JournalEntry.Type switch {
                JournalEntryType.Purchases => "فاتورة شراء",
                JournalEntryType.PaymentVoucher => "سند صرف",
                JournalEntryType.PurchaseReturn => "مرتجع",
                _ => "قيد"
            };
            lines.Add(new CustomerStatementLine(
                l.JournalEntry.EntryDate, typeStr, l.JournalEntry.Reference ?? l.JournalEntry.EntryNumber,
                l.Description ?? l.JournalEntry.Description ?? "حركة حساب",
                l.Credit, l.Debit, balance));
        }

        if (unpaidOnly) lines = lines.Where(l => l.Credit > 0 && l.Balance > 0).ToList();

        var totalCredit = lines.Where(l => l.Reference != "OPENING").Sum(l => l.Debit); // Debit side here
        var totalDebt   = lines.Where(l => l.Reference != "OPENING").Sum(l => l.Credit);

        if (excel) return ExcelSupplierStatement(supplier, lines, totalDebt, totalCredit, from, to);

        return Ok(new { 
            supplier = new { supplier.Id, supplier.Name, supplier.Phone },
            from, to, lines, 
            totalInvoiced = totalDebt, 
            totalPaid = totalCredit,
            outstanding = balance
        });
    }

    // ══════════════════════════════════════════════════════
    // 2. ديون العملاء (عمر الدين)
    // GET /api/operationalreports/customer-aging
    // ══════════════════════════════════════════════════════
    [HttpGet("customer-aging")]
    [Authorize(Roles = "Admin,Manager,Accountant")]
    public async Task<IActionResult> CustomerAging(
        [FromQuery] string?   search  = null,
        [FromQuery] DateTime? asOfDate = null,
        [FromQuery] bool      excel   = false)
    {
        var asOf = asOfDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

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

            // فقط المبيعات الآجلة حتى تاريخ asOf لتوزيع عمر الدين
            var creditOrders = c.Orders.OrderBy(o => o.CreatedAt).ToList();

            // حساب عمر الدين — توزيع الرصيد على الطلبات حسب أعمارها (LIFO logic for payments assumption)
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
    // 3. ديون الموردين
    // GET /api/operationalreports/supplier-aging
    // ══════════════════════════════════════════════════════
    [HttpGet("supplier-aging")]
    [Authorize(Roles = "Admin,Manager,Accountant")]
    public async Task<IActionResult> SupplierAging(
        [FromQuery] string?   search   = null,
        [FromQuery] DateTime? asOfDate = null,
        [FromQuery] bool      excel    = false)
    {
        var asOf = asOfDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();
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

                // الفواتير الآجلة حتى تاريخ asOf لتوزيع عمر الدين
                var creditInvoices = s.Invoices.OrderBy(i => i.InvoiceDate).ToList();

                decimal b = balance;
                decimal c30 = 0, c60 = 0, c90 = 0, c90p = 0;

                // توزيع الرصيد على الفواتير بحسب عمرها
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
        [FromQuery] bool    excel       = false)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);

        var cacheKey = $"Inventory_{search}_{categoryId}_{brandId}_{color}_{size}_{lowStock}_{stockStatus}_{page}_{pageSize}_{source}";
        if (!excel && _cache.TryGetValue(cacheKey, out var cachedData))
            return Ok(cachedData);

        var q = _db.Products
            .Include(p => p.Category)
            .Include(p => p.Variants)
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active || p.Status == ProductStatus.OutOfStock || p.Status == ProductStatus.Discontinued);

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
        var totalCount = await q.CountAsync();
        
        // جلب البيانات المطلوبة مع تفاصيلها
        var products = await q.OrderBy(p => p.CategoryId)
                             .ThenBy(p => p.NameAr)
                             .Skip((page - 1) * pageSize)
                             .Take(pageSize)
                             .ToListAsync();

        // حساب القيم الإجمالية للمجموعة المفلترة بالكامل (بدون تكرار الاستعلامات المكلفة)
        // ملاحظة: نستخدم الاستعلام q المفلتر قبل التقطيع (Skip/Take)
        var totals = await q.Select(p => new {
            TotalStock = p.Variants.Any() 
                ? p.Variants.Where(v => 
                    (string.IsNullOrEmpty(color) || v.Color == color || v.ColorAr == color) &&
                    (string.IsNullOrEmpty(size) || v.Size == size) &&
                    (stockStatus == "all" || (stockStatus == "positive" && v.StockQuantity > 0) || (stockStatus == "zero" && v.StockQuantity <= 0))
                ).Sum(v => v.StockQuantity)
                : p.TotalStock,
            p.Price,
            Cost = p.CostPrice ?? 0
        }).ToListAsync();

        var totalUnits     = totals.Sum(x => x.TotalStock);
        var lowStockCount  = totals.Count(x => x.TotalStock <= 5); // أو ReorderLevel
        var outOfStock     = totals.Count(x => x.TotalStock <= 0);
        var totalSalesVal  = totals.Sum(x => (decimal)x.TotalStock * x.Price);
        var totalCostVal   = totals.Sum(x => (decimal)x.TotalStock * x.Cost);

        var rows = products.Select(p =>
        {
            // Filter variants based on the same criteria as the main query
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

        // ✅ LEDGER RECONCILIATION FOR INVENTORY
        var maps = await _db.AccountSystemMappings.ToDictionaryAsync(m => m.Key, m => m.AccountId);
        var inventoryAccId = maps.GetValueOrDefault(MappingKeys.Inventory);
        decimal ledgerInventoryValue = 0;
        if (inventoryAccId != null) {
            var ledgerQ = _db.JournalLines
                .Where(l => l.AccountId == inventoryAccId && l.JournalEntry.Status == JournalEntryStatus.Posted);
            
            if (source.HasValue) ledgerQ = ledgerQ.Where(l => l.CostCenter == source.Value);

            ledgerInventoryValue = await ledgerQ.SumAsync(l => (decimal?)(l.Debit - l.Credit)) ?? 0;
        }

        var summary = new {
            totalFilteredProducts = totalCount,
            totalUnits            = totalUnits,
            lowStockCount         = lowStockCount,
            outOfStock            = outOfStock,
            totalSalesValue       = totalSalesVal,
            totalCostValue        = totalCostVal,
            ledgerInventoryValue  = ledgerInventoryValue, // Absolute Financial Truth
            valuationDifference   = ledgerInventoryValue - totalCostVal, // Gap between physical and books
            agingAlerts           = totals.Count(x => x.TotalStock > 0 && x.Cost > 0)
        };

        if (excel) return ExcelInventory(rows, summary);

        var result = new { 
            rows, 
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

    // ══════════════════════════════════════════════════════
    // 5. تقرير المبيعات
    // GET /api/operationalreports/sales?fromDate=&toDate=&source=
    // ══════════════════════════════════════════════════════
    [HttpGet("sales")]
    [Authorize(Roles = "Admin,Manager,Accountant")]
    public async Task<IActionResult> SalesReport(
        [FromQuery] DateTime?    fromDate   = null,
        [FromQuery] DateTime?    toDate     = null,
        [FromQuery] OrderSource? source     = null,
        [FromQuery] int?         categoryId = null,
        [FromQuery] int?         brandId    = null,
        [FromQuery] string?      color      = null,
        [FromQuery] string?      size       = null,
        [FromQuery] bool         excel      = false)
    {
        var from = fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

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

        var orders = await ordersQ
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
                Items = o.Items.Select(i => new {
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
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var salesReturnAccId = maps.GetValueOrDefault(MappingKeys.SalesReturn);
        var returnsQ = _db.JournalLines
            .Where(l => l.AccountId == salesReturnAccId && l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to && l.JournalEntry.Status == JournalEntryStatus.Posted);
        
        if (source.HasValue)
        {
            returnsQ = returnsQ.Where(l => l.CostCenter == source.Value);
        }

        decimal ledgerReturns = await returnsQ.SumAsync(l => (decimal?)l.Debit) ?? 0;

        var rows = orders.Select(o => {
            // ✅ Detailed payment string
            var paySummary = string.Join(", ", o.Payments.Select(p => $"{p.Method}: {p.Amount:N0}"));

            return new SalesRow(
                o.Id, o.OrderNumber, o.CreatedAt,
                o.CustomerName ?? "Walk-in",
                o.CustomerPhone ?? "",
                o.Source.ToString(),
                o.Status.ToString(),
                o.PaymentMethod.ToString(),
                o.SubTotal, o.DiscountAmount + o.TemporalDiscount, 
                o.PostedSales > 0 ? o.PostedSales : o.TotalAmount, // Use Ledger amount if possible
                o.Items.Sum(i => i.Quantity),
                o.Items.Select(i => new ReportItemDto(
                    i.ProductSKU,
                    i.ProductNameAr,
                    i.Size ?? "",
                    i.Color ?? "",
                    i.Quantity,
                    i.UnitPrice,
                    0, // UnitCost (optional in sales)
                    i.DiscountAmount / (i.Quantity > 0 ? i.Quantity : 1), // Per unit discount
                    i.TotalPrice
                )).ToList(),
                paySummary
            );
        }).ToList();

        var summary = new {
            totalOrders   = rows.Count,
            totalGrossRevenue  = rows.Sum(r => r.TotalAmount),
            totalDiscount      = rows.Sum(r => r.DiscountAmount),
            totalReturns       = ledgerReturns,
            totalNetRevenue    = rows.Sum(r => r.TotalAmount) - ledgerReturns,
            avgOrder      = rows.Count > 0 ? (rows.Sum(r => r.TotalAmount) - ledgerReturns) / rows.Count : 0,
            pos           = rows.Count(r => r.Source == "POS"),
            website       = rows.Count(r => r.Source == "Website")
        };

        if (excel) return ExcelSales(rows, summary, from, to);

        return Ok(new { from, to, rows, summary });
    }

    // ══════════════════════════════════════════════════════
    // 6. تقرير المشتريات
    // GET /api/operationalreports/purchases?fromDate=&toDate=&supplierId=
    // ══════════════════════════════════════════════════════
    [HttpGet("purchases")]
    [Authorize(Roles = "Admin,Manager,Accountant")]
    public async Task<IActionResult> PurchasesReport(
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] int?      supplierId = null,
        [FromQuery] int?      categoryId = null,
        [FromQuery] int?      brandId    = null,
        [FromQuery] string?   color      = null,
        [FromQuery] string?   size       = null,
        [FromQuery] OrderSource? source     = null,
        [FromQuery] bool      excel      = false)
    {
        var from = fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();
        _logger.LogInformation("Generating Purchases report from {From} to {To}", from, to);

        try
        {
            var maps = await _db.AccountSystemMappings.ToDictionaryAsync(m => m.Key, m => m.AccountId);
            var purchaseAccId = maps.GetValueOrDefault(MappingKeys.Purchase);

            // 1. Fetch Invoices joined with Posted JVs
            var q = _db.PurchaseInvoices.AsNoTracking()
                .Where(i => i.InvoiceDate >= from && i.InvoiceDate <= to)
                .Where(i => i.JournalEntries.Any(j => j.Status == JournalEntryStatus.Posted));

            if (supplierId.HasValue) q = q.Where(i => i.SupplierId == supplierId.Value);
            if (source.HasValue) q = q.Where(i => i.CostCenter == source.Value);

            var invoices = await q
                .Select(i => new {
                    i.Id, i.InvoiceNumber, i.SupplierInvoiceNumber, i.InvoiceDate,
                    SupplierName = i.Supplier != null ? i.Supplier.Name : "N/A",
                    i.PaymentTerms, i.Status, i.SubTotal, i.TaxAmount, i.TotalAmount, i.ReturnedAmount, i.PaidAmount,
                    Items = i.Items.Select(it => new {
                        ProductSKU = it.Product != null ? it.Product.SKU : "",
                        ProductNameAr = it.Product != null ? it.Product.NameAr : it.Description,
                        Size = it.ProductVariant != null ? it.ProductVariant.Size : "",
                        ColorAr = it.ProductVariant != null ? it.ProductVariant.ColorAr : (it.ProductVariant != null ? it.ProductVariant.Color : ""),
                        it.Quantity, it.UnitCost, it.TotalCost
                    }).ToList(),
                    LedgerPostedAmount = i.JournalEntries
                        .Where(j => j.Status == JournalEntryStatus.Posted)
                        .SelectMany(j => j.Lines)
                        .Where(l => l.AccountId == purchaseAccId)
                        .Sum(l => l.Debit)
                })
                .OrderByDescending(i => i.InvoiceDate).ToListAsync();

            var rows = invoices.Select(i => new PurchaseRow(
                i.Id, i.InvoiceNumber, i.SupplierInvoiceNumber ?? "",
                i.SupplierName, i.InvoiceDate,
                i.PaymentTerms.ToString(), i.Status.ToString(),
                i.SubTotal, i.TaxAmount, 
                i.LedgerPostedAmount > 0 ? i.LedgerPostedAmount : i.TotalAmount,
                i.ReturnedAmount,
                i.PaidAmount, i.TotalAmount - i.PaidAmount - i.ReturnedAmount,
                i.Items.Select(it => new ReportItemDto(
                    it.ProductSKU, it.ProductNameAr, it.Size, it.ColorAr,
                    it.Quantity, 0, it.UnitCost, 0, it.TotalCost
                )).ToList()
            )).ToList();

            var summary = new {
                totalInvoices  = rows.Count,
                totalGross     = rows.Sum(r => r.TotalAmount),
                totalReturned  = rows.Sum(r => r.ReturnedAmount),
                totalNet       = rows.Sum(r => r.TotalAmount - r.ReturnedAmount),
                totalPaid      = rows.Sum(r => r.PaidAmount),
                totalRemaining = rows.Sum(r => r.RemainingAmount),
            };

            if (excel) return ExcelPurchases(rows, summary, from, to);

            return Ok(new { from, to, rows, summary });
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
    [Authorize(Roles = "Admin,Manager,Accountant")]
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

        // Get all SalesReturn journal entries
        var returnsQ = _db.JournalEntries.AsNoTracking()
            .Where(j => j.Type == JournalEntryType.SalesReturn 
                     && j.EntryDate >= from && j.EntryDate <= to);

        if (source.HasValue)
        {
            returnsQ = returnsQ.Where(j => j.Order != null && j.Order.Source == source.Value);
        }

        if (categoryId.HasValue && categoryId > 0)
        {
            var categoryIds = await FilterHelper.GetCategoryFamilyIds(_db, categoryId);
            returnsQ = returnsQ.Where(j => j.Order != null && j.Order.Items.Any(it => it.Product != null && it.Product.CategoryId.HasValue && categoryIds.Contains(it.Product.CategoryId.Value)));
        }

        if (brandId.HasValue && brandId > 0)
        {
            var brandIds = await FilterHelper.GetBrandFamilyIds(_db, brandId);
            returnsQ = returnsQ.Where(j => j.Order != null && j.Order.Items.Any(it => it.Product != null && it.Product.BrandId.HasValue && brandIds.Contains(it.Product.BrandId.Value)));
        }

        if (!string.IsNullOrEmpty(color))
            returnsQ = returnsQ.Where(j => j.Order != null && j.Order.Items.Any(it => it.Color == color || (it.Product != null && it.Product.Variants.Any(v => v.Color == color || v.ColorAr == color))));

        if (!string.IsNullOrEmpty(size))
            returnsQ = returnsQ.Where(j => j.Order != null && j.Order.Items.Any(it => it.Size == size || (it.Product != null && it.Product.Variants.Any(v => v.Size == size))));

        var returns = await returnsQ
            .Select(j => new {
                j.Reference, j.EntryNumber, j.EntryDate,
                CustomerName = j.Order != null && j.Order.Customer != null ? j.Order.Customer.FullName : "Walk-in",
                CustomerPhone = j.Order != null && j.Order.Customer != null ? j.Order.Customer.Phone : "",
                Amount = j.Lines.Where(l => l.Debit > 0).Sum(l => l.Debit),
                j.Description,
                Items = j.Order != null ? j.Order.Items.Where(i => i.ReturnedQuantity > 0).Select(i => new {
                    ProductSKU = i.Product != null ? i.Product.SKU : "",
                    ProductNameAr = i.Product != null ? i.Product.NameAr : i.ProductNameAr,
                    i.Size, i.Color, i.ReturnedQuantity, i.UnitPrice, i.Quantity, i.DiscountAmount
                }).ToList() : null
            })
            .OrderByDescending(j => j.EntryDate).ToListAsync();

        var rows = returns.Select(j => new ReturnRow(
            j.Reference ?? j.EntryNumber, j.EntryDate,
            j.CustomerName,
            j.CustomerPhone ?? "",
            j.Amount,
            j.Description ?? "",
            j.Items?.Select(i => new ReportItemDto(
                i.ProductSKU,
                i.ProductNameAr,
                i.Size ?? "",
                i.Color ?? "",
                i.ReturnedQuantity,
                i.UnitPrice,
                0,
                i.DiscountAmount / (i.Quantity > 0 ? i.Quantity : 1),
                (i.UnitPrice - (i.DiscountAmount / (i.Quantity > 0 ? i.Quantity : 1))) * i.ReturnedQuantity
            )).ToList()
        )).ToList();

        var summary = new {
            count        = rows.Count,
            totalAmount  = rows.Sum(r => r.Amount),
        };

        if (excel) return ExcelReturns(rows, summary, from, to, "مرتجعات المبيعات");

        return Ok(new { from, to, rows, summary });
    }

    // ══════════════════════════════════════════════════════
    // 8. مرتجعات المشتريات
    // GET /api/operationalreports/purchase-returns
    // ══════════════════════════════════════════════════════
    [HttpGet("purchase-returns")]
    [Authorize(Roles = "Admin,Manager,Accountant")]
    public async Task<IActionResult> PurchaseReturns(
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

        // 🎯 FIX: Fetch from PurchaseReturns table to include Standalone returns
        var q = _db.PurchaseReturns.AsNoTracking()
            .Where(r => r.ReturnDate >= from && r.ReturnDate <= to);

        if (source.HasValue)
        {
            q = q.Where(r => r.CostCenter == source.Value);
        }

        if (categoryId.HasValue && categoryId > 0)
        {
            var categoryIds = await FilterHelper.GetCategoryFamilyIds(_db, categoryId);
            q = q.Where(r => r.Items.Any(it => it.Product != null && it.Product.CategoryId.HasValue && categoryIds.Contains(it.Product.CategoryId.Value)));
        }

        if (brandId.HasValue && brandId > 0)
        {
            var brandIds = await FilterHelper.GetBrandFamilyIds(_db, brandId);
            q = q.Where(r => r.Items.Any(it => it.Product != null && it.Product.BrandId.HasValue && brandIds.Contains(it.Product.BrandId.Value)));
        }

        if (!string.IsNullOrEmpty(color))
            q = q.Where(r => r.Items.Any(it => (it.ProductVariant != null && (it.ProductVariant.Color == color || it.ProductVariant.ColorAr == color)) || (it.Product != null && it.Product.Variants.Any(v => v.Color == color || v.ColorAr == color))));

        if (!string.IsNullOrEmpty(size))
            q = q.Where(r => r.Items.Any(it => (it.ProductVariant != null && it.ProductVariant.Size == size) || (it.Product != null && it.Product.Variants.Any(v => v.Size == size))));

        var returns = await q
            .Select(r => new {
                r.ReturnNumber, r.ReturnDate,
                SupplierName = r.Supplier != null ? r.Supplier.Name : "N/A",
                SupplierPhone = r.Supplier != null ? r.Supplier.Phone : "",
                r.TotalAmount, r.Notes,
                InvoiceNumber = r.Invoice != null ? r.Invoice.InvoiceNumber : null,
                Items = r.Items.Select(it => new {
                    ProductSKU = it.Product != null ? it.Product.SKU : "",
                    ProductNameAr = it.Product != null ? it.Product.NameAr : "",
                    Size = it.ProductVariant != null ? it.ProductVariant.Size : "",
                    ColorAr = it.ProductVariant != null ? (it.ProductVariant.ColorAr ?? it.ProductVariant.Color) : "",
                    it.Quantity, it.UnitCost, it.TotalCost
                }).ToList()
            })
            .OrderByDescending(r => r.ReturnDate).ToListAsync();

        var rows = returns.Select(r => new ReturnRow(
            r.ReturnNumber, r.ReturnDate,
            r.SupplierName, r.SupplierPhone,
            r.TotalAmount, 
            r.Notes ?? (r.InvoiceNumber != null ? $"مرتجع للفاتورة #{r.InvoiceNumber}" : "مرتجع مستقل"),
            r.Items.Select(it => new ReportItemDto(
                it.ProductSKU,
                it.ProductNameAr,
                it.Size,
                it.ColorAr ?? "",
                it.Quantity,
                0,
                it.UnitCost,
                0,
                it.TotalCost
            )).ToList()
        )).ToList();

        if (excel) return ExcelReturns(rows, new { count = rows.Count, totalAmount = rows.Sum(r => r.Amount) }, from, to, "مرتجعات المشتريات");

        return Ok(new { from, to, rows, summary = new { count = rows.Count, totalAmount = rows.Sum(r => r.Amount) } });
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

        var q = _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Where(o => o.Source == OrderSource.POS
                     && o.CreatedAt >= from && o.CreatedAt <= to
                     && !string.IsNullOrEmpty(o.SalesPersonId));

        if (!string.IsNullOrEmpty(userId))
            q = q.Where(o => o.SalesPersonId == userId);

        var orders = await q.OrderByDescending(o => o.CreatedAt).ToListAsync();

        // 🛡️ REFINEMENT: Resolve staff names from HR Employees or System Users
        var personIds = orders.Select(o => o.SalesPersonId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var numericIds = personIds.Where(id => int.TryParse(id, out _)).Select(id => int.Parse(id!)).ToList();
        
        var empNames = await _db.Employees
            .Where(e => numericIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id.ToString(), e => e.Name);

        var remainingIds = personIds.Where(id => id != null && !empNames.ContainsKey(id)).ToList();
        var userNamesResult = await _db.Users
            .Where(u => remainingIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        // Merge maps (Priority: Employees > Users)
        var personNamesMap = empNames;
        foreach (var un in userNamesResult) if (!personNamesMap.ContainsKey(un.Key)) personNamesMap[un.Key] = un.Value;

        // Group by sales person (bundle unknowns together)
        var byPerson = orders
            .GroupBy(o => o.SalesPersonId != null && personNamesMap.ContainsKey(o.SalesPersonId) ? o.SalesPersonId : "Unknown")
            .Select(g =>
            {
                var ordersList = g.ToList();
                var grossSales = ordersList.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount + o.DiscountAmount);
                
                // Calculate Total Returns Amount correctly handling partial returns
                decimal totalReturns = 0;
                foreach(var o in ordersList.Where(o => o.Status != OrderStatus.Cancelled))
                {
                    if (o.Status == OrderStatus.Returned)
                    {
                        totalReturns += (o.TotalAmount + o.DiscountAmount); // The gross returned
                    }
                    else if (o.Items.Any(i => i.ReturnedQuantity > 0))
                    {
                        // Partial returns or orders with returned items
                        foreach(var item in o.Items.Where(i => i.ReturnedQuantity > 0))
                        {
                            decimal lineReturn = (item.Quantity > 0) 
                                ? (item.TotalPrice / item.Quantity) * item.ReturnedQuantity 
                                : 0;
                            // Pro-rate discount return if necessary, but returning the raw line amount is roughly "Gross Returns" 
                            totalReturns += lineReturn;
                        }
                    }
                }

                var totalDiscount = ordersList.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.DiscountAmount);
                var netSales = grossSales - totalReturns - totalDiscount;

                return new UserActivityRow(
                    g.Key,
                    personNamesMap.GetValueOrDefault(g.Key, "System/Unknown"),
                    ordersList.Count,
                    grossSales,
                    totalReturns,
                    totalDiscount,
                    netSales,
                    ordersList.Count(o => o.Status == OrderStatus.Cancelled)
                );
            })
            .OrderByDescending(r => r.NetSales)
            .ToList();

        var detail = orders.Select(o => new {
            o.OrderNumber, o.CreatedAt, o.SalesPersonId,
            SalesPersonName = (o.SalesPersonId != null && personNamesMap.TryGetValue(o.SalesPersonId, out var name)) ? name : "Unknown",
            CustomerName = o.Customer?.FullName ?? "",
            o.TotalAmount,
            o.DiscountAmount,
            Status = o.Status.ToString(),
            ItemCount = o.Items.Sum(i => i.Quantity),
            Items = o.Items.Select(it => new {
                it.Product?.SKU,
                ProductName = it.Product?.NameAr ?? it.ProductNameAr,
                it.Size,
                it.Color,
                it.Quantity,
                it.UnitPrice,
                it.DiscountAmount,
                it.TotalPrice
            }).ToList()
        }).ToList();

        if (excel) return ExcelUserActivity(byPerson, detail, from, to);

        return Ok(new { from, to, summary = byPerson, detail });
    }

    // ══════════════════════════════════════════════════════
    // 10. حركة صنف
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
        [FromQuery] bool      excel      = false)
    {
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
                .Where(m => m.CreatedAt >= from && m.CreatedAt <= to);

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
                movementsQuery = movementsQuery.Where(m => (m.ProductVariant != null && (m.ProductVariant.Color == color || m.ProductVariant.ColorAr == color)) || (m.Product != null && m.Product.Variants.Any(v => v.Color == color || v.ColorAr == color)));

            if (!string.IsNullOrEmpty(size))
                movementsQuery = movementsQuery.Where(m => (m.ProductVariant != null && m.ProductVariant.Size == size) || (m.Product != null && m.Product.Variants.Any(v => v.Size == size)));

            var dbMovements = await movementsQuery
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();

            var orderRefs = dbMovements.Where(m => m.Type == InventoryMovementType.Sale || m.Type == InventoryMovementType.ReturnIn).Select(m => m.Reference).Distinct().ToList();
            var purchaseRefs = dbMovements.Where(m => m.Type == InventoryMovementType.Purchase || m.Type == InventoryMovementType.ReturnOut).Select(m => m.Reference).Distinct().ToList();

            var orderMap = await _db.Orders.AsNoTracking().Where(o => orderRefs.Contains(o.OrderNumber)).ToDictionaryAsync(o => o.OrderNumber, o => o.Id);
            var purchaseMap = await _db.PurchaseInvoices.AsNoTracking().Where(i => purchaseRefs.Contains(i.InvoiceNumber)).ToDictionaryAsync(i => i.InvoiceNumber, i => i.Id);

            // 🛡️ REFINEMENT: Resolve staff names for the report
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
                    (m.CreatedByUserId != null && personNamesMap.TryGetValue(m.CreatedByUserId, out var creator)) ? creator : "System",
                    "Completed",
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

            return Ok(new
            {
                productId = productId,
                productName = productBrief,
                from,
                to,
                rows = movements,
                movements,
                summary
            });
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
        [FromQuery] int?      productId = null,
        [FromQuery] int?      variantId = null,
        [FromQuery] DateTime? fromDate  = null,
        [FromQuery] DateTime? toDate    = null,
        [FromQuery] OrderSource? source = null,
        [FromQuery] InventoryMovementType? type = null)
    {
        var from = fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate ?? TimeHelper.GetEgyptTime();

        var q = _db.InventoryMovements
            .Include(m => m.Product)
            .Include(m => m.ProductVariant)
            .Where(m => m.CreatedAt >= from && m.CreatedAt <= to)
            .AsQueryable();

        if (productId.HasValue) q = q.Where(m => m.ProductId == productId.Value);
        if (variantId.HasValue) q = q.Where(m => m.ProductVariantId == variantId.Value);
        if (source.HasValue)    q = q.Where(m => m.CostCenter == source.Value);
        if (type.HasValue)      q = q.Where(m => m.Type == type.Value);

        var items = await q.OrderByDescending(m => m.CreatedAt).ToListAsync();

        var rows = items.Select(m => new {
            m.Id,
            m.CreatedAt,
            productName = m.Product?.NameAr,
            sku         = m.Product?.SKU,
            variant     = m.ProductVariant != null ? $"{m.ProductVariant.Size} {m.ProductVariant.ColorAr}" : "أساسي",
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
        _ => type.ToString()
    };

    // ══════════════════════════════════════════════════════
    // EXCEL HELPERS
    // ══════════════════════════════════════════════════════
    private IActionResult ExcelCustomerStatement(Customer c, List<CustomerStatementLine> lines, decimal invoiced, decimal paid, decimal outstanding, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("كشف حساب عميل");
        ws.RightToLeft = true;
        ws.Cell(1,1).Value = $"كشف حساب: {c.FullName} | {c.Phone}";
        ws.Cell(1,1).Style.Font.Bold = true; ws.Cell(1,1).Style.Font.FontSize = 13;
        ws.Cell(2,1).Value = $"من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";

        string[] h = {"التاريخ","النوع","المرجع","البيان","مدين","دائن","الرصيد"};
        for (int i=0;i<h.Length;i++){ws.Cell(3,i+1).Value=h[i];ws.Cell(3,i+1).Style.Font.Bold=true;ws.Cell(3,i+1).Style.Fill.BackgroundColor=XLColor.FromHtml("#1a237e");ws.Cell(3,i+1).Style.Font.FontColor=XLColor.White;}

        int r=4;
        foreach(var l in lines){ws.Cell(r,1).Value=l.Date.ToString("yyyy-MM-dd");ws.Cell(r,2).Value=l.Type;ws.Cell(r,3).Value=l.Reference;ws.Cell(r,4).Value=l.Description;ws.Cell(r,5).Value=l.Debit;ws.Cell(r,6).Value=l.Credit;ws.Cell(r,7).Value=l.Balance;for(int c2=5;c2<=7;c2++)ws.Cell(r,c2).Style.NumberFormat.Format="#,##0.00";r++;}

        ws.Cell(r,4).Value="الإجمالي";ws.Cell(r,4).Style.Font.Bold=true;ws.Cell(r,5).Value=invoiced;ws.Cell(r,6).Value=paid;ws.Cell(r,7).Value=outstanding;for(int c2=5;c2<=7;c2++){ws.Cell(r,c2).Style.Font.Bold=true;ws.Cell(r,c2).Style.NumberFormat.Format="#,##0.00";}
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"customer_{c.Id}_{from:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelCustomerAging(List<CustomerAgingRow> rows, DateTime asOf)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ديون العملاء");
        ws.RightToLeft = true;
        ws.Cell(1,1).Value = $"تقرير ديون العملاء — حتى {asOf:yyyy-MM-dd}";
        ws.Cell(1,1).Style.Font.Bold = true; ws.Cell(1,1).Style.Font.FontSize = 13;
        ws.Range(1,1,1,7).Merge();

        string[] h = {"اسم العميل","التليفون","الإجمالي","0-30 يوم","31-60 يوم","61-90 يوم","أكثر من 90"};
        for(int i=0;i<h.Length;i++){ws.Cell(2,i+1).Value=h[i];ws.Cell(2,i+1).Style.Font.Bold=true;ws.Cell(2,i+1).Style.Fill.BackgroundColor=XLColor.FromHtml("#1a237e");ws.Cell(2,i+1).Style.Font.FontColor=XLColor.White;}

        int r=3;
        foreach(var row in rows){ws.Cell(r,1).Value=row.Name;ws.Cell(r,2).Value=row.Phone;ws.Cell(r,3).Value=row.Total;ws.Cell(r,4).Value=row.Current;ws.Cell(r,5).Value=row.Days60;ws.Cell(r,6).Value=row.Days90;ws.Cell(r,7).Value=row.Over90;for(int c=3;c<=7;c++)ws.Cell(r,c).Style.NumberFormat.Format="#,##0.00";if(row.Over90>0)ws.Row(r).Style.Font.FontColor=XLColor.Red;r++;}

        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"customer_aging_{asOf:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelSupplierAging(List<SupplierAgingRow> rows, DateTime asOf)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ديون الموردين");
        ws.RightToLeft = true;
        ws.Cell(1,1).Value = $"تقرير ديون الموردين — حتى {asOf:yyyy-MM-dd}";
        ws.Cell(1,1).Style.Font.Bold = true; ws.Cell(1,1).Style.Font.FontSize = 13;

        string[] h = {"المورد","التليفون","الشركة","الإجمالي","0-30","31-60","61-90","أكثر من 90"};
        for(int i=0;i<h.Length;i++){ws.Cell(2,i+1).Value=h[i];ws.Cell(2,i+1).Style.Font.Bold=true;ws.Cell(2,i+1).Style.Fill.BackgroundColor=XLColor.FromHtml("#c62828");ws.Cell(2,i+1).Style.Font.FontColor=XLColor.White;}

        int r=3;
        foreach(var row in rows){ws.Cell(r,1).Value=row.Name;ws.Cell(r,2).Value=row.Phone;ws.Cell(r,3).Value=row.CompanyName;ws.Cell(r,4).Value=row.Total;ws.Cell(r,5).Value=row.Current;ws.Cell(r,6).Value=row.Days60;ws.Cell(r,7).Value=row.Days90;ws.Cell(r,8).Value=row.Over90;for(int c=4;c<=8;c++)ws.Cell(r,c).Style.NumberFormat.Format="#,##0.00";r++;}
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"supplier_aging_{asOf:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelSupplierStatement(Supplier s, List<CustomerStatementLine> lines, decimal invoiced, decimal paid, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("كشف حساب مورد");
        ws.RightToLeft = true;
        ws.Cell(1,1).Value = $"كشف حساب مورد: {s.Name} | {s.Phone}";
        ws.Cell(1,1).Style.Font.Bold = true;
        ws.Cell(2,1).Value = $"من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";

        string[] h = {"التاريخ","النوع","المرجع","البيان","مدين (مشتريات)","دائن (مدفوعات)","الرصيد"};
        for (int i=0;i<h.Length;i++){ws.Cell(3,i+1).Value=h[i];ws.Cell(3,i+1).Style.Font.Bold=true;ws.Cell(3,i+1).Style.Fill.BackgroundColor=XLColor.FromHtml("#c62828");ws.Cell(3,i+1).Style.Font.FontColor=XLColor.White;}

        int r=4;
        foreach(var l in lines){ws.Cell(r,1).Value=l.Date.ToString("yyyy-MM-dd");ws.Cell(r,2).Value=l.Type;ws.Cell(r,3).Value=l.Reference;ws.Cell(r,4).Value=l.Description;ws.Cell(r,5).Value=l.Debit;ws.Cell(r,6).Value=l.Credit;ws.Cell(r,7).Value=l.Balance;for(int c=5;c<=7;c++)ws.Cell(r,c).Style.NumberFormat.Format="#,##0.00";r++;}

        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"supplier_statement_{s.Id}_{from:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelInventory(List<InventoryRow> rows, dynamic summary)
    {
        using var wb = new XLWorkbook();
        var ws1 = wb.Worksheets.Add("ملخص المخزون");
        ws1.RightToLeft = true;
        string[] h1 = {"الاسم عربي","SKU","الفئة","السعر","سعر البيع","إجمالي المخزون","قيمة المخزون"};
        for(int i=0;i<h1.Length;i++){ws1.Cell(1,i+1).Value=h1[i];ws1.Cell(1,i+1).Style.Font.Bold=true;ws1.Cell(1,i+1).Style.Fill.BackgroundColor=XLColor.FromHtml("#1b5e20");ws1.Cell(1,i+1).Style.Font.FontColor=XLColor.White;}
        int r=2;
        foreach(var row in rows){ws1.Cell(r,1).Value=row.NameAr;ws1.Cell(r,2).Value=row.SKU;ws1.Cell(r,3).Value=row.CategoryName;ws1.Cell(r,4).Value=row.Price;ws1.Cell(r,5).Value=row.Price;ws1.Cell(r,6).Value=row.TotalStock;ws1.Cell(r,7).Value=row.TotalValue;ws1.Cell(r,4).Style.NumberFormat.Format="#,##0.00";ws1.Cell(r,5).Style.NumberFormat.Format="#,##0.00";ws1.Cell(r,7).Style.NumberFormat.Format="#,##0.00";if(row.TotalStock<=5)ws1.Row(r).Style.Fill.BackgroundColor=XLColor.FromHtml("#fff3e0");if(row.TotalStock==0)ws1.Row(r).Style.Fill.BackgroundColor=XLColor.FromHtml("#ffebee");r++;}
        ws1.Columns().AdjustToContents();

        var ws2 = wb.Worksheets.Add("تفاصيل المقاسات");
        ws2.RightToLeft = true;
        string[] h2 = {"المنتج","SKU","المقاس","اللون","المخزون","السعر","القيمة"};
        for(int i=0;i<h2.Length;i++){ws2.Cell(1,i+1).Value=h2[i];ws2.Cell(1,i+1).Style.Font.Bold=true;}
        int r2=2;
        foreach(var p in rows)foreach(var v in p.Variants){ws2.Cell(r2,1).Value=p.NameAr;ws2.Cell(r2,2).Value=p.SKU;ws2.Cell(r2,3).Value=v.Size;ws2.Cell(r2,4).Value=v.Color;ws2.Cell(r2,5).Value=v.StockQuantity;ws2.Cell(r2,6).Value=v.Price;ws2.Cell(r2,7).Value=v.Value;ws2.Cell(r2,6).Style.NumberFormat.Format="#,##0.00";ws2.Cell(r2,7).Style.NumberFormat.Format="#,##0.00";r2++;}
        ws2.Columns().AdjustToContents();

        return ExcelResult(wb, $"inventory_{TimeHelper.GetEgyptTime():yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelSales(List<SalesRow> rows, dynamic summary, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("تقرير المبيعات بالتفاصيل");
        ws.RightToLeft = true;
        ws.Cell(1,1).Value=$"تقرير المبيعات التفصيلي — من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";
        ws.Cell(1,1).Style.Font.Bold=true;
        
        string[] h={"رقم الطلب","التاريخ","العميل","التليفون","المصدر","الحالة","الدفع","تفاصيل الدفع","كود الصنف","اسم الصنف","المقاس","اللون","الكمية","سعر الوحدة","خصم البند","إجمالي البند","خصم الفاتورة (كوبون)","إجمالي الفاتورة"};
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
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"detailed_sales_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelPurchases(List<PurchaseRow> rows, dynamic summary, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("تقرير المشتريات بالتفاصيل");
        ws.RightToLeft = true;
        
        string[] h={"رقم الفاتورة","فاتورة المورد","المورد","التاريخ","شروط الدفع","الحالة","كود الصنف","اسم الصنف","المقاس","اللون","الكمية","التكلفة","الإجمالي"};
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
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"detailed_purchases_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelReturns(List<ReturnRow> rows, dynamic summary, DateTime from, DateTime to, string title)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(title);
        ws.RightToLeft = true;
        ws.Cell(1,1).Value=$"{title} التفصيلي — من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";
        ws.Cell(1,1).Style.Font.Bold=true;
        
        string[] h={"الرقم","التاريخ","الاسم","التليفون","كود الصنف","اسم الصنف","المقاس","اللون","الكمية","المبلغ","السبب"};
        for(int i=0;i<h.Length;i++){
            var cell = ws.Cell(2,i+1);
            cell.Value=h[i];
            cell.Style.Font.Bold=true;
            cell.Style.Fill.BackgroundColor=XLColor.FromHtml("#c62828");
            cell.Style.Font.FontColor=XLColor.White;
        }

        int r=3;
        foreach(var ret in rows)
        {
            if (ret.Items == null || ret.Items.Count == 0)
            {
                ws.Cell(r, 1).Value = ret.Reference;
                ws.Cell(r, 2).Value = ret.Date.ToString("yyyy-MM-dd");
                ws.Cell(r, 3).Value = ret.Name;
                ws.Cell(r, 4).Value = ret.Phone;
                ws.Cell(r, 10).Value = ret.Amount;
                ws.Cell(r, 11).Value = ret.Reason;
                r++;
                continue;
            }

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
                ws.Cell(r, 11).Value = ret.Reason;
                
                ws.Cell(r, 10).Style.NumberFormat.Format="#,##0.00";
                r++;
            }
        }
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"detailed_returns_{from:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelUserActivity(List<UserActivityRow> summary, dynamic detail, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        
        // Unified Report Sheet
        var ws = wb.Worksheets.Add("أداء الكاشير التفصيلي");
        ws.RightToLeft = true;
        
        ws.Cell(1,1).Value=$"تقرير أداء الموظفين التفصيلي — من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";
        ws.Cell(1,1).Style.Font.Bold=true;

        string[] h = { "الموظف/الكاشير", "رقم الطلب", "التاريخ", "العميل", "الحالة", "كود الصنف", "اسم الصنف", "المقاس", "اللون", "الكمية", "السعر", "الخصم", "الإجمالي" };
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
            
            ws.Cell(r, 6).Value = "إجماليات:";
            ws.Cell(r, 7).Value = $"مبيعات: {user.GrossSales:N2} | مرتجعات: {user.TotalReturns:N2} | خصومات: {user.TotalDiscount:N2} | صافي: {user.NetSales:N2}";
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

        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"cashier_performance_{from:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelProductMovement(Product? p, List<ProductMovementLine> movements, dynamic summary, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("حركة الصنف");
        ws.RightToLeft = true;
        
        var title = p != null ? $"حركة صنف: {p.NameAr} ({p.SKU})" : "حركة جميع الأصناف";
        ws.Cell(1,1).Value = title;
        ws.Cell(1,1).Style.Font.Bold = true;
        ws.Cell(1,1).Style.Font.FontSize = 14;

        string[] h = { "التاريخ", "النوع", "المرجع", "الصنف", "الاسم/المورد", "التفاصيل", "وارد", "صادر", "المبلغ" };
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

        ws.Columns().AdjustToContents();
        var fileName = p != null ? $"product_{p.SKU}_{from:yyyyMMdd}.xlsx" : $"all_products_movement_{from:yyyyMMdd}.xlsx";
        return ExcelResult(wb, fileName);
    }

    private static FileStreamResult ExcelResult(XLWorkbook wb, string filename)
    {
        var tempFile = Path.GetTempFileName();
        wb.SaveAs(tempFile);
        var stream = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
        return new FileStreamResult(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet") { FileDownloadName = filename };
    }    // ══════════════════════════════════════════════════════
    // 12. تقرير الأصناف الراكدة (Stock Aging)
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
            .Where(p => p.TotalStock > 0 && (p.Status == ProductStatus.Active || p.Status == ProductStatus.OutOfStock));

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

    private string TranslateMovementType(InventoryMovementType type) => type switch
    {
        InventoryMovementType.Purchase => "مشتريات (+)",
        InventoryMovementType.Sale => "مبيعات (-)",
        InventoryMovementType.ReturnOut => "مرتجع مشتريات (-)",
        InventoryMovementType.ReturnIn => "مرتجع مبيعات (+)",
        InventoryMovementType.Adjustment => "تسوية مخزنية",
        InventoryMovementType.Audit => "جرد مخزني",
        InventoryMovementType.TransferIn => "تحويل للداخل (+)",
        InventoryMovementType.TransferOut => "تحويل للخارج (-)",
        InventoryMovementType.OpeningBalance => "رصيد أول المدة (+)",
        _ => type.ToString()
    };
}

// ── Report DTOs ──────────────────────────────────────────
public record CustomerStatementLine(DateTime Date, string Type, string Reference, string Description, decimal Debit, decimal Credit, decimal Balance);
public record CustomerAgingRow(int CustomerId, string Name, string Phone, decimal Total, decimal Current, decimal Days60, decimal Days90, decimal Over90);
public record SupplierAgingRow(int SupplierId, string Name, string Phone, string CompanyName, decimal Total, decimal Current, decimal Days60, decimal Days90, decimal Over90);
public record InventoryRow(int Id, string NameAr, string NameEn, string SKU, string CategoryName, decimal Price, decimal? DiscountPrice, decimal CostPrice, int TotalStock, decimal TotalValue, decimal TotalCostValue, List<VariantInventoryRow> Variants);
public record VariantInventoryRow(int Id, string Size, string Color, string ColorAr, int StockQuantity, decimal Price, decimal Value);
public record SalesRow(int Id, string OrderNumber, DateTime Date, string CustomerName, string Phone, string Source, string Status, string PaymentMethod, decimal SubTotal, decimal DiscountAmount, decimal TotalAmount, int ItemCount, List<ReportItemDto>? Items = null, string? PaymentDetails = null);
public record PurchaseRow(int Id, string InvoiceNumber, string SupplierInvoiceNumber, string SupplierName, DateTime InvoiceDate, string PaymentTerms, string Status, decimal SubTotal, decimal TaxAmount, decimal TotalAmount, decimal ReturnedAmount, decimal PaidAmount, decimal RemainingAmount, List<ReportItemDto>? Items = null);
public record ReturnRow(string Reference, DateTime Date, string Name, string Phone, decimal Amount, string Reason, List<ReportItemDto>? Items = null);
public record ReportItemDto(string SKU, string ProductName, string Size, string Color, decimal Quantity, decimal UnitPrice = 0, decimal UnitCost = 0, decimal Discount = 0, decimal LineTotal = 0);
public record UserActivityRow(string UserId, string UserName, int OrderCount, decimal GrossSales, decimal TotalReturns, decimal TotalDiscount, decimal NetSales, int Cancellations);
public record ProductMovementLine(DateTime Date, string Type, string Reference, string EntityName, string Details, int In, int Out, decimal Amount, string ProductName = "", string Source = "", string Status = "", string SKU = "", int Balance = 0, int? SourceId = null, string Size = "", string Color = "");
