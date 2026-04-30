using Sportive.API.Attributes;
using Sportive.API.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.ReportsMain)]
public class ExportController : ControllerBase
{
    private readonly AppDbContext _db;

    public ExportController(AppDbContext db) => _db = db;

    // â”€â”€ ORDERS â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // GET /api/export/orders?source=0&fromDate=2025-01-01&toDate=2025-12-31
    [HttpGet("orders")]
    public async Task<IActionResult> ExportOrders(
        [FromQuery] OrderSource? source    = null,
        [FromQuery] DateTime?   fromDate   = null,
        [FromQuery] DateTime?   toDate     = null,
        [FromQuery] OrderStatus? status    = null)
    {
        var query = _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .AsQueryable();

        if (source.HasValue)   query = query.Where(o => o.Source   == source.Value);
        if (status.HasValue)   query = query.Where(o => o.Status   == status.Value);
        if (fromDate.HasValue) query = query.Where(o => o.CreatedAt >= fromDate.Value.Date);
        if (toDate.HasValue)   query = query.Where(o => o.CreatedAt <= toDate.Value.Date.AddDays(1).AddTicks(-1));

        var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("الطلبات");

        // Header
        var headers = new[] {
            "رقم الفاتورة","رقم المرجع","العميل","التليفون","المصدر",
            "الحالة","طريقة الدفع","حالة الدفع","كود الصنف","الاسم","المقاس","اللون","الكمية", 
            "المجموع","الخصم","التوصيل","الإجمالي","التاريخ"
        };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
            cell.Style.Font.FontColor       = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Data
        int row = 2;
        foreach (var o in orders)
        {
            if (o.Items == null || !o.Items.Any())
            {
                ws.Cell(row, 1).Value  = o.OrderNumber;
                ws.Cell(row, 2).Value  = ""; 
                ws.Cell(row, 3).Value  = o.Customer?.FullName ?? "";
                ws.Cell(row, 4).Value  = o.Customer?.Phone ?? "";
                ws.Cell(row, 5).Value  = o.Source == OrderSource.POS ? "كاشير" : "موقع";
                ws.Cell(row, 6).Value  = o.Status.ToString();
                ws.Cell(row, 7).Value  = o.PaymentMethod.ToString();
                ws.Cell(row, 8).Value  = o.PaymentStatus.ToString();
                ws.Cell(row, 14).Value = o.SubTotal;
                ws.Cell(row, 15).Value = o.DiscountAmount + o.TemporalDiscount;
                ws.Cell(row, 16).Value = o.DeliveryFee;
                ws.Cell(row, 17).Value = o.TotalAmount;
                ws.Cell(row, 18).Value = o.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                row++;
                continue;
            }

            foreach (var it in o.Items)
            {
                ws.Cell(row, 1).Value  = o.OrderNumber;
                ws.Cell(row, 3).Value  = o.Customer?.FullName ?? "";
                ws.Cell(row, 4).Value  = o.Customer?.Phone ?? "";
                ws.Cell(row, 5).Value  = o.Source == OrderSource.POS ? "كاشير" : "موقع";
                ws.Cell(row, 6).Value  = o.Status.ToString();
                ws.Cell(row, 7).Value  = o.PaymentMethod.ToString();
                ws.Cell(row, 8).Value  = o.PaymentStatus.ToString();
                
                ws.Cell(row, 9).Value  = it.Product?.SKU ?? "";
                ws.Cell(row, 10).Value = it.Product?.NameAr ?? it.ProductNameAr;
                ws.Cell(row, 11).Value = it.Size ?? "";
                ws.Cell(row, 12).Value = it.Color ?? "";
                ws.Cell(row, 13).Value = it.Quantity;

                ws.Cell(row, 14).Value = o.SubTotal;
                ws.Cell(row, 15).Value = o.DiscountAmount + o.TemporalDiscount;
                ws.Cell(row, 16).Value = o.DeliveryFee;
                ws.Cell(row, 17).Value = o.TotalAmount;
                ws.Cell(row, 18).Value = o.CreatedAt.ToString("yyyy-MM-dd HH:mm");

                for (int c = 14; c <= 17; c++) ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.00";
                row++;
            }

            if (o.Source == OrderSource.POS)
                ws.Rows(row - o.Items.Count, row - 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#fff8e1");
        }

        // Summary row
        ws.Cell(row + 1, 10).Value = "الإجمالي:";
        ws.Cell(row + 1, 10).Style.Font.Bold = true;
        ws.Cell(row + 1, 11).FormulaA1 = $"=SUM(K2:K{row})";
        ws.Cell(row + 1, 11).Style.Font.Bold = true;
        ws.Cell(row + 1, 11).Style.NumberFormat.Format = "#,##0.00";

        ws.Columns().AdjustToContents();
        ws.RightToLeft = true;

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;

        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"orders_{TimeHelper.GetEgyptTime():yyyyMMdd}.xlsx");
    }

