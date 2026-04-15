using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant,Staff")]
public class OperationalReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    public OperationalReportsController(AppDbContext db) => _db = db;
    
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

        // إذا بحث بالاسم ابحث أول
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

        var ordersQuery = _db.Orders
            .Where(o => o.CustomerId == customerId
                     && o.CreatedAt >= from && o.CreatedAt <= to
                     && o.Status != OrderStatus.Cancelled);

        if (unpaidOnly)
        {
            ordersQuery = ordersQuery.Where(o => (o.TotalAmount - o.PaidAmount) > 0 && o.PaymentMethod == PaymentMethod.Credit);
        }

        var orders = await ordersQuery.OrderBy(o => o.CreatedAt).ToListAsync();

        var receiptsQuery = _db.ReceiptVouchers
            .Where(r => r.CustomerId == customerId
                     && r.VoucherDate >= from && r.VoucherDate <= to);

        if (unpaidOnly)
        {
            var unpaidOrderIds = orders.Select(o => o.Id).ToList();
            receiptsQuery = receiptsQuery.Where(r => r.OrderId != null && unpaidOrderIds.Contains(r.OrderId.Value));
        }

        var receipts = await receiptsQuery.OrderBy(r => r.VoucherDate).ToListAsync();

        // بناء الكشف
        var lines = new List<CustomerStatementLine>();
        
        decimal balance = 0;
        if (!unpaidOnly)
        {
            // 1. الرصيد قبل الفترة = (الافتتاحي للحساب) + (الفواتير السابقة) - (المقبوضات السابقة)
            decimal initialAccountBalance = (customer.MainAccountId != null) 
                ? (await _db.Accounts.Where(a => a.Id == customer.MainAccountId).Select(a => a.OpeningBalance).FirstOrDefaultAsync())
                : 0;

            decimal priorOrders = await _db.Orders
                .Where(o => o.CustomerId == customerId && o.CreatedAt < from && o.Status != OrderStatus.Cancelled)
                .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

            decimal priorReceipts = await _db.ReceiptVouchers
                .Where(r => r.CustomerId == customerId && r.VoucherDate < from)
                .SumAsync(r => (decimal?)r.Amount) ?? 0;

            balance = initialAccountBalance + priorOrders - priorReceipts;
        }

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
    // 1.5 كشف حساب مورد
    // GET /api/operationalreports/supplier-statement?supplierId=&fromDate=&toDate=
    // ══════════════════════════════════════════════════════
    [HttpGet("supplier-statement")]
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

        var invoicesQuery = _db.PurchaseInvoices
            .Where(i => i.SupplierId == supplierId
                     && i.InvoiceDate >= from && i.InvoiceDate <= to
                     && i.Status != PurchaseInvoiceStatus.Cancelled);

        if (unpaidOnly)
        {
            invoicesQuery = invoicesQuery.Where(i => (i.TotalAmount - i.PaidAmount - i.ReturnedAmount) > 0 && i.PaymentTerms != PaymentTerms.Cash);
        }

        var invoices = await invoicesQuery.OrderBy(i => i.InvoiceDate).ToListAsync();

        var paymentsQuery = _db.SupplierPayments
            .Where(p => p.SupplierId == supplierId
                     && p.PaymentDate >= from && p.PaymentDate <= to);

        if (unpaidOnly)
        {
            var unpaidInvNos = invoices.Select(i => i.InvoiceNumber).ToList();
            paymentsQuery = paymentsQuery.Where(p => p.ReferenceNumber != null && unpaidInvNos.Contains(p.ReferenceNumber));
        }

        var payments = await paymentsQuery.OrderBy(p => p.PaymentDate).ToListAsync();

        var lines = new List<CustomerStatementLine>(); // Reuse DTO
        decimal balance = 0;

        if (!unpaidOnly)
        {
            decimal initialBal = 0; // Suppliers usually 0 opening or from account
            decimal priorInvoices = await _db.PurchaseInvoices
                .Where(i => i.SupplierId == supplierId && i.InvoiceDate < from && i.Status != PurchaseInvoiceStatus.Cancelled)
                .SumAsync(i => (decimal?)(i.TotalAmount - i.ReturnedAmount)) ?? 0;
            decimal priorPayments = await _db.SupplierPayments
                .Where(p => p.SupplierId == supplierId && p.PaymentDate < from)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;
            balance = initialBal + priorInvoices - priorPayments;

            if (balance != 0)
                lines.Add(new CustomerStatementLine(from.AddSeconds(-1), "رصيد", "OPENING", "رصيد مرحّل", balance, 0, balance));
        }

        foreach (var inv in invoices)
        {
            balance += inv.TotalAmount;
            lines.Add(new CustomerStatementLine(inv.InvoiceDate, "فاتورة شراء", inv.InvoiceNumber, "فاتورة مشتريات", inv.TotalAmount, 0, balance));
            
            if (inv.ReturnedAmount > 0)
            {
                balance -= inv.ReturnedAmount;
                lines.Add(new CustomerStatementLine(inv.UpdatedAt ?? inv.InvoiceDate, "مرتجع مشتريات", inv.InvoiceNumber + "-RTN", "مرتجع من فاتورة", 0, inv.ReturnedAmount, balance));
            }
        }

        foreach (var p in payments)
        {
            balance -= p.Amount;
            lines.Add(new CustomerStatementLine(p.PaymentDate, "سند صرف", p.PaymentNumber, p.Notes ?? "دفع للمورد", 0, p.Amount, balance));
        }

        lines = lines.OrderBy(l => l.Date).ToList();

        if (excel) return ExcelSupplierStatement(supplier, lines, invoices.Sum(i => i.TotalAmount), payments.Sum(p => p.Amount), from, to);

        return Ok(new { 
            supplier = new { supplier.Id, supplier.Name, supplier.Phone },
            from, to, lines, 
            totalInvoiced = invoices.Sum(i => i.TotalAmount), 
            totalReturned = invoices.Sum(i => i.ReturnedAmount),
            totalPaid = payments.Sum(p => p.Amount),
            outstanding = (invoices.Sum(i => i.TotalAmount) - invoices.Sum(i => i.ReturnedAmount)) - payments.Sum(p => p.Amount)
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
        var asOf = asOfDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var customers = await _db.Customers
            .Include(c => c.Orders)
            .Include(c => c.MainAccount)
            .ToListAsync();

        if (!string.IsNullOrEmpty(search))
            customers = customers.Where(c => c.FullName.Contains(search) || (c.Phone != null && c.Phone.Contains(search))).ToList();

        // ✅ FIX: Use Ledger (JournalLines) to get all movements accurately
        var ledgerBalances = await _db.JournalLines
            .Where(l => (l.Account.Code.StartsWith("1103") || l.Account.Code.StartsWith("1201")))
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
            var creditOrders = c.Orders
                .Where(o => o.Status != OrderStatus.Cancelled 
                         && o.CreatedAt <= asOf
                         && (o.PaymentMethod == PaymentMethod.Credit || o.PaymentMethod == PaymentMethod.Mixed || (o.TotalAmount - o.PaidAmount) > 0))
                .OrderBy(o => o.CreatedAt)
                .ToList();

            // حساب عمر الدين — توزيع الرصيد على الطلبات حسب أعمارها (LIFO logic for payments assumption)
            decimal rem = balance;
            decimal c30 = 0, c60 = 0, c90 = 0, c90plus = 0;

            foreach (var o in creditOrders)
            {
                if (rem <= 0) break;
                var days = (asOf - o.CreatedAt).Days;
                
                // Calculate net order amount after returns
                var oNet = o.TotalAmount - o.Items.Sum(i => i.ReturnedQuantity * i.UnitPrice);
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
        var asOf = asOfDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var suppliers = await _db.Suppliers
            .Include(s => s.Invoices)
            .Include(s => s.Payments)
            .ToListAsync();

        if (!string.IsNullOrEmpty(search))
            suppliers = suppliers.Where(s => s.Name.Contains(search) || s.Phone.Contains(search)).ToList();

        // ✅ FIX: Use Ledger (JournalLines) for accurate balance
        var ledgerBalances = await _db.JournalLines
            .Where(l => l.SupplierId != null && l.Account.Code.StartsWith("2101"))
            .Where(l => l.JournalEntry.EntryDate <= asOf && l.JournalEntry.Status == JournalEntryStatus.Posted)
            .GroupBy(l => l.SupplierId)
            .Select(g => new { SupplierId = g.Key, Balance = g.Sum(l => l.Credit - l.Debit) })
            .ToListAsync();

        var balanceMap = ledgerBalances
            .Where(x => x.SupplierId != null)
            .ToDictionary(x => x.SupplierId!.Value, x => x.Balance);

        var rows = new List<SupplierAgingRow>();
        foreach (var s in suppliers)
        {
            if (!balanceMap.TryGetValue(s.Id, out var balance) || balance <= 0) 
                continue;

            // الفواتير الآجلة حتى تاريخ asOf لتوزيع عمر الدين
            var creditInvoices = s.Invoices
                .Where(i => i.Status != PurchaseInvoiceStatus.Cancelled
                         && i.InvoiceDate <= asOf
                         && i.PaymentTerms == PaymentTerms.Credit)
                .OrderBy(i => i.InvoiceDate)
                .ToList();

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
        [FromQuery] string? search      = null,
        [FromQuery] int?    categoryId  = null,
        [FromQuery] int?    brandId     = null,
        [FromQuery] string? color       = null,
        [FromQuery] string? size        = null,
        [FromQuery] bool    lowStock    = false,
        [FromQuery] string  stockStatus = "all", // "all", "positive", "zero"
        [FromQuery] int     page        = 1,
        [FromQuery] int     pageSize    = 50,
        [FromQuery] bool    excel       = false)
    {
        var q = _db.Products
            .Include(p => p.Category)
            .Include(p => p.Variants)
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

        var summary = new {
            totalFilteredProducts = totalCount,
            totalUnits            = totalUnits,
            lowStockCount         = lowStockCount,
            outOfStock            = outOfStock,
            totalSalesValue       = totalSalesVal,
            totalCostValue        = totalCostVal,
            agingAlerts           = totals.Count(x => x.TotalStock > 0 && x.Cost > 0) // Placeholder
        };

        if (excel) return ExcelInventory(rows, summary);

        return Ok(new { 
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
    // 5. تقرير المبيعات
    // GET /api/operationalreports/sales?fromDate=&toDate=&source=
    // ══════════════════════════════════════════════════════
    [HttpGet("sales")]
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

        var q = _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p!.Category)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p!.Brand)
            .Where(o => o.Status != OrderStatus.Cancelled
                     && o.CreatedAt >= from && o.CreatedAt <= to);

        if (source.HasValue) q = q.Where(o => o.Source == source.Value);

        if (categoryId.HasValue && categoryId > 0)
        {
            var categoryIds = await FilterHelper.GetCategoryFamilyIds(_db, categoryId);
            q = q.Where(o => o.Items.Any(i => i.Product != null && i.Product.CategoryId.HasValue && categoryIds.Contains(i.Product.CategoryId.Value)));
        }

        if (brandId.HasValue && brandId > 0)
        {
            var brandIds = await FilterHelper.GetBrandFamilyIds(_db, brandId);
            q = q.Where(o => o.Items.Any(i => i.Product != null && i.Product.BrandId.HasValue && brandIds.Contains(i.Product.BrandId.Value)));
        }

        if (!string.IsNullOrEmpty(color))
            q = q.Where(o => o.Items.Any(i => i.Color == color || (i.ProductVariant != null && (i.ProductVariant.Color == color || i.ProductVariant.ColorAr == color))));

        if (!string.IsNullOrEmpty(size))
            q = q.Where(o => o.Items.Any(i => i.Size == size || (i.ProductVariant != null && i.ProductVariant.Size == size)));

        var orders = await q.OrderByDescending(o => o.CreatedAt).ToListAsync();
        
        // 🚨 GET RETURNS IN THE SAME PERIOD
        var returns = await _db.JournalEntries
            .Include(j => j.Lines)
            .Where(j => j.Type == JournalEntryType.SalesReturn && j.EntryDate >= from && j.EntryDate <= to)
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
            o.Id, o.OrderNumber, o.CreatedAt,
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
        [FromQuery] int?      categoryId = null,
        [FromQuery] int?      brandId    = null,
        [FromQuery] string?   color      = null,
        [FromQuery] string?   size       = null,
        [FromQuery] bool      excel      = false)
    {
        var from = fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var q = _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Include(i => i.Items).ThenInclude(it => it.Product).ThenInclude(p => p!.Category)
            .Include(i => i.Items).ThenInclude(it => it.Product).ThenInclude(p => p!.Brand)
            .Where(i => i.InvoiceDate >= from && i.InvoiceDate <= to);

        if (supplierId.HasValue) q = q.Where(i => i.SupplierId == supplierId.Value);

        if (categoryId.HasValue && categoryId > 0)
        {
            var categoryIds = await FilterHelper.GetCategoryFamilyIds(_db, categoryId);
            q = q.Where(i => i.Items.Any(it => it.Product != null && it.Product.CategoryId.HasValue && categoryIds.Contains(it.Product.CategoryId.Value)));
        }

        if (brandId.HasValue && brandId > 0)
        {
            var brandIds = await FilterHelper.GetBrandFamilyIds(_db, brandId);
            q = q.Where(i => i.Items.Any(it => it.Product != null && it.Product.BrandId.HasValue && brandIds.Contains(it.Product.BrandId.Value)));
        }

        if (!string.IsNullOrEmpty(color))
            q = q.Where(i => i.Items.Any(it => it.ProductVariant != null && (it.ProductVariant.Color == color || it.ProductVariant.ColorAr == color) || (it.Product != null && it.Product.Variants.Any(v => v.Color == color || v.ColorAr == color))));

        if (!string.IsNullOrEmpty(size))
            q = q.Where(i => i.Items.Any(it => it.ProductVariant != null && it.ProductVariant.Size == size || (it.Product != null && it.Product.Variants.Any(v => v.Size == size))));

        var invoices = await q.OrderByDescending(i => i.InvoiceDate).ToListAsync();

        var rows = invoices.Select(i => new PurchaseRow(
            i.Id, i.InvoiceNumber, i.SupplierInvoiceNumber ?? "",
            i.Supplier?.Name ?? "N/A", i.InvoiceDate,
            i.PaymentTerms.ToString(), i.Status.ToString(),
            i.SubTotal, i.TaxAmount, i.TotalAmount,
            i.ReturnedAmount,
            i.PaidAmount, i.TotalAmount - i.PaidAmount - i.ReturnedAmount
        )).ToList();

        var summary = new {
            totalInvoices  = rows.Count,
            totalGross     = rows.Sum(r => r.TotalAmount),
            totalReturned  = rows.Sum(r => r.ReturnedAmount),
            totalNet       = rows.Sum(r => r.TotalAmount - r.ReturnedAmount),
            totalAmount    = rows.Sum(r => r.TotalAmount - r.ReturnedAmount), // Compatibility
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
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] int?      categoryId = null,
        [FromQuery] int?      brandId    = null,
        [FromQuery] string?   color      = null,
        [FromQuery] string?   size       = null,
        [FromQuery] bool      excel      = false)
    {
        var from = fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        // Get all SalesReturn journal entries
        var returnsQ = _db.JournalEntries
            .Include(j => j.Lines)
            .Include(j => j.Order).ThenInclude(o => o!.Customer)
            .Include(j => j.Order).ThenInclude(o => o!.Items).ThenInclude(it => it.Product).ThenInclude(p => p!.Category)
            .Include(j => j.Order).ThenInclude(o => o!.Items).ThenInclude(it => it.Product).ThenInclude(p => p!.Brand)
            .Where(j => j.Type == JournalEntryType.SalesReturn 
                     && j.EntryDate >= from && j.EntryDate <= to);

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

        var returns = await returnsQ.OrderByDescending(j => j.EntryDate).ToListAsync();

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
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] int?      categoryId = null,
        [FromQuery] int?      brandId    = null,
        [FromQuery] string?   color      = null,
        [FromQuery] string?   size       = null,
        [FromQuery] bool      excel      = false)
    {
        var from = fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var q = _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Include(i => i.Items).ThenInclude(it => it.Product).ThenInclude(p => p!.Category)
            .Include(i => i.Items).ThenInclude(it => it.Product).ThenInclude(p => p!.Brand)
            .Where(i => (i.Status == PurchaseInvoiceStatus.Returned || i.Status == PurchaseInvoiceStatus.PartiallyReturned || i.ReturnedAmount > 0)
                     && i.InvoiceDate >= from && i.InvoiceDate <= to);

        if (categoryId.HasValue && categoryId > 0)
        {
            var categoryIds = await FilterHelper.GetCategoryFamilyIds(_db, categoryId);
            q = q.Where(i => i.Items.Any(it => it.Product != null && it.Product.CategoryId.HasValue && categoryIds.Contains(it.Product.CategoryId.Value)));
        }

        if (brandId.HasValue && brandId > 0)
        {
            var brandIds = await FilterHelper.GetBrandFamilyIds(_db, brandId);
            q = q.Where(i => i.Items.Any(it => it.Product != null && it.Product.BrandId.HasValue && brandIds.Contains(it.Product.BrandId.Value)));
        }

        if (!string.IsNullOrEmpty(color))
            q = q.Where(i => i.Items.Any(it => (it.ProductVariant != null && (it.ProductVariant.Color == color || it.ProductVariant.ColorAr == color)) || (it.Product != null && it.Product.Variants.Any(v => v.Color == color || v.ColorAr == color))));

        if (!string.IsNullOrEmpty(size))
            q = q.Where(i => i.Items.Any(it => (it.ProductVariant != null && it.ProductVariant.Size == size) || (it.Product != null && it.Product.Variants.Any(v => v.Size == size))));

        var returns = await q.OrderByDescending(i => i.InvoiceDate).ToListAsync();

        var rows = returns.Select(i => new ReturnRow(
            i.InvoiceNumber, i.InvoiceDate,
            i.Supplier.Name, i.Supplier.Phone,
            i.ReturnedAmount > 0 ? i.ReturnedAmount : i.TotalAmount, 
            i.Notes ?? ""
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

        // 🛡️ REFINEMENT: Fetch User Names for the summary
        var userIds = orders.Select(o => o.SalesPersonId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var userNames = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        // Group by sales person
        var byPerson = orders
            .GroupBy(o => o.SalesPersonId!)
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
                    userNames.GetValueOrDefault(g.Key, "System/Unknown"),
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
        [FromQuery] int?      productId  = null,
        [FromQuery] string?   search     = null,
        [FromQuery] int?      categoryId = null,
        [FromQuery] int?      brandId    = null,
        [FromQuery] string?   color      = null,
        [FromQuery] string?   size       = null,
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
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

            // If all filters are empty, return the choices list
            bool hasFilters = categoryId.HasValue || brandId.HasValue || !string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(size) || productId.HasValue;
            
            if (!hasFilters && string.IsNullOrEmpty(search))
            {
                var pList = await _db.Products
                    .Select(p => new { p.Id, p.NameAr, p.SKU })
                    .ToListAsync();
                
                // Add "All" option
                var result = new List<object> { new { Id = 0, NameAr = "الكل (جميع الأصناف)", SKU = "ALL" } };
                result.AddRange(pList.Cast<object>());
                
                return Ok(new { products = result });
            }

            // 1. Fetch Movements from InventoryMovements table
            var movementsQuery = _db.InventoryMovements
                .Include(m => m.Product)
                .Include(m => m.ProductVariant)
                .Where(m => m.CreatedAt >= from && m.CreatedAt <= to);

            if (productId > 0) movementsQuery = movementsQuery.Where(m => m.ProductId == productId);

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

            var movements = dbMovements.Select(m => new ProductMovementLine(
                m.CreatedAt,
                TranslateMovementType(m.Type),
                m.Reference ?? "N/A",
                m.Note ?? "-",
                (m.ProductVariant != null ? (m.ProductVariant.Size + " / " + (m.ProductVariant.ColorAr ?? m.ProductVariant.Color ?? "")) : "أساسي"),
                m.Quantity > 0 ? m.Quantity : 0,
                m.Quantity < 0 ? Math.Abs(m.Quantity) : 0,
                m.Quantity * m.UnitCost,
                m.Product?.NameAr ?? "Deleted Product",
                "System",
                "Completed",
                m.Product?.SKU ?? "N/A",
                m.RemainingStock
            )).ToList();

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
        var from = fromDate ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate ?? TimeHelper.GetEgyptTime();

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
        string[] h = { "المستخدم", "عدد العمليات", "إجمالي المبيعات", "إجمالي المرتجعات", "إجمالي الخصومات", "الصافي المحقق", "الملغيات" };
        for (int i = 0; i < h.Length; i++) { ws1.Cell(1, i + 1).Value = h[i]; ws1.Cell(1, i + 1).Style.Font.Bold = true; ws1.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1a237e"); ws1.Cell(1, i + 1).Style.Font.FontColor = XLColor.White; }
        int r = 2;
        foreach (var row in summary)
        {
            ws1.Cell(r, 1).Value = row.UserName;
            ws1.Cell(r, 2).Value = row.OrderCount;
            ws1.Cell(r, 3).Value = row.GrossSales;
            ws1.Cell(r, 4).Value = row.TotalReturns;
            ws1.Cell(r, 5).Value = row.TotalDiscount;
            ws1.Cell(r, 6).Value = row.NetSales;
            ws1.Cell(r, 7).Value = row.Cancellations;
            for (int c = 3; c <= 6; c++) ws1.Cell(r, c).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }
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
                p.Id, p.NameAr, p.SKU, p.TotalStock, p.Price,
                LastSaleDate = _db.InventoryMovements
                    .Where(m => m.ProductId == p.Id && m.Type == InventoryMovementType.Sale)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => (DateTime?)m.CreatedAt)
                    .FirstOrDefault()
            })
            .ToListAsync();

        // 4. Filter by Aging Cutoff
        var agingRows = productsData
            .Where(p => p.LastSaleDate == null || p.LastSaleDate <= cutoff)
            .Select(p => new {
                p.Id, 
                p.NameAr, 
                p.SKU, 
                p.TotalStock, 
                p.Price,
                DaysSinceLastSale = p.LastSaleDate.HasValue ? (int)(TimeHelper.GetEgyptTime() - p.LastSaleDate.Value).TotalDays : 999,
                Value = p.TotalStock * p.Price
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
public record CustomerAgingRow(int CustomerId, string CustomerName, string Phone, decimal Total, decimal Current, decimal Days60, decimal Days90, decimal Over90);
public record SupplierAgingRow(int SupplierId, string SupplierName, string Phone, string CompanyName, decimal Total, decimal Current, decimal Days60, decimal Days90, decimal Over90);
public record InventoryRow(int Id, string NameAr, string NameEn, string SKU, string CategoryName, decimal Price, decimal? DiscountPrice, decimal CostPrice, int TotalStock, decimal TotalValue, decimal TotalCostValue, List<VariantInventoryRow> Variants);
public record VariantInventoryRow(int Id, string Size, string Color, string ColorAr, int StockQuantity, decimal Price, decimal Value);
public record SalesRow(int Id, string OrderNumber, DateTime Date, string CustomerName, string Phone, string Source, string Status, string PaymentMethod, decimal SubTotal, decimal DiscountAmount, decimal TotalAmount, int ItemCount);
public record PurchaseRow(int Id, string InvoiceNumber, string SupplierInvoiceNumber, string SupplierName, DateTime InvoiceDate, string PaymentTerms, string Status, decimal SubTotal, decimal TaxAmount, decimal TotalAmount, decimal ReturnedAmount, decimal PaidAmount, decimal RemainingAmount);
public record ReturnRow(string Reference, DateTime Date, string Name, string Phone, decimal Amount, string Reason);
public record UserActivityRow(string UserId, string UserName, int OrderCount, decimal GrossSales, decimal TotalReturns, decimal TotalDiscount, decimal NetSales, int Cancellations);
public record ProductMovementLine(DateTime Date, string Type, string Reference, string EntityName, string Details, int In, int Out, decimal Amount, string ProductName = "", string Source = "", string Status = "", string SKU = "", int Balance = 0);
