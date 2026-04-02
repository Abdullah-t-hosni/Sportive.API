using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Interfaces;

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
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var q = _db.Suppliers
            .Include(s => s.Invoices)
            .Where(s => !s.IsDeleted)
            .AsQueryable();

        if (isActive.HasValue) q = q.Where(s => s.IsActive == isActive.Value);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(s => s.Name.Contains(search) || s.Phone.Contains(search)
                           || (s.CompanyName != null && s.CompanyName.Contains(search)));

        var total = await q.CountAsync();
        var items = await q.OrderBy(s => s.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new SupplierDto(
                s.Id, s.Name, s.Phone, s.CompanyName, s.TaxNumber, s.Email, s.Address,
                s.IsActive, s.TotalPurchases, s.TotalPaid, s.TotalPurchases - s.TotalPaid,
                s.Invoices.Count(i => !i.IsDeleted),
                s.AttachmentUrl, s.AttachmentPublicId
            )).ToListAsync();

        return Ok(new PaginatedResult<SupplierDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var s = await _db.Suppliers.Include(s => s.Invoices).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (s == null) return NotFound();
        return Ok(new SupplierDto(s.Id, s.Name, s.Phone, s.CompanyName, s.TaxNumber,
            s.Email, s.Address, s.IsActive, s.TotalPurchases, s.TotalPaid,
            s.TotalPurchases - s.TotalPaid, s.Invoices.Count(i => !i.IsDeleted),
            s.AttachmentUrl, s.AttachmentPublicId));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSupplierDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Phone))
            return BadRequest(new { message = "الاسم ورقم الهاتف إلزاميان" });

        var supplier = new Supplier
        {
            Name        = dto.Name.Trim(),
            Phone       = dto.Phone.Trim(),
            CompanyName = dto.CompanyName?.Trim(),
            TaxNumber   = dto.TaxNumber?.Trim(),
            Email       = dto.Email?.Trim().ToLower(),
            Address     = dto.Address?.Trim(),
            CreatedAt   = DateTime.UtcNow,
            AttachmentUrl = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId
        };

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();

        // ── Auto-create Supplier Account in Chart of Accounts ──
        var parent = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "2101");
        if (parent != null)
        {
            var maxCode = await _db.Accounts.Where(a => a.ParentId == parent.Id).MaxAsync(a => (string?)a.Code);
            long nextCodeNum = 1;
            if (maxCode != null && maxCode.StartsWith("2101") && long.TryParse(maxCode.Substring(4), out var existingNum)) {
                nextCodeNum = existingNum + 1;
            } else {
                nextCodeNum = 1;
            }
            var newCode = $"2101{nextCodeNum:D4}";

            var account = new Account
            {
                Code = newCode,
                NameAr = $"مورد - {supplier.Name}",
                Type = AccountType.Liability,
                Nature = AccountNature.Credit,
                ParentId = parent.Id,
                Level = parent.Level + 1,
                IsLeaf = true,
                AllowPosting = true,
                IsSystem = false,
                CreatedAt = DateTime.UtcNow
            };
            _db.Accounts.Add(account);
            await _db.SaveChangesAsync();

            supplier.MainAccountId = account.Id;
            await _db.SaveChangesAsync();
        }

        return CreatedAtAction(nameof(GetById), new { id = supplier.Id }, supplier);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSupplierDto dto)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);
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
        supplier.UpdatedAt   = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(supplier);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);
        if (supplier == null) return NotFound();
        supplier.IsDeleted = true;
        supplier.UpdatedAt = DateTime.UtcNow;
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

    public PurchaseInvoicesController(AppDbContext db, IServiceScopeFactory scopeFactory, IProductService productService, IInventoryService inventory)
    {
        _db           = db;
        _scopeFactory = scopeFactory;
        _productService = productService;
        _inventory = inventory;
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
            .Include(i => i.Supplier)
            .Where(i => !i.IsDeleted)
            .AsQueryable();

        if (supplierId.HasValue) q = q.Where(i => i.SupplierId == supplierId.Value);
        if (fromDate.HasValue)   q = q.Where(i => i.InvoiceDate >= fromDate.Value);
        if (toDate.HasValue)     q = q.Where(i => i.InvoiceDate <= toDate.Value);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<PurchaseInvoiceStatus>(status, out var st))
            q = q.Where(i => i.Status == st);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(i => i.InvoiceNumber.Contains(search)
                           || (i.SupplierInvoiceNumber != null && i.SupplierInvoiceNumber.Contains(search))
                           || i.Supplier.Name.Contains(search));

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(i => i.InvoiceDate)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(i => new PurchaseInvoiceSummaryDto(
                i.Id, i.InvoiceNumber, i.SupplierInvoiceNumber, i.Supplier.Name,
                i.PaymentTerms.ToString(), i.Status.ToString(),
                i.InvoiceDate, i.DueDate,
                i.TotalAmount, i.PaidAmount, i.TotalAmount - i.PaidAmount
            )).ToListAsync();

        return Ok(new PaginatedResult<PurchaseInvoiceSummaryDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var inv = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Include(i => i.Items).ThenInclude(it => it.Product)
            .Include(i => i.Items).ThenInclude(it => it.ProductVariant)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted);

        if (inv == null) return NotFound();

        return Ok(new PurchaseInvoiceDetailDto(
            inv.Id, inv.InvoiceNumber, inv.SupplierInvoiceNumber,
            new SupplierBasicDto(inv.Supplier.Id, inv.Supplier.Name, inv.Supplier.Phone, inv.Supplier.CompanyName),
            inv.PaymentTerms.ToString(), inv.Status.ToString(),
            inv.InvoiceDate, inv.DueDate,
            inv.SubTotal, inv.TaxPercent, inv.TaxAmount, inv.DiscountAmount, inv.TotalAmount,
            inv.PaidAmount, inv.TotalAmount - inv.PaidAmount, inv.Notes,
            inv.Items.Select(it => new PurchaseItemDto(
                it.Id, it.Description, it.ProductId, 
                it.Product?.SKU, it.Product?.NameAr, 
                it.ProductVariantId, it.ProductVariant?.Size, it.ProductVariant?.ColorAr,
                it.Unit, it.Quantity, it.UnitCost, it.TotalCost
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

        if (dto.Items.Any(i => i.ProductId.HasValue && !i.ProductVariantId.HasValue))
            return BadRequest(new { message = "إلزامي اختيار المقاس واللون لكل صنف في الفاتورة" });

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId && !s.IsDeleted);
        if (supplier == null)
            return BadRequest(new { message = "المورد غير موجود" });

        var year  = DateTime.UtcNow.Year % 100;
        var count = await _db.PurchaseInvoices.IgnoreQueryFilters().CountAsync() + 1;
        var invNo = $"PO-{year}{count:D4}";

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
            Status                = dto.PaymentTerms == PaymentTerms.Cash ? PurchaseInvoiceStatus.Paid : PurchaseInvoiceStatus.Draft,
            AttachmentUrl         = dto.AttachmentUrl,
            AttachmentPublicId    = dto.AttachmentPublicId,
            CreatedAt             = DateTime.UtcNow,
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
                CreatedAt   = DateTime.UtcNow
            });
            subtotal += total;

            if (item.ProductId.HasValue)
            {
                await _inventory.LogMovementAsync(InventoryMovementType.Purchase, item.Quantity, item.ProductId, item.ProductVariantId, invNo, "Purchase Invoice receipt", User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
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

            var pCount = await _db.SupplierPayments.IgnoreQueryFilters().CountAsync() + 1;
            invoice.Payments.Add(new SupplierPayment
            {
                PaymentNumber = $"PV-{year}{pCount:D4}",
                SupplierId    = supplier.Id,
                Amount        = invoice.TotalAmount,
                PaymentDate   = invoice.InvoiceDate,
                PaymentMethod = PaymentMethod_Purchase.Cash,
                AccountName   = "الخزينة (آلي)",
                Notes         = $"سداد تلقائي لفاتورة {invNo}",
                CreatedAt     = DateTime.UtcNow
            });
        }

        _db.PurchaseInvoices.Add(invoice);
        await _db.SaveChangesAsync();

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            try {
                var invInner = await db.PurchaseInvoices.Include(i => i.Supplier).Include(i => i.Payments).FirstAsync(i => i.Id == invoice.Id);
                await accounting.PostPurchaseInvoiceAsync(invInner); 
            } catch { }
        });

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
            .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted);

        if (inv == null) return NotFound();

        if (dto.Items != null && dto.Items.Any(i => i.ProductId.HasValue && !i.ProductVariantId.HasValue))
            return BadRequest(new { message = "إلزامي اختيار المقاس واللون لكل صنف في الفاتورة للتعديل" });

        var oldStockMap = inv.Items
            .Where(x => x.ProductId.HasValue)
            .GroupBy(x => new { x.ProductId, x.ProductVariantId })
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

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

        await _db.SaveChangesAsync();

        var newStockMap = inv.Items
            .Where(x => x.ProductId.HasValue)
            .GroupBy(x => new { x.ProductId, x.ProductVariantId })
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

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

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var acc = scope.ServiceProvider.GetRequiredService<IAccountingService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            try {
                var full = await db.PurchaseInvoices.Include(i => i.Supplier).FirstAsync(i => i.Id == id);
                await acc.PostPurchaseInvoiceAsync(full);
            } catch { }
        });

        return Ok(new { id = inv.Id, invoiceNumber = inv.InvoiceNumber });
    }

    [AcceptVerbs("PATCH", "PUT")]
    [Route("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdatePurchaseStatusDto dto)
    {
        var inv = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted);

        if (inv == null) return NotFound();

        if (dto.Status == PurchaseInvoiceStatus.Cancelled)
        {
            var journalEntry = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Type == JournalEntryType.PurchaseInvoice && e.Reference == inv.InvoiceNumber);
            if (journalEntry != null) { 
                journalEntry.IsDeleted = true; 
                foreach (var l in journalEntry.Lines) l.IsDeleted = true;
            }
            foreach (var p in inv.Payments)
            {
                p.IsDeleted = true;
                var pEntry = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Type == JournalEntryType.PaymentVoucher && e.Reference == p.PaymentNumber);
                if (pEntry != null) {
                    pEntry.IsDeleted = true;
                    foreach (var l in pEntry.Lines) l.IsDeleted = true;
                }
            }
        }

        if (dto.Status == PurchaseInvoiceStatus.Returned)
        {
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var acc = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                try {
                    var full = await db.PurchaseInvoices.Include(i => i.Supplier).FirstAsync(i => i.Id == id);
                    await acc.PostPurchaseReturnAsync(full); 
                } catch { }
            });
        }

        inv.Status = dto.Status;
        inv.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { id = inv.Id, status = inv.Status.ToString() });
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Delete(int id)
    {
        var inv = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Include(i => i.Items)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted);

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

        foreach (var p in inv.Payments)
        {
            p.IsDeleted = true;
            var pEntry = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Type == JournalEntryType.PaymentVoucher && e.Reference == p.PaymentNumber);
            if (pEntry != null) {
                pEntry.IsDeleted = true;
                foreach (var l in pEntry.Lines) l.IsDeleted = true;
            }
        }

        var invoiceEntry = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Type == JournalEntryType.PurchaseInvoice && e.Reference == inv.InvoiceNumber);
        if (invoiceEntry != null) {
            invoiceEntry.IsDeleted = true;
            foreach (var l in invoiceEntry.Lines) l.IsDeleted = true;
        }

        inv.IsDeleted = true;
        inv.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
