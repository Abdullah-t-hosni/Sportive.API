using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Interfaces;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

// ══════════════════════════════════════════════════════
// SUPPLIERS
// ══════════════════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class SuppliersController : ControllerBase
{
    private readonly AppDbContext _db;
    public SuppliersController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search   = null,
        [FromQuery] bool?   isActive = null,
        [FromQuery] string? sortBy   = null,
        [FromQuery] string  sortDir  = "asc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var q = _db.Suppliers
            .Include(s => s.Invoices)
            .AsQueryable();

        if (isActive.HasValue) q = q.Where(s => s.IsActive == isActive.Value);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(s => s.Name.Contains(search) || s.Phone.Contains(search)
                           || (s.CompanyName != null && s.CompanyName.Contains(search)));

        var total = await q.CountAsync();
        var desc = sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<Supplier> ordered = sortBy?.ToLower() switch {
            "totalpurchases" => desc ? q.OrderByDescending(s => s.TotalPurchases) : q.OrderBy(s => s.TotalPurchases),
            "totalpaid"      => desc ? q.OrderByDescending(s => s.TotalPaid)      : q.OrderBy(s => s.TotalPaid),
            "balance"        => desc ? q.OrderByDescending(s => s.TotalPurchases - s.TotalPaid) : q.OrderBy(s => s.TotalPurchases - s.TotalPaid),
            _                => q.OrderBy(s => s.Name),
        };
        var items = await ordered
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new SupplierDto(
                s.Id, s.Name, s.Phone, s.CompanyName, s.TaxNumber, s.Email, s.Address,
                s.IsActive, s.TotalPurchases, s.TotalPaid, s.TotalPurchases - s.TotalPaid,
                s.Invoices.Count,
                s.AttachmentUrl, s.AttachmentPublicId
            )).ToListAsync();

        return Ok(new PaginatedResult<SupplierDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var s = await _db.Suppliers.Include(s => s.Invoices).FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();
        return Ok(new SupplierDto(s.Id, s.Name, s.Phone, s.CompanyName, s.TaxNumber,
            s.Email, s.Address, s.IsActive, s.TotalPurchases, s.TotalPaid,
            s.TotalPurchases - s.TotalPaid, s.Invoices.Count,
            s.AttachmentUrl, s.AttachmentPublicId));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSupplierDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Phone))
            return BadRequest(new { message = "الاسم ورقم الهاتف إلزاميان" });

        // 1. التحقق من وجود المورد مسبقاً (نشط)
        var existing = await _db.Suppliers.FirstOrDefaultAsync(s => 
            s.Name.Trim() == dto.Name.Trim() || s.Phone.Trim() == dto.Phone.Trim());
        
        if (existing != null)
        {
            return BadRequest(new { message = "هذا المورد مسجل بالفعل." });
        }

        var supplier = new Supplier
        {
            Name        = dto.Name.Trim(),
            Phone       = dto.Phone.Trim(),
            CompanyName = dto.CompanyName?.Trim(),
            TaxNumber   = dto.TaxNumber?.Trim(),
            Email       = dto.Email?.Trim().ToLower(),
            Address     = dto.Address?.Trim(),
            CreatedAt   = TimeHelper.GetEgyptTime(),
            AttachmentUrl = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId
        };

        // ── Use Global Control Account for Supplier ──
        var parent = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "2101");
        if (parent != null)
        {
            supplier.MainAccountId = parent.Id;
        }

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = supplier.Id }, new SupplierDto(
            supplier.Id, supplier.Name, supplier.Phone, supplier.CompanyName,
            supplier.TaxNumber, supplier.Email, supplier.Address, supplier.IsActive,
            supplier.TotalPurchases, supplier.TotalPaid, 0, 0,
            supplier.AttachmentUrl, supplier.AttachmentPublicId
        ));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSupplierDto dto)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id);
        if (supplier == null) return NotFound();

        supplier.Name        = dto.Name.Trim();
        supplier.Phone       = dto.Phone.Trim();
        supplier.CompanyName = dto.CompanyName?.Trim();
        supplier.TaxNumber   = dto.TaxNumber?.Trim();
        supplier.Email       = dto.Email?.Trim().ToLower();
        supplier.Address     = dto.Address?.Trim();
        supplier.IsActive    = dto.IsActive;
        supplier.AttachmentUrl = dto.AttachmentUrl;
        supplier.AttachmentPublicId = dto.AttachmentPublicId;
        supplier.MainAccountId = dto.MainAccountId;
        supplier.UpdatedAt   = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return Ok(new SupplierDto(
            supplier.Id, supplier.Name, supplier.Phone, supplier.CompanyName,
            supplier.TaxNumber, supplier.Email, supplier.Address, supplier.IsActive,
            supplier.TotalPurchases, supplier.TotalPaid,
            supplier.TotalPurchases - supplier.TotalPaid,
            await _db.PurchaseInvoices.CountAsync(i => i.SupplierId == supplier.Id),
            supplier.AttachmentUrl, supplier.AttachmentPublicId
        ));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var supplier = await _db.Suppliers.Include(s => s.Invoices).FirstOrDefaultAsync(s => s.Id == id);
        if (supplier == null) return NotFound();

        if (supplier.Invoices.Any())
            return BadRequest(new { message = "لا يمكن حذف مورد مسجل له فواتير مشتريات. يرجى حذف الفواتير أولاً." });

        _db.Suppliers.Remove(supplier);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