    // â”€â”€ PRODUCTS â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // GET /api/export/products
    [HttpGet("products")]
    public async Task<IActionResult> ExportProducts()
    {
        var products = await _db.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Variants)
            .OrderBy(p => p.NameAr)
            .ToListAsync();

        using var wb = new XLWorkbook();

        // Sheet 1: Products
        var ws1 = wb.Worksheets.Add("المنتجات");
        var h1 = new[] { "الاسم عربي","الاسم انجليزي","الفئة","الكود (SKU)",
            "السعر","سعر الخصم","العلامة التجارية","الحالة","مميز","الوصف عربي" };
        StyleHeader(ws1, h1);

        int r = 2;
        foreach (var p in products)
        {
            ws1.Cell(r,1).Value  = p.NameAr;
            ws1.Cell(r,2).Value  = p.NameEn;
            ws1.Cell(r,3).Value  = p.Category != null ? p.Category.NameAr : "Category Missing";
            ws1.Cell(r,4).Value  = p.SKU;
            ws1.Cell(r,5).Value  = p.Price;
            ws1.Cell(r,6).Value  = p.DiscountPrice ?? 0;
            ws1.Cell(r,7).Value  = p.Brand?.NameAr ?? "";
            ws1.Cell(r,8).Value  = p.Status.ToString();
            ws1.Cell(r,9).Value  = p.IsFeatured ? "نعم" : "لا";
            ws1.Cell(r,10).Value = p.DescriptionAr ?? "";
            ws1.Cell(r,5).Style.NumberFormat.Format = "#,##0.00";
            ws1.Cell(r,6).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }
        ws1.Columns().AdjustToContents();
        ws1.RightToLeft = true;

        // Sheet 2: Variants
        var ws2 = wb.Worksheets.Add("المقاسات والألوان");
        var h2 = new[] { "الكود (SKU)","الاسم عربي","المقاس","اللون","اللون عربي","المخزون","فارق السعر" };
        StyleHeader(ws2, h2);

