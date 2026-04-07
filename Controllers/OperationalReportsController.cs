using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant,Staff")]
public class OperationalReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    public OperationalReportsController(AppDbContext db) => _db = db;

    // ══════════════════════════════════════════════════════
    // 1. كشف حساب عميل
    // GET /api/operationalreports/customer-statement?customerId=&fromDate=&toDate=
    // ══════════════════════════════════════════════════════
    [HttpGet("customer-statement")]
    public async Task<IActionResult> CustomerStatement(
        [FromQuery] int?      customerId = null,
        [FromQuery] string?   search     = null,
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] bool      excel      = false)
    {
        var from = fromDate ?? new DateTime(DateTime.UtcNow.Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? DateTime.UtcNow;

        // إذا بحث بالاسم ابحث أول
        if (customerId == null && !string.IsNullOrEmpty(search))
        {
            var found = await _db.Customers
                .Where(c => !c.IsDeleted && (c.FullName.Contains(search) || (c.Phone != null && c.Phone.Contains(search))))
                .Select(c => c.Id)
                .FirstOrDefaultAsync();
            if (found > 0) customerId = found;
        }

        if (customerId == null)
            return Ok(new { customers = await _db.Customers.Where(c => !c.IsDeleted).Select(c => new { c.Id, c.FullName, c.Phone, c.Email }).ToListAsync() });

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && !c.IsDeleted);
        if (customer == null) return NotFound();

        var orders = await _db.Orders
            .Where(o => o.CustomerId == customerId && !o.IsDeleted
                     && o.CreatedAt >= from && o.CreatedAt <= to
                     && o.Status != OrderStatus.Cancelled)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();

        // مدفوعات العميل من سندات القبض (إذا كان لديك JournalLines)
        var receipts = await _db.ReceiptVouchers
            .Where(r => r.CustomerId == customerId && !r.IsDeleted
                     && r.VoucherDate >= from && r.VoucherDate <= to)
            .OrderBy(r => r.VoucherDate)
            .ToListAsync();

        // بناء الكشف
        var lines = new List<CustomerStatementLine>();
        
        // 1. الرصيد قبل الفترة = (الافتتاحي للحساب) + (الفواتير السابقة) - (المقبوضات السابقة)
        decimal initialAccountBalance = (customer.MainAccountId != null) 
            ? (await _db.Accounts.Where(a => a.Id == customer.MainAccountId).Select(a => a.OpeningBalance).FirstOrDefaultAsync())
            : 0;

        decimal priorOrders = await _db.Orders
            .Where(o => o.CustomerId == customerId && !o.IsDeleted && o.CreatedAt < from && o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        decimal priorReceipts = await _db.ReceiptVouchers
            .Where(r => r.CustomerId == customerId && !r.IsDeleted && r.VoucherDate < from)
            .SumAsync(r => (decimal?)r.Amount) ?? 0;

        decimal balance = initialAccountBalance + priorOrders - priorReceipts;

        if (balance != 0)
        {
            lines.Add(new CustomerStatementLine(
                from.AddSeconds(-1), "رصيد", "OPENING",
                "رصيد مرحّل من الفترة السابقة", balance, 0, balance));
        }

        foreach (var o in orders)
        {
            balance += o.TotalAmount;
            lines.Add(new CustomerStatementLine(
                o.CreatedAt, "فاتورة", o.OrderNumber,
                $"طلب {o.FulfillmentType}", o.TotalAmount, 0, balance));
        }

        foreach (var r in receipts)
        {
            balance -= r.Amount;
            lines.Add(new CustomerStatementLine(
                r.VoucherDate, "سند قبض", r.VoucherNumber,
                r.Description ?? "دفعة", 0, r.Amount, balance));
        }

        lines = lines.OrderBy(l => l.Date).ToList();

        var totalInvoiced = orders.Sum(o => o.TotalAmount);
        var totalPaid     = receipts.Sum(r => r.Amount);
        var outstanding   = totalInvoiced - totalPaid;

        if (excel) return ExcelCustomerStatement(customer, lines, totalInvoiced, totalPaid, outstanding, from, to);

        return Ok(new {
            customer = new { customer.Id, customer.FullName, customer.Phone, customer.Email },
            from, to, lines,
            totalInvoiced, totalPaid, outstanding,
            hasBalance = outstanding > 0
        });
    }

    // ══════════════════════════════════════════════════════
    // 2. ديون العملاء (عمر الدين)
    // GET /api/operationalreports/customer-aging
    // ══════════════════════════════════════════════════════
    [HttpGet("customer-aging")]
    public async Task<IActionResult> CustomerAging(
        [FromQuery] string?   search  = null,
        [FromQuery] DateTime? asOfDate = null,
        [FromQuery] bool      excel   = false)
    {
        var asOf = asOfDate?.Date.AddDays(1).AddTicks(-1) ?? DateTime.UtcNow;

        var customers = await _db.Customers
            .Include(c => c.Orders)
            .Include(c => c.MainAccount)
            .Where(c => !c.IsDeleted)
            .ToListAsync();

        if (!string.IsNullOrEmpty(search))
            customers = customers.Where(c => c.FullName.Contains(search) || (c.Phone != null && c.Phone.Contains(search))).ToList();

        // المدفوعات
        var allReceipts = await _db.ReceiptVouchers
            .Where(r => !r.IsDeleted && r.CustomerId != null)
            .ToListAsync();

        var rows = new List<CustomerAgingRow>();

        foreach (var c in customers)
        {
            var opening  = (c.MainAccount != null ? c.MainAccount.OpeningBalance : 0);
            var invoiced = c.Orders.Where(o => !o.IsDeleted && o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount);
            var paid     = allReceipts.Where(r => r.CustomerId == c.Id).Sum(r => r.Amount);
            var balance  = opening + invoiced - paid;
            if (balance <= 0) continue;

            // حساب عمر الدين
            var unpaidOrders = c.Orders
                .Where(o => !o.IsDeleted && o.Status != OrderStatus.Cancelled)
                .OrderBy(o => o.CreatedAt)
                .ToList();

            decimal rem = balance;
            decimal c30 = 0, c60 = 0, c90 = 0, c90plus = 0;

            foreach (var o in unpaidOrders)
            {
                if (rem <= 0) break;
                var days = (asOf - o.CreatedAt).Days;
                var amt  = Math.Min(rem, o.TotalAmount);
                rem -= amt;
                if      (days <= 30) c30    += amt;
                else if (days <= 60) c60    += amt;
                else if (days <= 90) c90    += amt;
                else                 c90plus += amt;
            }

            rows.Add(new CustomerAgingRow(c.Id, c.FullName, c.Phone ?? "", balance, c30, c60, c90, c90plus));
        }

        rows = rows.OrderByDescending(r => r.Total).ToList();

        if (excel) return ExcelCustomerAging(rows, asOf);

        return Ok(new {
            asOf, rows,
            totals = new {
                total   = rows.Sum(r => r.Total),
                current = rows.Sum(r => r.Current),
                days60  = rows.Sum(r => r.Days60),
                days90  = rows.Sum(r => r.Days90),
                over90  = rows.Sum(r => r.Over90),
            }
        });
    }

    // ══════════════════════════════════════════════════════
    // 3. ديون الموردين
    // GET /api/operationalreports/supplier-aging
    // ══════════════════════════════════════════════════════
    [HttpGet("supplier-aging")]
    public async Task<IActionResult> SupplierAging(
        [FromQuery] string?   search   = null,
        [FromQuery] DateTime? asOfDate = null,
        [FromQuery] bool      excel    = false)
    {
        var asOf = asOfDate?.Date.AddDays(1).AddTicks(-1) ?? DateTime.UtcNow;

        var suppliers = await _db.Suppliers
            .Include(s => s.Invoices)
            .Include(s => s.Payments)
            .Where(s => !s.IsDeleted)
            .ToListAsync();

        if (!string.IsNullOrEmpty(search))
            suppliers = suppliers.Where(s => s.Name.Contains(search) || s.Phone.Contains(search)).ToList();

        var rows = suppliers
            .Where(s => s.Balance > 0)
            .Select(s =>
            {
                decimal b    = s.Balance;
                decimal c30 = 0, c60 = 0, c90 = 0, c90p = 0;

                foreach (var inv in s.Invoices.Where(i => !i.IsDeleted && i.RemainingAmount > 0).OrderBy(i => i.InvoiceDate))
                {
                    var days = (asOf - inv.InvoiceDate).Days;
                    var amt  = Math.Min(b, inv.RemainingAmount);
                    b -= amt;
                    if (b < 0) break;
                    if      (days <= 30) c30  += amt;
                    else if (days <= 60) c60  += amt;
                    else if (days <= 90) c90  += amt;
                    else                 c90p += amt;
                }

                return new SupplierAgingRow(
                    s.Id, s.Name, s.Phone,
                    s.CompanyName ?? "",
                    s.Balance, c30, c60, c90, c90p);
            })
            .Where(r => r.Total > 0)
            .OrderByDescending(r => r.Total)
            .ToList();

        if (excel) return ExcelSupplierAging(rows, asOf);

        return Ok(new {
            asOf, rows,
            totals = new {
                total   = rows.Sum(r => r.Total),
                current = rows.Sum(r => r.Current),
                days60  = rows.Sum(r => r.Days60),
                days90  = rows.Sum(r => r.Days90),
                over90  = rows.Sum(r => r.Over90),
            }
        });
    }

    // ══════════════════════════════════════════════════════
    // 4. تقرير المخزون (إجمالي + تفصيلي)
    // GET /api/operationalreports/inventory
    // ══════════════════════════════════════════════════════
    [HttpGet("inventory")]
    public async Task<IActionResult> Inventory(
        [FromQuery] string? search     = null,
        [FromQuery] int?    categoryId = null,
        [FromQuery] bool    lowStock   = false,
        [FromQuery] bool    excel      = false)
    {
        var q = _db.Products
            .Include(p => p.Category)
            .Include(p => p.Variants)
            .Where(p => !p.IsDeleted && p.Status == ProductStatus.Active);

        if (!string.IsNullOrEmpty(search))
            q = q.Where(p => p.NameAr.Contains(search) || p.SKU.Contains(search));
        if (categoryId.HasValue)
            q = q.Where(p => p.CategoryId == categoryId.Value);

        var products = await q.OrderBy(p => p.CategoryId).ThenBy(p => p.NameAr).ToListAsync();

        var rows = products.Select(p =>
        {
            var totalStock = p.Variants.Any(v => !v.IsDeleted)
                ? p.Variants.Where(v => !v.IsDeleted).Sum(v => v.StockQuantity)
                : p.TotalStock;

            var variants = p.Variants.Where(v => !v.IsDeleted).Select(v => new VariantInventoryRow(
                v.Id, v.Size ?? "", v.Color ?? "", v.ColorAr ?? "",
                v.StockQuantity,
                p.Price + (v.PriceAdjustment ?? 0),
                v.StockQuantity * (p.Price + (v.PriceAdjustment ?? 0))
            )).ToList();

            return new InventoryRow(
                p.Id, p.NameAr, p.NameEn, p.SKU,
                p.Category?.NameAr ?? "",
                p.Price, p.DiscountPrice,
                p.CostPrice ?? 0,
                totalStock,
                totalStock * p.Price, // Sales Value
                totalStock * (p.CostPrice ?? 0),           // Cost Value
                variants
            );
        }).ToList();

        if (lowStock) rows = rows.Where(r => r.TotalStock <= 5).ToList();

        var summary = new {
            totalProducts  = rows.Count,
            totalUnits     = rows.Sum(r => r.TotalStock),
            totalSalesValue = rows.Sum(r => r.TotalValue),
            totalCostValue  = rows.Sum(r => r.TotalCostValue),
            lowStockCount  = rows.Count(r => r.TotalStock <= 5),
            outOfStock     = rows.Count(r => r.TotalStock == 0),
        };

        if (excel) return ExcelInventory(rows, summary);

        return Ok(new { rows, summary });
    }

    // ══════════════════════════════════════════════════════
    // 5. تقرير المبيعات
    // GET /api/operationalreports/sales?fromDate=&toDate=&source=
    // ══════════════════════════════════════════════════════
    [HttpGet("sales")]
    public async Task<IActionResult> SalesReport(
        [FromQuery] DateTime?    fromDate = null,
        [FromQuery] DateTime?    toDate   = null,
        [FromQuery] OrderSource? source   = null,
        [FromQuery] bool         excel    = false)
    {
        var from = fromDate ?? new DateTime(DateTime.UtcNow.Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? DateTime.UtcNow;

        var q = _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Where(o => !o.IsDeleted
                     && o.Status != OrderStatus.Cancelled
                     && o.CreatedAt >= from && o.CreatedAt <= to);

        if (source.HasValue) q = q.Where(o => o.Source == source.Value);

        var orders = await q.OrderByDescending(o => o.CreatedAt).ToListAsync();
        
        // 🚨 GET RETURNS IN THE SAME PERIOD
        var returns = await _db.JournalEntries
            .Include(j => j.Lines)
            .Where(j => !j.IsDeleted && j.Type == JournalEntryType.SalesReturn && j.EntryDate >= from && j.EntryDate <= to)
            .ToListAsync();
        
        decimal totalReturnAmount = 0;
        foreach(var ret in returns)
        {
            // Sum all SalesReturn account debits
            // or just the total amount of the entry
            // Usually the SalesReturn line is the one we want to track as "Loss of Revenue"
             totalReturnAmount += ret.Lines.Where(l => l.Debit > 0).Sum(l => l.Debit);
        }

        var rows = orders.Select(o => new SalesRow(
            o.OrderNumber, o.CreatedAt,
            o.Customer?.FullName ?? "",
            o.Customer?.Phone ?? "",
            o.Source.ToString(),
            o.Status.ToString(),
            o.PaymentMethod.ToString(),
            o.SubTotal, o.DiscountAmount, o.TotalAmount,
            o.Items.Sum(i => i.Quantity)
        )).ToList();

        var summary = new {
            totalOrders   = rows.Count,
            totalGrossRevenue  = rows.Sum(r => r.TotalAmount),
            totalReturns       = totalReturnAmount,
            totalNetRevenue    = rows.Sum(r => r.TotalAmount) - totalReturnAmount,
            totalDiscount = rows.Sum(r => r.DiscountAmount),
            avgOrder      = rows.Count > 0 ? (rows.Sum(r => r.TotalAmount) - totalReturnAmount) / rows.Count : 0,
            website       = rows.Count(r => r.Source == "Website"),
            pos           = rows.Count(r => r.Source == "POS"),
        };

        if (excel) return ExcelSales(rows, summary, from, to);

        return Ok(new { from, to, rows, summary });
    }

    // ══════════════════════════════════════════════════════
    // 6. تقرير المشتريات
    // GET /api/operationalreports/purchases?fromDate=&toDate=&supplierId=
    // ══════════════════════════════════════════════════════
    [HttpGet("purchases")]
    public async Task<IActionResult> PurchasesReport(
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] int?      supplierId = null,
        [FromQuery] bool      excel      = false)
    {
        var from = fromDate ?? new DateTime(DateTime.UtcNow.Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? DateTime.UtcNow;

        var q = _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Include(i => i.Items)
            .Where(i => !i.IsDeleted && i.InvoiceDate >= from && i.InvoiceDate <= to);

        if (supplierId.HasValue) q = q.Where(i => i.SupplierId == supplierId.Value);

        var invoices = await q.OrderByDescending(i => i.InvoiceDate).ToListAsync();

        var rows = invoices.Select(i => new PurchaseRow(
            i.InvoiceNumber, i.SupplierInvoiceNumber ?? "",
            i.Supplier?.Name ?? "N/A", i.InvoiceDate,
            i.PaymentTerms.ToString(), i.Status.ToString(),
            i.SubTotal, i.TaxAmount, i.TotalAmount,
            i.PaidAmount, i.TotalAmount - i.PaidAmount
        )).ToList();

        var summary = new {
            totalInvoices  = rows.Count,
            totalAmount    = rows.Sum(r => r.TotalAmount),
            totalPaid      = rows.Sum(r => r.PaidAmount),
            totalRemaining = rows.Sum(r => r.RemainingAmount),
            totalTax       = rows.Sum(r => r.TaxAmount),
        };

        if (excel) return ExcelPurchases(rows, summary, from, to);

        return Ok(new { from, to, rows, summary });
    }

    // ══════════════════════════════════════════════════════
    // 7. مرتجعات المبيعات
    // GET /api/operationalreports/sales-returns
    // ══════════════════════════════════════════════════════
    [HttpGet("sales-returns")]
    public async Task<IActionResult> SalesReturns(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] bool      excel    = false)
    {
        var from = fromDate ?? new DateTime(DateTime.UtcNow.Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? DateTime.UtcNow;

        // Get all SalesReturn journal entries
        var returns = await _db.JournalEntries
            .Include(j => j.Lines)
            .Include(j => j.Order).ThenInclude(o => o!.Customer)
            .Where(j => !j.IsDeleted && j.Type == JournalEntryType.SalesReturn 
                     && j.EntryDate >= from && j.EntryDate <= to)
            .OrderByDescending(j => j.EntryDate)
            .ToListAsync();

        var rows = returns.Select(j => new ReturnRow(
            j.Reference ?? j.EntryNumber, j.EntryDate,
            j.Order?.Customer?.FullName ?? "Walk-in",
            j.Order?.Customer?.Phone ?? "",
            j.Lines.Where(l => l.Debit > 0).Sum(l => l.Debit), // The return amount (Revenue reversal part)
            j.Description ?? ""
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
    public async Task<IActionResult> PurchaseReturns(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] bool      excel    = false)
    {
        var from = fromDate ?? new DateTime(DateTime.UtcNow.Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? DateTime.UtcNow;

        var returns = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Where(i => !i.IsDeleted
                     && i.Status == PurchaseInvoiceStatus.Returned
                     && i.InvoiceDate >= from && i.InvoiceDate <= to)
            .OrderByDescending(i => i.InvoiceDate)
            .ToListAsync();

        var rows = returns.Select(i => new ReturnRow(
            i.InvoiceNumber, i.InvoiceDate,
            i.Supplier.Name, i.Supplier.Phone,
            i.TotalAmount, i.Notes ?? ""
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
        var from = fromDate ?? DateTime.UtcNow.Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? DateTime.UtcNow;

        var q = _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Where(o => !o.IsDeleted && o.Source == OrderSource.POS
                     && o.CreatedAt >= from && o.CreatedAt <= to
                     && !string.IsNullOrEmpty(o.SalesPersonId));

        if (!string.IsNullOrEmpty(userId))
            q = q.Where(o => o.SalesPersonId == userId);

        var orders = await q.OrderByDescending(o => o.CreatedAt).ToListAsync();

        // 🛡️ REFINEMENT: Fetch User Names for the summary
        var userIds = orders.Select(o => o.SalesPersonId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var userNames = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        // Group by sales person
        var byPerson = orders
            .GroupBy(o => o.SalesPersonId!)
            .Select(g => new UserActivityRow(
                g.Key,
                userNames.GetValueOrDefault(g.Key, "System/Unknown"),
                g.Count(),
                g.Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned).Sum(o => o.TotalAmount),
                g.Count(o => o.Status == OrderStatus.Returned),
                g.Count(o => o.Status == OrderStatus.Cancelled)
            ))
            .OrderByDescending(r => r.TotalSales)
            .ToList();

        var detail = orders.Select(o => new {
            o.OrderNumber, o.CreatedAt, o.SalesPersonId,
            CustomerName = o.Customer?.FullName ?? "",
            o.TotalAmount,
            Status = o.Status.ToString(),
            ItemCount = o.Items.Sum(i => i.Quantity)
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
        [FromQuery] int?      productId = null,
        [FromQuery] string?   search    = null,
        [FromQuery] DateTime? fromDate  = null,
        [FromQuery] DateTime? toDate    = null,
        [FromQuery] bool      excel     = false)
    {
        try
        {
            var from = fromDate ?? new DateTime(DateTime.UtcNow.Year, 1, 1).Date;
            var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? DateTime.UtcNow;

            // Handle search
            if (productId == null && !string.IsNullOrEmpty(search))
            {
                var p = await _db.Products
                    .Where(x => !x.IsDeleted && (x.NameAr.Contains(search) || x.SKU.Contains(search)))
                    .FirstOrDefaultAsync();
                if (p != null) productId = p.Id;
            }

            // If productId is null, return the choices list including "All"
            if (productId == null)
            {
                var pList = await _db.Products
                    .Where(p => !p.IsDeleted)
                    .Select(p => new { p.Id, p.NameAr, p.SKU })
                    .ToListAsync();
                
                // Add "All" option
                var result = new List<object> { new { Id = 0, NameAr = "الكل (جميع الأصناف)", SKU = "ALL" } };
                result.AddRange(pList.Cast<object>());
                
                return Ok(new { products = result });
            }

            // 1. مبيعات (Sales) — Include everything except Cancelled
            // We include Returned here so the initial sale shows up, and the return shows as a second entry
            var salesQuery = _db.OrderItems
                .Where(i => !i.IsDeleted && !i.Order.IsDeleted && i.Order.Status != OrderStatus.Cancelled
                         && i.Order.CreatedAt >= from && i.Order.CreatedAt <= to);

            if (productId > 0) salesQuery = salesQuery.Where(i => i.ProductId == productId);

            var salesMovements = await salesQuery
                .Select(i => new ProductMovementLine(
                    i.Order.CreatedAt,
                    "مبيعات",
                    i.Order.OrderNumber ?? "N/A",
                    i.Order.Customer.FullName,
                    (i.ProductVariant != null ? (i.ProductVariant.Size + " / " + i.ProductVariant.ColorAr) : "أساسي"),
                    0,
                    i.Quantity,
                    i.TotalPrice,
                    i.Product.NameAr,
                    i.Order.Source.ToString(),
                    i.Order.Status.ToString(),
                    i.Product.SKU,
                    0))
                .ToListAsync();

            // 2. مرتجع مبيعات (Sales Returns)
            // Ideally we'd use StatusHistory for refined date, but using Order.UpdatedAt (standard for status change) or CreatedAt
            var returnQuery = _db.OrderItems
                .Where(i => !i.IsDeleted && !i.Order.IsDeleted && i.Order.Status == OrderStatus.Returned
                         && i.Order.UpdatedAt >= from && i.Order.UpdatedAt <= to);

            if (productId > 0) returnQuery = returnQuery.Where(i => i.ProductId == productId);

            var returnMovements = await returnQuery
                .Select(i => new ProductMovementLine(
                    i.Order.UpdatedAt ?? i.Order.CreatedAt,
                    "مرتجع مبيعات",
                    i.Order.OrderNumber ?? "N/A",
                    i.Order.Customer.FullName,
                    (i.ProductVariant != null ? (i.ProductVariant.Size + " / " + i.ProductVariant.ColorAr) : "أساسي"),
                    i.Quantity,
                    0,
                    i.TotalPrice,
                    i.Product.NameAr,
                    i.Order.Source.ToString(),
                    i.Order.Status.ToString(),
                    i.Product.SKU,
                    0))
                .ToListAsync();

            // 3. مشتريات (Purchases) — Not Draft
            var purchaseQuery = _db.PurchaseInvoiceItems
                .Where(i => !i.IsDeleted && i.Invoice.Status != PurchaseInvoiceStatus.Draft
                         && i.Invoice.InvoiceDate >= from && i.Invoice.InvoiceDate <= to);

            if (productId > 0) purchaseQuery = purchaseQuery.Where(i => i.ProductId == productId);

            var purchaseMovements = await purchaseQuery
                .Select(i => new ProductMovementLine(
                    i.Invoice.InvoiceDate,
                    "مشتريات",
                    i.Invoice.InvoiceNumber ?? "N/A",
                    i.Invoice.Supplier.Name ?? "مورد غير معروف",
                    (i.ProductVariant != null ? (i.ProductVariant.Size + " / " + i.ProductVariant.ColorAr) : "أساسي"),
                    i.Quantity,
                    0,
                    i.TotalCost,
                    i.Product != null ? i.Product.NameAr : "Deleted Product",
                    "Supplier",
                    i.Invoice.Status.ToString(),
                    i.Product != null ? i.Product.SKU : "N/A",
                    0))
                .ToListAsync();

            // 4. مرتجع مشتريات (Purchase Returns)
            var purchaseReturnQuery = _db.PurchaseInvoiceItems
                .Where(i => !i.IsDeleted && i.Invoice.Status == PurchaseInvoiceStatus.Returned
                         && i.Invoice.UpdatedAt >= from && i.Invoice.UpdatedAt <= to);

            if (productId > 0) purchaseReturnQuery = purchaseReturnQuery.Where(i => i.ProductId == productId);

            var purchaseReturnMovements = await purchaseReturnQuery
                .Select(i => new ProductMovementLine(
                    i.Invoice.UpdatedAt ?? i.Invoice.InvoiceDate,
                    "مرتجع مشتريات",
                    i.Invoice.InvoiceNumber ?? "N/A",
                    i.Invoice.Supplier.Name ?? "مورد غير معروف",
                    (i.ProductVariant != null ? (i.ProductVariant.Size + " / " + i.ProductVariant.ColorAr) : "أساسي"),
                    0,
                    i.Quantity,
                    i.TotalCost,
                    i.Product != null ? i.Product.NameAr : "Deleted Product",
                    "Supplier",
                    i.Invoice.Status.ToString(),
                    i.Product != null ? i.Product.SKU : "N/A",
                    0))
                .ToListAsync();

            var movements = salesMovements
                .Concat(returnMovements)
                .Concat(purchaseMovements)
                .Concat(purchaseReturnMovements)
                .OrderBy(m => m.Date)
                .ToList();

            // Summary
            decimal currentStock = 0;
            string productBrief = "الكل";

            if (productId > 0)
            {
                var product = await _db.Products
                    .Include(p => p.Variants)
                    .FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted);
                if (product != null)
                {
                    currentStock = product.Variants?
                        .Where(v => !v.IsDeleted)
                        .Sum(v => v.StockQuantity) ?? 0;
                    productBrief = $"{product.NameAr} ({product.SKU})";
                }

                // Calculate exact running balance for each movement by starting from currentStock
                // Net change after the report 'to' date until now
                int netChangeAfterToDate = await _db.InventoryMovements
                        .Where(m => m.ProductId == productId && m.CreatedAt > to)
                        .SumAsync(m => m.Quantity);

                int runningBalance = (int)currentStock - netChangeAfterToDate;

                // Propagate backwards (from most recent to oldest in the report range)
                for (int i = movements.Count - 1; i >= 0; i--)
                {
                    var m = movements[i];
                    movements[i] = m with { Balance = runningBalance };
                    runningBalance -= (m.In - m.Out);
                }
            }
            else
            {
                // All products stock
                currentStock = await _db.ProductVariants
                    .Where(v => !v.IsDeleted && !v.Product.IsDeleted)
                    .SumAsync(v => (decimal)v.StockQuantity);
            }

            var summary = new
            {
                totalSold      = salesMovements.Sum(i => i.Out),
                totalReturned  = returnMovements.Sum(i => i.In),
                totalPurchased = purchaseMovements.Sum(i => i.In),
                totalPurchaseReturned = purchaseReturnMovements.Sum(i => i.Out),
                salesRevenue   = salesMovements.Sum(i => i.Amount),
                totalOrders    = salesMovements.Select(m => m.Reference).Distinct().Count(),
                totalDiscount  = 0, // Placeholder if needed, but revenue is post-discount usually 
                avgOrder       = salesMovements.Select(m => m.Reference).Distinct().Count() > 0 
                                 ? salesMovements.Sum(i => i.Amount) / salesMovements.Select(m => m.Reference).Distinct().Count() 
                                 : 0,
                currentStock
            };

            if (excel) return ExcelProductMovement(productId == 0 ? null : await _db.Products.FindAsync(productId), movements, summary, from, to);

            return Ok(new
            {
                productId = productId,
                productName = productBrief,
                from,
                to,
                movements,
                summary
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                message = "Error generating product movement report", 
                details = ex.Message
            });
        }
    }

    // ══════════════════════════════════════════════════════
    // 11. سجل حركات المخزن الشامل (Advanced Movement Ledger)
    // GET /api/operationalreports/stock-movements?productId=&fromDate=&toDate=&type=
    // ══════════════════════════════════════════════════════
    [HttpGet("stock-movements")]
    public async Task<IActionResult> StockMovementsLedger(
        [FromQuery] int?      productId = null,
        [FromQuery] int?      variantId = null,
        [FromQuery] DateTime? fromDate  = null,
        [FromQuery] DateTime? toDate    = null,
        [FromQuery] InventoryMovementType? type = null)
    {
        var from = fromDate ?? new DateTime(DateTime.UtcNow.Year, 1, 1);
        var to   = toDate ?? DateTime.UtcNow;

        var q = _db.InventoryMovements
            .Include(m => m.Product)
            .Include(m => m.ProductVariant)
            .Where(m => m.CreatedAt >= from && m.CreatedAt <= to)
            .AsQueryable();

        if (productId.HasValue) q = q.Where(m => m.ProductId == productId.Value);
        if (variantId.HasValue) q = q.Where(m => m.ProductVariantId == variantId.Value);
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

        return Ok(new { from, to, rows });
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
        foreach(var row in rows){ws.Cell(r,1).Value=row.CustomerName;ws.Cell(r,2).Value=row.Phone;ws.Cell(r,3).Value=row.Total;ws.Cell(r,4).Value=row.Current;ws.Cell(r,5).Value=row.Days60;ws.Cell(r,6).Value=row.Days90;ws.Cell(r,7).Value=row.Over90;for(int c=3;c<=7;c++)ws.Cell(r,c).Style.NumberFormat.Format="#,##0.00";if(row.Over90>0)ws.Row(r).Style.Font.FontColor=XLColor.Red;r++;}

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
        foreach(var row in rows){ws.Cell(r,1).Value=row.SupplierName;ws.Cell(r,2).Value=row.Phone;ws.Cell(r,3).Value=row.CompanyName;ws.Cell(r,4).Value=row.Total;ws.Cell(r,5).Value=row.Current;ws.Cell(r,6).Value=row.Days60;ws.Cell(r,7).Value=row.Days90;ws.Cell(r,8).Value=row.Over90;for(int c=4;c<=8;c++)ws.Cell(r,c).Style.NumberFormat.Format="#,##0.00";r++;}
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"supplier_aging_{asOf:yyyyMMdd}.xlsx");
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

        return ExcelResult(wb, $"inventory_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelSales(List<SalesRow> rows, dynamic summary, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("تقرير المبيعات");
        ws.RightToLeft = true;
        ws.Cell(1,1).Value=$"تقرير المبيعات — من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";ws.Cell(1,1).Style.Font.Bold=true;
        string[] h={"رقم الطلب","التاريخ","العميل","التليفون","المصدر","الحالة","الدفع","المجموع","الخصم","الإجمالي","عدد القطع"};
        for(int i=0;i<h.Length;i++){ws.Cell(2,i+1).Value=h[i];ws.Cell(2,i+1).Style.Font.Bold=true;ws.Cell(2,i+1).Style.Fill.BackgroundColor=XLColor.FromHtml("#1a237e");ws.Cell(2,i+1).Style.Font.FontColor=XLColor.White;}
        int r=3;
        foreach(var row in rows){ws.Cell(r,1).Value=row.OrderNumber;ws.Cell(r,2).Value=row.Date.ToString("yyyy-MM-dd");ws.Cell(r,3).Value=row.CustomerName;ws.Cell(r,4).Value=row.Phone;ws.Cell(r,5).Value=row.Source;ws.Cell(r,6).Value=row.Status;ws.Cell(r,7).Value=row.PaymentMethod;ws.Cell(r,8).Value=row.SubTotal;ws.Cell(r,9).Value=row.DiscountAmount;ws.Cell(r,10).Value=row.TotalAmount;ws.Cell(r,11).Value=row.ItemCount;ws.Cell(r,8).Style.NumberFormat.Format="#,##0.00";ws.Cell(r,9).Style.NumberFormat.Format="#,##0.00";ws.Cell(r,10).Style.NumberFormat.Format="#,##0.00";r++;}
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"sales_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelPurchases(List<PurchaseRow> rows, dynamic summary, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("تقرير المشتريات");
        ws.RightToLeft = true;
        string[] h={"رقم الفاتورة","فاتورة المورد","المورد","التاريخ","شروط الدفع","الحالة","المجموع","الضريبة","الإجمالي","المدفوع","المتبقي"};
        for(int i=0;i<h.Length;i++){ws.Cell(1,i+1).Value=h[i];ws.Cell(1,i+1).Style.Font.Bold=true;ws.Cell(1,i+1).Style.Fill.BackgroundColor=XLColor.FromHtml("#e65100");ws.Cell(1,i+1).Style.Font.FontColor=XLColor.White;}
        int r=2;
        foreach(var row in rows){ws.Cell(r,1).Value=row.InvoiceNumber;ws.Cell(r,2).Value=row.SupplierInvoiceNumber;ws.Cell(r,3).Value=row.SupplierName;ws.Cell(r,4).Value=row.InvoiceDate.ToString("yyyy-MM-dd");ws.Cell(r,5).Value=row.PaymentTerms;ws.Cell(r,6).Value=row.Status;ws.Cell(r,7).Value=row.SubTotal;ws.Cell(r,8).Value=row.TaxAmount;ws.Cell(r,9).Value=row.TotalAmount;ws.Cell(r,10).Value=row.PaidAmount;ws.Cell(r,11).Value=row.RemainingAmount;for(int c=7;c<=11;c++)ws.Cell(r,c).Style.NumberFormat.Format="#,##0.00";r++;}
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"purchases_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelReturns(List<ReturnRow> rows, dynamic summary, DateTime from, DateTime to, string title)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(title);
        ws.RightToLeft = true;
        ws.Cell(1,1).Value=$"{title} — من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";ws.Cell(1,1).Style.Font.Bold=true;
        string[] h={"الرقم","التاريخ","الاسم","التليفون","المبلغ","السبب"};
        for(int i=0;i<h.Length;i++){ws.Cell(2,i+1).Value=h[i];ws.Cell(2,i+1).Style.Font.Bold=true;}
        int r=3;
        foreach(var row in rows){ws.Cell(r,1).Value=row.Reference;ws.Cell(r,2).Value=row.Date.ToString("yyyy-MM-dd");ws.Cell(r,3).Value=row.Name;ws.Cell(r,4).Value=row.Phone;ws.Cell(r,5).Value=row.Amount;ws.Cell(r,5).Style.NumberFormat.Format="#,##0.00";ws.Cell(r,6).Value=row.Reason;r++;}
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"returns_{from:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelUserActivity(List<UserActivityRow> summary, dynamic detail, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws1 = wb.Worksheets.Add("ملخص الكاشير");
        ws1.RightToLeft = true;
        string[] h={"المستخدم","عدد الفواتير","إجمالي المبيعات","المرتجعات","الملغيات"};
        for(int i=0;i<h.Length;i++){ws1.Cell(1,i+1).Value=h[i];ws1.Cell(1,i+1).Style.Font.Bold=true;}
        int r=2;
        foreach(var row in summary){ws1.Cell(r,1).Value=row.UserName;ws1.Cell(r,2).Value=row.OrderCount;ws1.Cell(r,3).Value=row.TotalSales;ws1.Cell(r,3).Style.NumberFormat.Format="#,##0.00";ws1.Cell(r,4).Value=row.Returns;ws1.Cell(r,5).Value=row.Cancellations;r++;}
        ws1.Columns().AdjustToContents();
        return ExcelResult(wb, $"user_activity_{from:yyyyMMdd}.xlsx");
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
        var s = new MemoryStream(); wb.SaveAs(s); s.Position = 0;
        return new FileStreamResult(s, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet") { FileDownloadName = filename };
    }
}

// ── Report DTOs ──────────────────────────────────────────
public record CustomerStatementLine(DateTime Date, string Type, string Reference, string Description, decimal Debit, decimal Credit, decimal Balance);
public record CustomerAgingRow(int CustomerId, string CustomerName, string Phone, decimal Total, decimal Current, decimal Days60, decimal Days90, decimal Over90);
public record SupplierAgingRow(int SupplierId, string SupplierName, string Phone, string CompanyName, decimal Total, decimal Current, decimal Days60, decimal Days90, decimal Over90);
public record InventoryRow(int Id, string NameAr, string NameEn, string SKU, string CategoryName, decimal Price, decimal? DiscountPrice, decimal CostPrice, int TotalStock, decimal TotalValue, decimal TotalCostValue, List<VariantInventoryRow> Variants);
public record VariantInventoryRow(int Id, string Size, string Color, string ColorAr, int StockQuantity, decimal Price, decimal Value);
public record SalesRow(string OrderNumber, DateTime Date, string CustomerName, string Phone, string Source, string Status, string PaymentMethod, decimal SubTotal, decimal DiscountAmount, decimal TotalAmount, int ItemCount);
public record PurchaseRow(string InvoiceNumber, string SupplierInvoiceNumber, string SupplierName, DateTime InvoiceDate, string PaymentTerms, string Status, decimal SubTotal, decimal TaxAmount, decimal TotalAmount, decimal PaidAmount, decimal RemainingAmount);
public record ReturnRow(string Reference, DateTime Date, string Name, string Phone, decimal Amount, string Reason);
public record UserActivityRow(string UserId, string UserName, int OrderCount, decimal TotalSales, int Returns, int Cancellations);
public record ProductMovementLine(DateTime Date, string Type, string Reference, string EntityName, string Details, int In, int Out, decimal Amount, string ProductName = "", string Source = "", string Status = "", string SKU = "", int Balance = 0);