// ══════════════════════════════════════════════════════
// PURCHASE INVOICES
// ══════════════════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class PurchaseInvoicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProductService _productService;
    private readonly IInventoryService _inventory;
    private readonly ILogger<PurchaseInvoicesController> _logger;
    private readonly SequenceService _seq;
    private readonly IPdfService _pdf;

    public PurchaseInvoicesController(AppDbContext db, IServiceScopeFactory scopeFactory, IProductService productService, IInventoryService inventory, ILogger<PurchaseInvoicesController> logger, SequenceService seq, IPdfService pdf)
    {
        _db             = db;
        _scopeFactory   = scopeFactory;
        _productService = productService;
        _inventory      = inventory;
        _logger         = logger;
        _seq            = seq;
        _pdf            = pdf;
    }

    private async Task<List<ProductUnit>> GetUnitsListAsync() => await _db.ProductUnits.AsNoTracking().ToListAsync();
    private decimal GetMultiplier(List<ProductUnit> units, string? unitStr)
    {
        if (string.IsNullOrWhiteSpace(unitStr)) return 1M;
        return units.FirstOrDefault(u => u.Symbol == unitStr || u.NameAr == unitStr || u.NameEn == unitStr)?.Multiplier ?? 1M;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int?    supplierId = null,
        [FromQuery] string? status     = null,
        [FromQuery] string? search     = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.PurchaseInvoices
            .AsNoTracking()
            .Include(i => i.Supplier)
            .AsQueryable();

        if (supplierId.HasValue) q = q.Where(i => i.SupplierId == supplierId.Value);
        if (fromDate.HasValue)   q = q.Where(i => i.InvoiceDate >= fromDate.Value.Date);
        if (toDate.HasValue)     q = q.Where(i => i.InvoiceDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));
        if (!string.IsNullOrEmpty(status))
        {
            var stList = status.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var parsedStatuses = new List<PurchaseInvoiceStatus>();
            foreach (var s in stList)
            {
                if (Enum.TryParse<PurchaseInvoiceStatus>(s.Trim(), out var st))
                    parsedStatuses.Add(st);
            }
            if (parsedStatuses.Count > 0)
                q = q.Where(i => parsedStatuses.Contains(i.Status));
        }
        if (!string.IsNullOrEmpty(search))
            q = q.Where(i => i.InvoiceNumber.Contains(search)
                           || (i.SupplierInvoiceNumber != null && i.SupplierInvoiceNumber.Contains(search))
                           || i.Supplier.Name.Contains(search));

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(i => i.InvoiceDate)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(i => new PurchaseInvoiceSummaryDto(
                i.Id, i.InvoiceNumber, i.SupplierInvoiceNumber, i.SupplierId, i.Supplier.Name,
                i.PaymentTerms.ToString(), i.Status.ToString(),
                i.InvoiceDate, i.DueDate,
                i.TotalAmount, i.PaidAmount, i.TotalAmount - i.PaidAmount - i.ReturnedAmount
            )).ToListAsync();

        return Ok(new PaginatedResult<PurchaseInvoiceSummaryDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpGet("returns")]
    public async Task<IActionResult> GetReturns(
        [FromQuery] int? supplierId = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.PurchaseReturns
            .AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.Invoice)
            .AsQueryable();

        if (supplierId.HasValue) q = q.Where(r => r.SupplierId == supplierId.Value);
        if (fromDate.HasValue)   q = q.Where(r => r.ReturnDate >= fromDate.Value.Date);
        if (toDate.HasValue)     q = q.Where(r => r.ReturnDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));
        
        if (!string.IsNullOrEmpty(search))
            q = q.Where(r => r.ReturnNumber.Contains(search) 
                           || r.Invoice.InvoiceNumber.Contains(search) 
                           || r.Supplier.Name.Contains(search));

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(r => r.ReturnDate)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(r => new {
                r.Id,
                r.ReturnNumber,
                r.PurchaseInvoiceId,
                InvoiceNumber = r.Invoice.InvoiceNumber,
                r.SupplierId,
                SupplierName = r.Supplier.Name,
                r.ReturnDate,
                r.TotalAmount,
                r.SubTotal,
                r.TaxAmount,
                r.DiscountAmount,
                r.Notes
            }).ToListAsync();

        return Ok(new PaginatedResult<object>(items.Cast<object>().ToList(), total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }


    [HttpGet("returns/{id}")]
    public async Task<IActionResult> GetReturnDetail(int id)
    {
        var rtn = await _db.PurchaseReturns
            .Include(r => r.Supplier)
            .Include(r => r.Invoice)
            .Include(r => r.Items).ThenInclude(ri => ri.Product)
            .Include(r => r.Items).ThenInclude(ri => ri.ProductVariant)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (rtn == null) return NotFound();

        return Ok(new {
            rtn.Id,
            rtn.ReturnNumber,
            rtn.PurchaseInvoiceId,
            InvoiceNumber = rtn.Invoice.InvoiceNumber,
            rtn.SupplierId,
            SupplierName = rtn.Supplier.Name,
            rtn.ReturnDate,
            rtn.SubTotal,
            rtn.TaxAmount,
            rtn.DiscountAmount,
            rtn.TotalAmount,
            rtn.Notes,
            rtn.ReferenceNumber,
            Items = rtn.Items.Select(ri => new {
                ri.Id,
                ri.PurchaseInvoiceItemId,
                ri.ProductId,
                ProductName = ri.Product?.NameAr,
                ri.ProductVariantId,
                Size = ri.ProductVariant?.Size,
                Color = ri.ProductVariant?.ColorAr,
                ri.Quantity,
                ri.UnitCost,
                ri.TotalCost
            }).ToList()
        });
    }

    [HttpGet("{id}")]

    public async Task<IActionResult> GetById(int id)
    {
        var pUnits = await GetUnitsListAsync();
        var inv = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Include(i => i.Items).ThenInclude(it => it.Product)
            .Include(i => i.Items).ThenInclude(it => it.ProductVariant)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (inv == null) return NotFound();

        return Ok(new PurchaseInvoiceDetailDto(
            inv.Id, inv.InvoiceNumber, inv.SupplierInvoiceNumber,
            new SupplierBasicDto(inv.Supplier.Id, inv.Supplier.Name, inv.Supplier.Phone, inv.Supplier.CompanyName),
            inv.PaymentTerms.ToString(), inv.Status.ToString(),
            inv.InvoiceDate, inv.DueDate,
            inv.SubTotal, inv.TaxPercent, inv.TaxAmount, inv.DiscountAmount, inv.TotalAmount,
            inv.PaidAmount, inv.TotalAmount - inv.PaidAmount - inv.ReturnedAmount, inv.Notes,
            inv.Items.Select(it => new PurchaseItemDto(
                it.Id, it.Description, it.ProductId, 
                it.Product?.SKU, it.Product?.NameAr, 
                it.ProductVariantId, it.ProductVariant?.Size, it.ProductVariant?.ColorAr,
                it.Unit, GetMultiplier(pUnits, it.Unit), it.Quantity, it.ReturnedQuantity, it.UnitCost, it.TotalCost
            )).ToList(),
            inv.Payments.Select(p => new SupplierPaymentSummaryDto(
                p.Id, p.PaymentNumber, inv.Supplier.Name, inv.InvoiceNumber,
                p.PaymentDate, p.Amount, p.PaymentMethod.ToString(), p.AccountName, p.Notes,
                p.AttachmentUrl, p.AttachmentPublicId
            )).ToList(),
            inv.AttachmentUrl, inv.AttachmentPublicId
        ));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePurchaseInvoiceDto dto)
    {
        if (!dto.Items.Any())
            return BadRequest(new { message = "يجب إضافة صنف واحد على الأقل" });

        foreach (var item in dto.Items)
        {
            if (item.ProductId.HasValue && !item.ProductVariantId.HasValue)
            {
                var hasVariants = await _db.ProductVariants.AnyAsync(v => v.ProductId == item.ProductId.Value);
                if (hasVariants)
                {
                    return BadRequest(new { message = $"يجب اختيار المقاس واللون للصنف: {item.Description}" });
                }
            }
        }

        var pUnits = await GetUnitsListAsync();

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId);
        if (supplier == null)
            return BadRequest(new { message = "المورد غير موجود" });

        var invNo = await _seq.NextAsync("PO", async (db, pattern) =>
        {
            var max = await db.PurchaseInvoices
                .Where(i => EF.Functions.Like(i.InvoiceNumber, pattern))
                .Select(i => i.InvoiceNumber)
                .ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                      .DefaultIfEmpty(0).Max();
        });

        var invoice = new PurchaseInvoice
        {
            InvoiceNumber         = invNo,
            SupplierInvoiceNumber = dto.SupplierInvoiceNumber,
            SupplierId            = dto.SupplierId,
            PaymentTerms          = dto.PaymentTerms,
            InvoiceDate           = dto.InvoiceDate,
            PaymentTermDays       = dto.PaymentTermDays,
            DueDate               = dto.PaymentTerms == PaymentTerms.Cash ? null : (dto.PaymentTermDays.HasValue ? dto.InvoiceDate.AddDays(dto.PaymentTermDays.Value) : dto.DueDate),
            TaxPercent            = dto.TaxPercent,
            DiscountAmount        = dto.DiscountAmount,
            Notes                 = dto.Notes,
            Status                = dto.PaymentTerms == PaymentTerms.Cash ? PurchaseInvoiceStatus.Paid : PurchaseInvoiceStatus.Received,
            AttachmentUrl         = dto.AttachmentUrl,
            AttachmentPublicId    = dto.AttachmentPublicId,
            CreatedAt             = TimeHelper.GetEgyptTime(),
            VendorAccountId       = dto.VendorAccountId,
            InventoryAccountId    = dto.InventoryAccountId,
            ExpenseAccountId      = dto.ExpenseAccountId,
            VatAccountId          = dto.VatAccountId,
            CashAccountId         = dto.CashAccountId
        };

        decimal subtotal = 0;
        foreach (var item in dto.Items)
        {
            var total = item.Quantity * item.UnitCost;
            invoice.Items.Add(new PurchaseInvoiceItem
            {
                Description = item.Description,
                ProductId   = item.ProductId,
                ProductVariantId = item.ProductVariantId,
                Unit        = item.Unit,
                Quantity    = item.Quantity,
                UnitCost    = item.UnitCost,
                TotalCost   = total,
                CreatedAt   = TimeHelper.GetEgyptTime()
            });
            subtotal += total;

            if (item.ProductId.HasValue)
            {
                var multiplier = GetMultiplier(pUnits, item.Unit);
                var actualQty = (int)Math.Round(item.Quantity * multiplier);
                await _inventory.LogMovementAsync(InventoryMovementType.Purchase, actualQty, item.ProductId, item.ProductVariantId, invNo, "Purchase Invoice receipt", User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                
                // ── Auto-update Product Cost Price ──
                var product = await _db.Products.FindAsync(item.ProductId.Value);
                if (product != null)
                {
                    product.CostPrice = multiplier > 0 ? Math.Round(item.UnitCost / multiplier, 2) : item.UnitCost;
                    product.UpdatedAt = TimeHelper.GetEgyptTime();
                }
            }
        }

        invoice.SubTotal    = subtotal;
        invoice.TaxAmount   = Math.Round(subtotal * (dto.TaxPercent / 100), 2);
        invoice.TotalAmount = (subtotal + invoice.TaxAmount) - dto.DiscountAmount;
        supplier.TotalPurchases += invoice.TotalAmount;

        if (dto.PaymentTerms == PaymentTerms.Cash)
        {
            invoice.PaidAmount = invoice.TotalAmount;
            supplier.TotalPaid += invoice.TotalAmount;

            var pNo = await _seq.NextAsync("SP", async (db, pattern) =>
            {
                var max = await db.SupplierPayments
                    .Where(p => EF.Functions.Like(p.PaymentNumber, pattern))
                    .Select(p => p.PaymentNumber)
                    .ToListAsync();
                return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                          .DefaultIfEmpty(0).Max();
            });
            invoice.Payments.Add(new SupplierPayment
            {
                PaymentNumber = pNo,
                SupplierId    = supplier.Id,
                Amount        = invoice.TotalAmount,
                PaymentDate   = invoice.InvoiceDate,
                PaymentMethod = PaymentMethod_Purchase.Cash,
                AccountName   = "الخزينة (آلي)",
                Notes         = $"سداد تلقائي لفاتورة {invNo}",
                CreatedAt     = TimeHelper.GetEgyptTime()
            });
        }

        _db.PurchaseInvoices.Add(invoice);
        await _db.SaveChangesAsync();

        _ = PostJournalWithRetryAsync(invoice.Id, invoice.InvoiceNumber, isReturn: false);

        return CreatedAtAction(nameof(GetById), new { id = invoice.Id }, new { id = invoice.Id, invoiceNumber = invoice.InvoiceNumber });
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePurchaseInvoiceDto dto)
    {
        var inv = await _db.PurchaseInvoices
            .Include(i => i.Items)
            .Include(i => i.Supplier)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (inv == null) return NotFound();

        if (dto.Items != null)
        {
            foreach (var item in dto.Items)
            {
                if (item.ProductId.HasValue && !item.ProductVariantId.HasValue)
                {
                    var hasVariants = await _db.ProductVariants.AnyAsync(v => v.ProductId == item.ProductId.Value);
                    if (hasVariants)
                    {
                        return BadRequest(new { message = $"يجب اختيار المقاس واللون للصنف بالتعديل: {item.Description}" });
                    }
                }
            }
        }

        var pUnits = await GetUnitsListAsync();

        var oldStockMap = inv.Items
            .Where(x => x.ProductId.HasValue)
            .GroupBy(x => new { x.ProductId, x.ProductVariantId })
            .ToDictionary(g => g.Key, g => g.Sum(x => (int)Math.Round(x.Quantity * GetMultiplier(pUnits, x.Unit))));

        inv.Supplier.TotalPurchases -= inv.TotalAmount;

        // Use the global DTO properties
        inv.PaymentTerms = dto.PaymentTerms;
        inv.InvoiceDate = dto.InvoiceDate;
        inv.DueDate = dto.DueDate;
        inv.TaxPercent = dto.TaxPercent;
        inv.DiscountAmount = dto.DiscountAmount;
        inv.Notes = dto.Notes;
        if (dto.SupplierInvoiceNumber != null) inv.SupplierInvoiceNumber = dto.SupplierInvoiceNumber;

        _db.PurchaseInvoiceItems.RemoveRange(inv.Items);
        inv.Items.Clear();

        decimal subtotal = 0;
        if (dto.Items != null)
        {
            foreach (var item in dto.Items)
            {
                var total = Math.Round(item.Quantity * item.UnitCost, 2);
                subtotal += total;
                inv.Items.Add(new PurchaseInvoiceItem
                {
                    ProductId = item.ProductId,
                    ProductVariantId = item.ProductVariantId,
                    Description = item.Description,
                    Unit = item.Unit ?? "Unit",
                    Quantity = item.Quantity,
                    UnitCost = item.UnitCost,
                    TotalCost = total
                });
            }
        }

        inv.SubTotal = subtotal;
        inv.TaxAmount = Math.Round(subtotal * (inv.TaxPercent / 100), 2);
        inv.TotalAmount = (subtotal + inv.TaxAmount) - inv.DiscountAmount;
        inv.Supplier.TotalPurchases += inv.TotalAmount;

        // ── Auto-update Product Cost Prices from updated items ──
        foreach (var item in inv.Items.Where(i => i.ProductId.HasValue))
        {
            var product = await _db.Products.FindAsync(item.ProductId!.Value);
            if (product != null)
            {
                var multiplier = GetMultiplier(pUnits, item.Unit);
                product.CostPrice = multiplier > 0 ? Math.Round(item.UnitCost / multiplier, 2) : item.UnitCost;
                product.UpdatedAt = TimeHelper.GetEgyptTime();
            }
        }

        await _db.SaveChangesAsync();

        var newStockMap = inv.Items
            .Where(x => x.ProductId.HasValue)
            .GroupBy(x => new { x.ProductId, x.ProductVariantId })
            .ToDictionary(g => g.Key, g => g.Sum(x => (int)Math.Round(x.Quantity * GetMultiplier(pUnits, x.Unit))));

        var keys = oldStockMap.Keys.Union(newStockMap.Keys).Distinct();
        foreach (var key in keys)
        {
            var oldQty = oldStockMap.GetValueOrDefault(key, 0);
            var newQty = newStockMap.GetValueOrDefault(key, 0);
            var diff = newQty - oldQty;
            if (diff != 0)
            {
                await _inventory.LogMovementAsync(InventoryMovementType.Adjustment, diff, key.ProductId, key.ProductVariantId, inv.InvoiceNumber, $"Edit Inv #{inv.InvoiceNumber}", User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            }
        }

        await _db.SaveChangesAsync();

        _ = PostJournalWithRetryAsync(id, inv.InvoiceNumber, isReturn: false);

        return Ok(new { id = inv.Id, invoiceNumber = inv.InvoiceNumber });
    }

    [AcceptVerbs("PATCH", "PUT")]
    [Route("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdatePurchaseStatusDto dto)
    {
        var inv = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Include(i => i.Payments)
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (inv == null) return NotFound();

        var oldStatus = inv.Status;
        if (oldStatus == dto.Status) return Ok(new { id = inv.Id, status = inv.Status.ToString() });

        // ── 1. Handle Stock Reversal (If moving FROM received TO inactive) ──
        bool wasInStock = (oldStatus == PurchaseInvoiceStatus.Received || oldStatus == PurchaseInvoiceStatus.Paid || oldStatus == PurchaseInvoiceStatus.PartPaid);
        bool willBeOut  = (dto.Status == PurchaseInvoiceStatus.Cancelled || dto.Status == PurchaseInvoiceStatus.Returned);

        var pUnits = await GetUnitsListAsync();

        if (wasInStock && willBeOut)
        {
            foreach (var item in inv.Items)
            {
                if (item.ProductId.HasValue)
                {
                    var actualQty = (int)Math.Round(item.Quantity * GetMultiplier(pUnits, item.Unit));
                    await _inventory.LogMovementAsync(
                        dto.Status == PurchaseInvoiceStatus.Returned ? InventoryMovementType.ReturnOut : InventoryMovementType.Adjustment,
                        -actualQty, // Reverse the previous increase
                        item.ProductId,
                        item.ProductVariantId,
                        inv.InvoiceNumber,
                        $"Purchase status changed: {oldStatus} -> {dto.Status}",
                        User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    );
                }
            }
        }
        // Case: If moving FROM Draft/Cancelled TO Received/Paid (Re-activating)
        else if (!wasInStock && (dto.Status == PurchaseInvoiceStatus.Received || dto.Status == PurchaseInvoiceStatus.Paid))
        {
             foreach (var item in inv.Items)
             {
                if (item.ProductId.HasValue)
                {
                    var actualQty = (int)Math.Round(item.Quantity * GetMultiplier(pUnits, item.Unit));
                    await _inventory.LogMovementAsync(
                        InventoryMovementType.Purchase,
                        actualQty,
                        item.ProductId,
                        item.ProductVariantId,
                        inv.InvoiceNumber,
                        $"Purchase status activated: {dto.Status}",
                        User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    );
                }
             }
        }

        // ── 2. Handle Accounting & Totals ──
        if (dto.Status == PurchaseInvoiceStatus.Cancelled)
        {
            // Reverse Supplier Totals
            inv.Supplier.TotalPurchases -= inv.TotalAmount;
            inv.Supplier.TotalPaid -= inv.PaidAmount;

            var journalEntry = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Type == JournalEntryType.PurchaseInvoice && e.Reference == inv.InvoiceNumber);
            if (journalEntry != null) { 
                _db.JournalEntries.Remove(journalEntry);
            }
            foreach (var p in inv.Payments.ToList())
            {
                var pEntry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Reference == p.PaymentNumber && e.Type == JournalEntryType.PaymentVoucher);
                if (pEntry != null) {
                    _db.JournalEntries.Remove(pEntry);
                }
                _db.SupplierPayments.Remove(p);
            }
        }

        if (dto.Status == PurchaseInvoiceStatus.Returned)
        {
            _ = PostJournalWithRetryAsync(id, inv.InvoiceNumber, isReturn: true);
        }

        inv.Status = dto.Status;
        inv.UpdatedAt = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();
        return Ok(new { id = inv.Id, status = inv.Status.ToString() });
    }

    [HttpPost("{id}/return")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> ReturnPurchase(int id, [FromBody] ReturnPurchaseInvoiceDto dto)
    {
        if (dto.Items == null || !dto.Items.Any())
            return BadRequest(new { message = "يجب اختيار أصناف للإرجاع" });

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var inv = await _db.PurchaseInvoices
                .Include(i => i.Supplier)
                .Include(i => i.Items)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (inv == null) return NotFound();
            if (inv.Status == PurchaseInvoiceStatus.Cancelled)
                return BadRequest(new { message = "لا يمكن عمل مرتجع لفاتورة ملغاة" });

            // 1. Generate Return Document Number
            var returnNo = await _seq.NextAsync("PR", async (db, pattern) =>
            {
                var max = await db.PurchaseReturns
                    .Where(r => EF.Functions.Like(r.ReturnNumber, pattern))
                    .Select(r => r.ReturnNumber)
                    .ToListAsync();
                return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                          .DefaultIfEmpty(0).Max();
            });

            var pReturn = new PurchaseReturn
            {
                ReturnNumber      = returnNo,
                PurchaseInvoiceId = inv.Id,
                SupplierId        = inv.SupplierId,
                ReturnDate        = dto.ReturnDate != default ? dto.ReturnDate : TimeHelper.GetEgyptTime(),
                Notes             = dto.Notes,
                ReferenceNumber   = dto.ReferenceNumber,
                CreatedByUserId   = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            };

            decimal totalSubTotal = 0;
            var pUnits = await GetUnitsListAsync();

            foreach (var reqItem in dto.Items)
            {
                var invItem = inv.Items.FirstOrDefault(i => i.Id == reqItem.PurchaseInvoiceItemId);
                if (invItem == null) continue;

                var multiplier = GetMultiplier(pUnits, invItem.Unit);
                
                // A. Validate against Invoice quantity remaining (converting to pieces for accuracy)
                decimal invQtyInPieces = invItem.Quantity * multiplier;
                decimal returnedInPieces = invItem.ReturnedQuantity * multiplier;
                decimal remainingInPieces = invQtyInPieces - returnedInPieces;

                if (reqItem.Quantity > remainingInPieces)
                    return BadRequest(new { message = $"الكمية المطلوبة ({reqItem.Quantity} قطعة) أكبر من المتبقي في الفاتورة ({remainingInPieces} قطعة) للصنف {invItem.Description}" });

                // B. Validate against PHYSICAL STOCK
                var physicalStock = await _inventory.GetCurrentStockAsync(invItem.ProductId, invItem.ProductVariantId);
                if (reqItem.Quantity > physicalStock)
                    return BadRequest(new { message = $"لا يوجد رصيد كافي في المخزن لإتمام المرتجع. الرصيد المتاح حالياً: {physicalStock} قطعة، مخصوم منها المبيعات وأي حركات سابقة." });

                // C. Update Invoice Item (Store returned portion in invoice units)
                decimal returnedInOriginalUnits = multiplier > 0 ? (reqItem.Quantity / multiplier) : reqItem.Quantity;
                invItem.ReturnedQuantity += returnedInOriginalUnits;

                // D. Create Return Item
                decimal unitCostPerPiece = multiplier > 0 ? (invItem.UnitCost / multiplier) : invItem.UnitCost;
                var itemSubTotal = Math.Round(reqItem.Quantity * unitCostPerPiece, 2);
                
                pReturn.Items.Add(new PurchaseReturnItem
                {
                    PurchaseInvoiceItemId = invItem.Id,
                    ProductId             = invItem.ProductId,
                    ProductVariantId      = invItem.ProductVariantId,
                    Quantity              = reqItem.Quantity,
                    UnitCost              = unitCostPerPiece,
                    TotalCost             = itemSubTotal
                });

                totalSubTotal += itemSubTotal;

                // E. Log Inventory Movement
                if (invItem.ProductId.HasValue)
                {
                    await _inventory.LogMovementAsync(
                        InventoryMovementType.ReturnOut,
                        -(int)Math.Round(reqItem.Quantity), // Outward movement
                        invItem.ProductId,
                        invItem.ProductVariantId,
                        returnNo,
                        $"Purchase Return for Inv #{inv.InvoiceNumber}",
                        pReturn.CreatedByUserId,
                        unitCostPerPiece
                    );
                }
            }

            if (totalSubTotal <= 0)
                return BadRequest(new { message = "لم يتم تحديد أي كميات صالحة للإرجاع" });

            // 2. Calculate Prorated Tax & Discount
            // Ratio = Returned Subtotal / Original Invoice Subtotal
            decimal prorationRatio = totalSubTotal / (inv.SubTotal > 0 ? inv.SubTotal : 1);
            pReturn.SubTotal       = totalSubTotal;
            pReturn.TaxAmount      = Math.Round(inv.TaxAmount * prorationRatio, 2);
            pReturn.DiscountAmount = Math.Round(inv.DiscountAmount * prorationRatio, 2);
            pReturn.TotalAmount    = (pReturn.SubTotal + pReturn.TaxAmount) - pReturn.DiscountAmount;

            // 3. Update Invoice & Supplier Ledger
            inv.ReturnedAmount += pReturn.TotalAmount;
            inv.Supplier.TotalPurchases -= pReturn.TotalAmount;

            var totalQty = inv.Items.Sum(i => i.Quantity);
            var totalReturnedQty = inv.Items.Sum(i => i.ReturnedQuantity);

            if (totalReturnedQty >= totalQty)
                inv.Status = PurchaseInvoiceStatus.Returned;
            else if (totalReturnedQty > 0)
                inv.Status = PurchaseInvoiceStatus.PartiallyReturned;

            inv.UpdatedAt = TimeHelper.GetEgyptTime();

            _db.PurchaseReturns.Add(pReturn);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            // 4. Trigger Accounting Sync
            _ = PostReturnJournalByReturnRecordWithRetryAsync(pReturn.Id, pReturn.ReturnNumber);

            return Ok(new { 
                id = inv.Id, 
                returnId = pReturn.Id,
                returnNumber = pReturn.ReturnNumber,
                status = inv.Status.ToString() 
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to process purchase return for invoice {Id}", id);
            return StatusCode(500, new { message = "خطأ داخلي أثناء معالجة المرتجع" });
        }
    }

    private async Task PostReturnJournalByReturnRecordWithRetryAsync(int returnId, string returnNumber)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var acc = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                var pReturn = await db.PurchaseReturns
                    .Include(r => r.Supplier)
                    .Include(r => r.Invoice)
                    .Include(r => r.Items)
                    .FirstAsync(r => r.Id == returnId);

                await acc.PostPurchaseReturnAsync(pReturn);
                return; 
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "[Accounting] Return Journal posting attempt {Attempt}/{Max} failed for return {Number}. Retrying...", attempt, maxAttempts, returnNumber);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Accounting] Return Journal posting permanently failed for return {Number}.", returnNumber);
            }
        }
    }


    private async Task PostPartialJournalWithRetryAsync(int invoiceId, string invoiceNumber, decimal returnedSubTotal, decimal returnedTaxAmount, decimal returnedDiscountAmount)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var acc = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var inv = await db.PurchaseInvoices
                    .Include(i => i.Supplier)
                    .FirstAsync(i => i.Id == invoiceId);

                await acc.PostPurchaseReturnAsync(inv, returnedSubTotal, returnedTaxAmount, returnedDiscountAmount);
                return; 
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "[Accounting] Partial Return Journal posting attempt {Attempt}/{Max} failed for invoice {Number}. Retrying...", attempt, maxAttempts, invoiceNumber);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Accounting] Partial Return Journal posting permanently failed for invoice {Number}.", invoiceNumber);
            }
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Delete(int id)
    {
        var inv = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Include(i => i.Items)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (inv == null) return NotFound();

        inv.Supplier.TotalPurchases -= inv.TotalAmount;
        inv.Supplier.TotalPaid -= inv.PaidAmount;

        foreach (var item in inv.Items)
        {
            if (item.ProductId.HasValue)
            {
                await _inventory.LogMovementAsync(InventoryMovementType.Adjustment, -item.Quantity, item.ProductId, item.ProductVariantId, inv.InvoiceNumber, "Purchase Invoice Deleted", User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            }
        }

        foreach (var p in inv.Payments.ToList())
        {
            var pEntry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Reference == p.PaymentNumber && e.Type == JournalEntryType.PaymentVoucher);
            if (pEntry != null) _db.JournalEntries.Remove(pEntry);
            _db.SupplierPayments.Remove(p);
        }

        var invoiceEntry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Type == JournalEntryType.PurchaseInvoice && e.Reference == inv.InvoiceNumber);
        if (invoiceEntry != null) _db.JournalEntries.Remove(invoiceEntry);

        _db.PurchaseInvoices.Remove(inv);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Posts the accounting journal for a purchase invoice in the background.
    // ══════════════════════════════════════════════════
    // GET /api/purchaseinvoices/{id}/pdf
    // ══════════════════════════════════════════════════
    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetPdf(int id)
    {
        var invoice = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice == null) return NotFound();

        var pdfBytes = await _pdf.GeneratePurchaseInvoicePdfAsync(invoice);
        return File(pdfBytes, "application/pdf", $"purchase-{invoice.InvoiceNumber}.pdf");
    }

    /// Retries up to 3 times with exponential back-off before giving up and logging an error.
    /// </summary>
    private async Task PostJournalWithRetryAsync(int invoiceId, string invoiceNumber, bool isReturn)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var acc = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var inv = await db.PurchaseInvoices
                    .Include(i => i.Supplier)
                    .Include(i => i.Payments)
                    .FirstAsync(i => i.Id == invoiceId);

                if (isReturn)
                    await acc.PostPurchaseReturnAsync(inv);
                else
                    await acc.PostPurchaseInvoiceAsync(inv);

                return; // success
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex,
                    "[Accounting] Journal posting attempt {Attempt}/{Max} failed for invoice {Number}. Retrying...",
                    attempt, maxAttempts, invoiceNumber);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Accounting] Journal posting permanently failed for invoice {Number} after {Max} attempts.",
                    invoiceNumber, maxAttempts);
            }
        }
    }
}