        int r2 = 2;
        foreach (var p in products)
        foreach (var v in p.Variants)
        {
            ws2.Cell(r2,1).Value = p.SKU;
            ws2.Cell(r2,2).Value = p.NameAr;
            ws2.Cell(r2,3).Value = v.Size  ?? "";
            ws2.Cell(r2,4).Value = v.Color ?? "";
            ws2.Cell(r2,5).Value = v.ColorAr ?? "";
            ws2.Cell(r2,6).Value = v.StockQuantity;
            ws2.Cell(r2,7).Value = v.PriceAdjustment ?? 0;
            r2++;
        }
        ws2.Columns().AdjustToContents();
        ws2.RightToLeft = true;

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;

        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"products_{TimeHelper.GetEgyptTime():yyyyMMdd}.xlsx");
    }

    // â”€â”€ CUSTOMERS â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [HttpGet("customers")]
    public async Task<IActionResult> ExportCustomers()
    {
        var customers = await _db.Customers
            .Include(c => c.Orders)
            .OrderByDescending(c => c.Orders.Sum(o => o.TotalAmount))
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("العملاء");
        var headers = new[] { "الاسم","الإيميل","التليفون","عدد الطلبات","إجمالي الإنفاق","تاريخ التسجيل" };
        StyleHeader(ws, headers);

        int row = 2;
        foreach (var c in customers)
        {
            ws.Cell(row,1).Value = c.FullName;
            ws.Cell(row,2).Value = c.Email;
            ws.Cell(row,3).Value = c.Phone ?? "";
            ws.Cell(row,4).Value = c.Orders.Count;
            ws.Cell(row,5).Value = c.Orders.Sum(o => o.TotalAmount);
            ws.Cell(row,6).Value = c.CreatedAt.ToString("yyyy-MM-dd");
            ws.Cell(row,5).Style.NumberFormat.Format = "#,##0.00";
            row++;
        }
        ws.Columns().AdjustToContents();
        ws.RightToLeft = true;

        var stream = new MemoryStream();
        wb.SaveAs(stream); stream.Position = 0;

        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"customers_{TimeHelper.GetEgyptTime():yyyyMMdd}.xlsx");
    }

    // â”€â”€ POS DAY REPORT â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [HttpGet("pos-report")]
    public async Task<IActionResult> ExportPosReport(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null)
    {
        var from  = fromDate?.Date ?? TimeHelper.GetEgyptTime().Date;
        var to    = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var orders = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Where(o => o.Source == OrderSource.POS && o.CreatedAt >= from && o.CreatedAt < to)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("تقرير الكاشير");

        // Title
        ws.Cell(1,1).Value = $"تقرير الكاشير — {from:yyyy-MM-dd}";
        ws.Cell(1,1).Style.Font.Bold = true;
        ws.Cell(1,1).Style.Font.FontSize = 14;
        ws.Range(1,1,1,8).Merge();

        var headers = new[] { "رقم الفاتورة","العميل","التليفون","البائع","المبلغ","الخصم","الإجمالي","الحالة" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(2, c+1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f3460");
            cell.Style.Font.FontColor = XLColor.White;
        }

        int row = 3;
        foreach (var o in orders)
        {
            ws.Cell(row,1).Value = o.OrderNumber;
            ws.Cell(row,2).Value = o.Customer?.FullName ?? "";
            ws.Cell(row,3).Value = o.Customer?.Phone ?? "";
            ws.Cell(row,4).Value = o.SalesPersonId ?? "";
            ws.Cell(row,5).Value = o.SubTotal;
            ws.Cell(row,6).Value = o.DiscountAmount;
            ws.Cell(row,7).Value = o.TotalAmount;
            ws.Cell(row,8).Value = o.Status == OrderStatus.Returned ? "مرتجع"
                : o.Status == OrderStatus.Cancelled ? "ملغي" : "مكتمل";

            ws.Cell(row,5).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row,6).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row,7).Style.NumberFormat.Format = "#,##0.00";

            if (o.Status == OrderStatus.Returned)
                ws.Row(row).Style.Font.FontColor = XLColor.Red;
            row++;
        }

        // Totals
        var active   = orders.Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned).ToList();
        var returned = orders.Where(o => o.Status == OrderStatus.Returned).ToList();

        ws.Cell(row+1, 6).Value = "إجمالي المبيعات:";
        ws.Cell(row+1, 6).Style.Font.Bold = true;
        ws.Cell(row+1, 7).Value = active.Sum(o => o.TotalAmount);
        ws.Cell(row+1, 7).Style.Font.Bold = true;

        ws.Cell(row+2, 6).Value = "المرتجعات:";
        ws.Cell(row+2, 6).Style.Font.FontColor = XLColor.Red;
        ws.Cell(row+2, 7).Value = returned.Sum(o => o.TotalAmount);
        ws.Cell(row+2, 7).Style.Font.FontColor = XLColor.Red;

        ws.Cell(row+3, 6).Value = "الصافي:";
        ws.Cell(row+3, 6).Style.Font.Bold = true;
        ws.Cell(row+3, 7).Value = active.Sum(o => o.TotalAmount) - returned.Sum(o => o.TotalAmount);
        ws.Cell(row+3, 7).Style.Font.Bold = true;
        ws.Cell(row+3, 7).Style.Fill.BackgroundColor = XLColor.FromHtml("#e8f5e9");

        ws.Columns().AdjustToContents();
        ws.RightToLeft = true;

        var stream = new MemoryStream();
        wb.SaveAs(stream); stream.Position = 0;

        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"pos_report_{from:yyyyMMdd}.xlsx");
    }

    // â”€â”€ INVENTORY FULL â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // GET /api/export/inventory-full
    [HttpGet("inventory-full")]
    public async Task<IActionResult> ExportInventoryFull()
    {
        var products = await _db.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Variants)
            .OrderBy(p => p.Category == null ? "" : p.Category.NameAr).ThenBy(p => p.NameAr)
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("المخزون الكامل");

        var headers = new[]
        {
            "الكود (SKU)", "اسم المنتج", "الفئة", "الماركة",
            "السعر", "التكلفة", "إجمالي المخزون", "حد إعادة الطلب",
            "قيمة المخزون", "الحالة"
        };
        StyleHeader(ws, headers);

        int row = 2;
        decimal grandTotal = 0;
        foreach (var p in products)
        {
            var stockValue = p.TotalStock * (p.CostPrice ?? 0);
            grandTotal += stockValue;

            ws.Cell(row, 1).Value  = p.SKU;
            ws.Cell(row, 2).Value  = p.NameAr;
            ws.Cell(row, 3).Value  = p.Category?.NameAr ?? "";
            ws.Cell(row, 4).Value  = p.Brand?.NameAr ?? "";
            ws.Cell(row, 5).Value  = p.Price;
            ws.Cell(row, 6).Value  = p.CostPrice ?? 0;
            ws.Cell(row, 7).Value  = p.TotalStock;
            ws.Cell(row, 8).Value  = p.ReorderLevel;
            ws.Cell(row, 9).Value  = stockValue;
            ws.Cell(row, 10).Value = p.Status.ToString();

            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0.00";

            // ØªÙ„ÙˆÙŠÙ† Ø§Ù„Ù…Ù†ØªØ¬Ø§Øª Ù…Ù†Ø®ÙØ¶Ø© Ø§Ù„Ù…Ø®Ø²ÙˆÙ†
            if (p.TotalStock == 0)
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#ffebee");
            else if (p.ReorderLevel > 0 && p.TotalStock <= p.ReorderLevel)
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#fff8e1");

            row++;
        }

        // ØµÙ Ø§Ù„Ø¥Ø¬Ù…Ø§Ù„ÙŠ
        ws.Cell(row + 1, 8).Value = "إجمالي قيمة المخزون:";
        ws.Cell(row + 1, 8).Style.Font.Bold = true;
        ws.Cell(row + 1, 9).Value = grandTotal;
        ws.Cell(row + 1, 9).Style.Font.Bold = true;
        ws.Cell(row + 1, 9).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(row + 1, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#e8f5e9");

        ws.Columns().AdjustToContents();
        ws.RightToLeft = true;

        var stream = new MemoryStream();
        wb.SaveAs(stream); stream.Position = 0;

        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"inventory_full_{TimeHelper.GetEgyptTime():yyyyMMdd}.xlsx");
    }

    // â”€â”€ LOW STOCK â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // GET /api/export/low-stock
    [HttpGet("low-stock")]
    public async Task<IActionResult> ExportLowStock()
    {
        var products = await _db.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Variants)
            .Where(p => p.ReorderLevel > 0 && p.TotalStock <= p.ReorderLevel)
            .OrderBy(p => p.TotalStock)
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("منخفضة المخزون");

        var headers = new[]
        {
            "الكود (SKU)", "اسم المنتج", "الفئة", "الماركة",
            "المخزون الحالي", "حد إعادة الطلب", "النقص", "التكلفة", "الحالة"
        };
        StyleHeader(ws, headers);

        int row = 2;
        foreach (var p in products)
        {
            var shortage = p.ReorderLevel - p.TotalStock;

            ws.Cell(row, 1).Value = p.SKU;
            ws.Cell(row, 2).Value = p.NameAr;
            ws.Cell(row, 3).Value = p.Category?.NameAr ?? "";
            ws.Cell(row, 4).Value = p.Brand?.NameAr ?? "";
            ws.Cell(row, 5).Value = p.TotalStock;
            ws.Cell(row, 6).Value = p.ReorderLevel;
            ws.Cell(row, 7).Value = shortage > 0 ? shortage : 0;
            ws.Cell(row, 8).Value = p.CostPrice ?? 0;
            ws.Cell(row, 9).Value = p.TotalStock == 0 ? "نفذ المخزون" : "مخزون منخفض";

            ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0.00";

            // Ø£Ø­Ù…Ø± Ù„Ù„Ù†Ø§ÙØ°ØŒ Ø£ØµÙØ± Ù„Ù„Ù…Ù†Ø®ÙØ¶
            ws.Row(row).Style.Fill.BackgroundColor = p.TotalStock == 0
                ? XLColor.FromHtml("#ffebee")
                : XLColor.FromHtml("#fff8e1");

            row++;
        }

        ws.Columns().AdjustToContents();
        ws.RightToLeft = true;

        var stream = new MemoryStream();
        wb.SaveAs(stream); stream.Position = 0;

        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"low_stock_{TimeHelper.GetEgyptTime():yyyyMMdd}.xlsx");
    }

    // â”€â”€ VARIANTS (SIZES & COLORS) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // GET /api/export/inventory-variants
    [HttpGet("inventory-variants")]
    public async Task<IActionResult> ExportInventoryVariants()
    {
        var products = await _db.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Variants)
            .Where(p => p.Variants.Any())
            .OrderBy(p => p.Category == null ? "" : p.Category.NameAr).ThenBy(p => p.NameAr)
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("المقاسات والألوان");

        var headers = new[]
        {
            "الكود (SKU)", "اسم المنتج", "الفئة", "الماركة",
            "المقاس", "اللون", "اللون عربي",
            "المخزون", "حد إعادة الطلب", "فارق السعر", "الحالة"
        };
        StyleHeader(ws, headers);

        int row = 2;
        foreach (var p in products)
        foreach (var v in p.Variants)
        {
            ws.Cell(row, 1).Value  = p.SKU;
            ws.Cell(row, 2).Value  = p.NameAr;
            ws.Cell(row, 3).Value  = p.Category?.NameAr ?? "";
            ws.Cell(row, 4).Value  = p.Brand?.NameAr ?? "";
            ws.Cell(row, 5).Value  = v.Size ?? "";
            ws.Cell(row, 6).Value  = v.Color ?? "";
            ws.Cell(row, 7).Value  = v.ColorAr ?? "";
            ws.Cell(row, 8).Value  = v.StockQuantity;
            ws.Cell(row, 9).Value  = v.ReorderLevel;
            ws.Cell(row, 10).Value = v.PriceAdjustment ?? 0;
            ws.Cell(row, 11).Value = v.StockQuantity == 0 ? "نفذ" : v.ReorderLevel > 0 && v.StockQuantity <= v.ReorderLevel ? "منخفض" : "متاح";

            ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0.00";

            if (v.StockQuantity == 0)
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#ffebee");
            else if (v.ReorderLevel > 0 && v.StockQuantity <= v.ReorderLevel)
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#fff8e1");

            row++;
        }

        ws.Columns().AdjustToContents();
        ws.RightToLeft = true;

        var stream = new MemoryStream();
        wb.SaveAs(stream); stream.Position = 0;

        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"variants_{TimeHelper.GetEgyptTime():yyyyMMdd}.xlsx");
    }

    // â”€â”€ ACCOUNTS â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [HttpGet("accounts")]
    public async Task<IActionResult> ExportAccounts()
    {
        var accounts = await _db.Accounts
            .Include(a => a.Parent)
            .OrderBy(a => a.Code)
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("شجرة الحسابات");

        var headers = new[] { "كود الحساب", "الاسم عربي", "الاسم انجليزي", "النوع", "الطبيعة", "كود الأب", "يقبل ترحيل" };
        StyleHeader(ws, headers);

        int row = 2;
        foreach (var a in accounts)
        {
            ws.Cell(row, 1).Value = a.Code;
            ws.Cell(row, 2).Value = a.NameAr;
            ws.Cell(row, 3).Value = a.NameEn ?? "";
            ws.Cell(row, 4).Value = a.Type.ToString();
            ws.Cell(row, 5).Value = a.Nature.ToString();
            ws.Cell(row, 6).Value = a.Parent?.Code ?? "";
            ws.Cell(row, 7).Value = a.AllowPosting ? "نعم" : "لا";
            row++;
        }

        ws.Columns().AdjustToContents();
        ws.RightToLeft = true;

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;

        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"chart_of_accounts_{TimeHelper.GetEgyptTime():yyyyMMdd}.xlsx");
    }

    private static void StyleHeader(IXLWorksheet ws, string[] headers)
    {
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }
}

