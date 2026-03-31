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

        // Auto-flag overdue
        var now = DateTime.UtcNow;

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

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId && !s.IsDeleted);
        if (supplier == null)
            return BadRequest(new { message = "المورد غير موجود" });

        // Generate invoice number
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
            Status                = dto.PaymentTerms == PaymentTerms.Cash
                ? PurchaseInvoiceStatus.Received : PurchaseInvoiceStatus.Draft,
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
            var invoiceItem = new PurchaseInvoiceItem
            {
                Description = item.Description,
                ProductId   = item.ProductId,
                ProductVariantId = item.ProductVariantId,
                Unit        = item.Unit,
                Quantity    = item.Quantity,
                UnitCost    = item.UnitCost,
                TotalCost   = total,
                CreatedAt   = DateTime.UtcNow,
            };
            invoice.Items.Add(invoiceItem);
            subtotal += total;

            // ── Inventory Movement Logging ──────────────────────
            if (item.ProductId.HasValue)
            {
                await _inventory.LogMovementAsync(
                    InventoryMovementType.Purchase,
                    item.Quantity,
                    item.ProductId,
                    item.ProductVariantId,
                    invNo,
                    $"Purchase Invoice receipt",
                    User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                );
            }
        }

        invoice.SubTotal    = subtotal;
        invoice.TaxAmount   = Math.Round(subtotal * (dto.TaxPercent / 100), 2);
        invoice.TotalAmount = (subtotal + invoice.TaxAmount) - dto.DiscountAmount;

        // Update supplier totals (Accrual)
        supplier.TotalPurchases += invoice.TotalAmount;

        // 🛡️ AUTO-PAYMENT FOR CASH INVOICES
        if (dto.PaymentTerms == PaymentTerms.Cash)
        {
            invoice.PaidAmount = invoice.TotalAmount;
            invoice.Status     = PurchaseInvoiceStatus.Paid;
            
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

        // ترحيل القيد المحاسبي
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var accountingInner = scope.ServiceProvider.GetRequiredService<IAccountingService>();
            var dbInner = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try 
            {
                var invoiceInner = await dbInner.PurchaseInvoices
                    .Include(i => i.Supplier)
                    .Include(i => i.Payments)
                    .FirstAsync(i => i.Id == invoice.Id);

                await accountingInner.PostPurchaseInvoiceAsync(invoiceInner); 
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Accounting] PostPurchaseInvoice failed for {invoice.InvoiceNumber}: {ex.Message}");
            }
        });

        return CreatedAtAction(nameof(GetById), new { id = invoice.Id },
            new { id = invoice.Id, invoiceNumber = invoice.InvoiceNumber });
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePurchaseInvoiceDto dto)
    {
        var inv = await _db.PurchaseInvoices
            .Include(i => i.Items)
            .Include(i => i.Supplier)
            .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted);

        if (inv == null) return NotFound();

        // ⚠️ Reverse previous supplier total to apply new one
        inv.Supplier.TotalPurchases -= inv.TotalAmount;

        if (dto.SupplierInvoiceNumber != null) inv.SupplierInvoiceNumber = dto.SupplierInvoiceNumber;
        if (dto.PaymentTerms.HasValue) 
        {
            inv.PaymentTerms = dto.PaymentTerms.Value;
        }
        if (dto.InvoiceDate.HasValue)   inv.InvoiceDate = dto.InvoiceDate.Value;
        if (dto.PaymentTermDays.HasValue) inv.PaymentTermDays = dto.PaymentTermDays.Value;

        // Auto-calculate DueDate
        if (inv.PaymentTerms == PaymentTerms.Cash)
        {
            inv.DueDate = null;
        }
        else if (dto.PaymentTermDays.HasValue)
        {
            inv.DueDate = inv.InvoiceDate.AddDays(dto.PaymentTermDays.Value);
        }
        else if (dto.DueDate.HasValue)
        {
            inv.DueDate = dto.DueDate.Value;
        }
        if (dto.TaxPercent.HasValue)    inv.TaxPercent  = dto.TaxPercent.Value;
        if (dto.DiscountAmount.HasValue) inv.DiscountAmount = dto.DiscountAmount.Value;
        if (dto.Notes != null)          inv.Notes       = dto.Notes;
        if (dto.Status.HasValue)        inv.Status      = dto.Status.Value;

        // Clean up old items or update them?
        // Simpler: Just update the header for now because updating items in a PUT is complex for stock
        // But the frontend seems to call this with a full body

        decimal subtotal = inv.Items.Sum(x => x.TotalCost);
        inv.SubTotal    = subtotal;
        inv.TaxAmount   = Math.Round(subtotal * (inv.TaxPercent / 100), 2);
        inv.TotalAmount = (subtotal + inv.TaxAmount) - inv.DiscountAmount;

        inv.Supplier.TotalPurchases += inv.TotalAmount;
        inv.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(inv);
    }

    [AcceptVerbs("PATCH", "PUT")]
    [Route("{id}/status")]
    [Route("{id}/status:{statusValue}")] // This specifically targets the :1 syntax seen in the logs
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdatePurchaseStatusDto? dto, [FromRoute] string? statusValue = null)
    {
        var inv = await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted);
        if (inv == null) return NotFound();

        PurchaseInvoiceStatus newStatus;
        if (dto != null) newStatus = dto.Status;
        else if (!string.IsNullOrEmpty(statusValue) && Enum.TryParse<PurchaseInvoiceStatus>(statusValue, out var st)) newStatus = st;
        else if (!string.IsNullOrEmpty(statusValue) && int.TryParse(statusValue, out var v)) newStatus = (PurchaseInvoiceStatus)v;
        else return BadRequest(new { message = "Invalid status" });

        inv.Status    = newStatus;
        inv.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        if (newStatus == PurchaseInvoiceStatus.Returned)
        {
            // Reduce Stock (Decrease)
            var invoiceWithItems = await _db.PurchaseInvoices.Include(i => i.Items).FirstAsync(i => i.Id == id);
            foreach (var item in invoiceWithItems.Items)
            {
                if (item.ProductId.HasValue)
                {
                    await _inventory.LogMovementAsync(
                        InventoryMovementType.ReturnOut,
                        -item.Quantity, // deduction
                        item.ProductId,
                        item.ProductVariantId,
                        inv.InvoiceNumber,
                        $"Purchase Return",
                        User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    );
                }
            }
            await _db.SaveChangesAsync(); // Save stock changes

            var fullInvoice = await _db.PurchaseInvoices
                .Include(i => i.Supplier)
                .FirstAsync(i => i.Id == id);

            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var accountingInner = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                var dbInner = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                try 
                {
                    var fullInvoiceInner = await dbInner.PurchaseInvoices
                        .Include(i => i.Supplier)
                        .FirstAsync(i => i.Id == id);

                    await accountingInner.PostPurchaseReturnAsync(fullInvoiceInner); 
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Accounting] PostPurchaseReturn failed for {id}: {ex.Message}");
                }
            });
        }

        return Ok(new { status = inv.Status.ToString() });
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

        // 1. Reverse Supplier Totals
        inv.Supplier.TotalPurchases -= inv.TotalAmount;
        inv.Supplier.TotalPaid -= inv.PaidAmount;

        // 2. Reverse Stock (Technically a deletion movement)
        foreach (var item in inv.Items)
        {
            if (item.ProductId.HasValue)
            {
                await _inventory.LogMovementAsync(
                    InventoryMovementType.Adjustment,
                    -item.Quantity, // Reverse the previous addition
                    item.ProductId,
                    item.ProductVariantId,
                    inv.InvoiceNumber,
                    $"Purchase Invoice Deleted",
                    User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                );
            }
        }

        // 3. Delete Associated Payments and their Journal Entries
        foreach (var p in inv.Payments)
        {
            p.IsDeleted = true;
            var pEntry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Type == JournalEntryType.PaymentVoucher && e.Reference == p.PaymentNumber);
            if (pEntry != null) pEntry.IsDeleted = true;
        }

        // 4. Delete the Invoice Journal Entry
        var invoiceEntry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Type == JournalEntryType.PurchaseInvoice && e.Reference == inv.InvoiceNumber);
        if (invoiceEntry != null) invoiceEntry.IsDeleted = true;

        inv.IsDeleted = true;
        inv.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record UpdatePurchaseStatusDto(PurchaseInvoiceStatus Status);

public record UpdatePurchaseInvoiceDto(
    string? SupplierInvoiceNumber,
    PaymentTerms? PaymentTerms,
    DateTime? InvoiceDate,
    int? PaymentTermDays,
    DateTime? DueDate,
    decimal? TaxPercent,
    decimal? DiscountAmount,
    string? Notes,
    PurchaseInvoiceStatus? Status
);

// ══════════════════════════════════════════════════════
// SUPPLIER PAYMENTS (VOUCHERS)
// ══════════════════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class SupplierPaymentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;

    public SupplierPaymentsController(AppDbContext db, IServiceScopeFactory scopeFactory)
    {
        _db           = db;
        _scopeFactory = scopeFactory;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int?    supplierId = null,
        [FromQuery] string? search     = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.SupplierPayments
            .Include(p => p.Supplier)
            .Include(p => p.Invoice)
            .Where(p => !p.IsDeleted)
            .AsQueryable();

        if (supplierId.HasValue) q = q.Where(p => p.SupplierId == supplierId.Value);
        if (fromDate.HasValue)   q = q.Where(p => p.PaymentDate >= fromDate.Value);
        if (toDate.HasValue)     q = q.Where(p => p.PaymentDate <= toDate.Value);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(p => p.PaymentNumber.Contains(search)
                           || p.Supplier.Name.Contains(search)
                           || p.Supplier.Phone.Contains(search));

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(p => p.PaymentDate)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(p => new SupplierPaymentSummaryDto(
                p.Id, p.PaymentNumber, p.Supplier.Name,
                p.Invoice != null ? p.Invoice.InvoiceNumber : null,
                p.PaymentDate, p.Amount, p.PaymentMethod.ToString(),
                p.AccountName, p.Notes,
                p.AttachmentUrl, p.AttachmentPublicId
            )).ToListAsync();

        return Ok(new PaginatedResult<SupplierPaymentSummaryDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSupplierPaymentDto dto)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId && !s.IsDeleted);
        if (supplier == null)
            return BadRequest(new { message = "المورد غير موجود" });

        if (dto.Amount <= 0)
            return BadRequest(new { message = "المبلغ يجب أن يكون أكبر من صفر" });

        // Voucher number
        var year  = DateTime.UtcNow.Year % 100;
        var count = await _db.SupplierPayments.IgnoreQueryFilters().CountAsync() + 1;
        var voucherNo = $"SP-{year}{count:D4}";

        var payment = new SupplierPayment
        {
            PaymentNumber     = voucherNo,
            SupplierId        = dto.SupplierId,
            PurchaseInvoiceId = dto.PurchaseInvoiceId,
            PaymentDate       = dto.PaymentDate,
            Amount            = dto.Amount,
            PaymentMethod     = dto.PaymentMethod,
            AccountName       = dto.AccountName.Trim(),
            Notes             = dto.Notes?.Trim(),
            ReferenceNumber   = dto.ReferenceNumber?.Trim(),
            CreatedByUserId   = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            AttachmentUrl     = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId,
            CreatedAt         = DateTime.UtcNow,
        };

        // Update supplier paid amount
        supplier.TotalPaid += dto.Amount;

        // Update invoice paid amount if linked
        if (dto.PurchaseInvoiceId.HasValue)
        {
            var invoice = await _db.PurchaseInvoices
                .FirstOrDefaultAsync(i => i.Id == dto.PurchaseInvoiceId && !i.IsDeleted);
            if (invoice != null)
            {
                invoice.PaidAmount += dto.Amount;
                invoice.Status = invoice.PaidAmount >= invoice.TotalAmount
                    ? PurchaseInvoiceStatus.Paid
                    : PurchaseInvoiceStatus.PartPaid;
                invoice.UpdatedAt = DateTime.UtcNow;
            }
        }

        _db.SupplierPayments.Add(payment);
        await _db.SaveChangesAsync();

        // ترحيل سند الصرف
        var paymentWithSupplier = await _db.SupplierPayments
            .Include(p => p.Supplier)
            .FirstAsync(p => p.Id == payment.Id);

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var accountingInner = scope.ServiceProvider.GetRequiredService<IAccountingService>();
            var dbInner = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try 
            {
                var paymentInner = await dbInner.SupplierPayments
                    .Include(p => p.Supplier)
                    .FirstAsync(p => p.Id == payment.Id);

                await accountingInner.PostSupplierPaymentAsync(paymentInner); 
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Accounting] PostSupplierPayment failed for {payment.PaymentNumber}: {ex.Message}");
            }
        });

        return CreatedAtAction(nameof(GetAll), new { },
            new { id = payment.Id, paymentNumber = payment.PaymentNumber });
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Delete(int id)
    {
        var payment = await _db.SupplierPayments
            .Include(p => p.Supplier)
            .Include(p => p.Invoice)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
        if (payment == null) return NotFound();

        // Reverse supplier total
        payment.Supplier.TotalPaid -= payment.Amount;

        // Reverse invoice
        if (payment.Invoice != null)
        {
            payment.Invoice.PaidAmount -= payment.Amount;
            payment.Invoice.Status = payment.Invoice.PaidAmount <= 0
                ? PurchaseInvoiceStatus.Draft
                : PurchaseInvoiceStatus.PartPaid;
        }

        payment.IsDeleted = true;
        payment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
