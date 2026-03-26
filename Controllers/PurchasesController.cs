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
[Authorize(Roles = "Admin")]
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
                s.Invoices.Count(i => !i.IsDeleted)
            )).ToListAsync();

        return Ok(new PaginatedResult<SupplierDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (s == null) return NotFound();
        return Ok(new SupplierDto(s.Id, s.Name, s.Phone, s.CompanyName, s.TaxNumber,
            s.Email, s.Address, s.IsActive, s.TotalPurchases, s.TotalPaid,
            s.TotalPurchases - s.TotalPaid, 0));
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
[Authorize(Roles = "Admin")]
public class PurchaseInvoicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;

    public PurchaseInvoicesController(AppDbContext db, IServiceScopeFactory scopeFactory)
    {
        _db           = db;
        _scopeFactory = scopeFactory;
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
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted);

        if (inv == null) return NotFound();

        return Ok(new PurchaseInvoiceDetailDto(
            inv.Id, inv.InvoiceNumber, inv.SupplierInvoiceNumber,
            new SupplierBasicDto(inv.Supplier.Id, inv.Supplier.Name, inv.Supplier.Phone, inv.Supplier.CompanyName),
            inv.PaymentTerms.ToString(), inv.Status.ToString(),
            inv.InvoiceDate, inv.DueDate,
            inv.SubTotal, inv.TaxPercent, inv.TaxAmount, inv.TotalAmount,
            inv.PaidAmount, inv.TotalAmount - inv.PaidAmount, inv.Notes,
            inv.Items.Select(it => new PurchaseItemDto(
                it.Id, it.Description, it.ProductId, it.Unit, it.Quantity, it.UnitCost, it.TotalCost
            )).ToList(),
            inv.Payments.Select(p => new SupplierPaymentSummaryDto(
                p.Id, p.PaymentNumber, inv.Supplier.Name, inv.InvoiceNumber,
                p.PaymentDate, p.Amount, p.PaymentMethod.ToString(), p.AccountName, p.Notes
            )).ToList()
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
            DueDate               = dto.PaymentTerms == PaymentTerms.Cash ? null : dto.DueDate,
            TaxPercent            = dto.TaxPercent,
            Notes                 = dto.Notes,
            Status                = dto.PaymentTerms == PaymentTerms.Cash
                ? PurchaseInvoiceStatus.Received : PurchaseInvoiceStatus.Draft,
            CreatedAt             = DateTime.UtcNow,
        };

        decimal subtotal = 0;
        foreach (var item in dto.Items)
        {
            var total = item.Quantity * item.UnitCost;
            invoice.Items.Add(new PurchaseInvoiceItem
            {
                Description = item.Description,
                ProductId   = item.ProductId,
                Unit        = item.Unit,
                Quantity    = item.Quantity,
                UnitCost    = item.UnitCost,
                TotalCost   = total,
                CreatedAt   = DateTime.UtcNow,
            });
            subtotal += total;
        }

        invoice.SubTotal    = subtotal;
        invoice.TaxAmount   = Math.Round(subtotal * (dto.TaxPercent / 100), 2);
        invoice.TotalAmount = subtotal + invoice.TaxAmount;

        // Update supplier totals
        supplier.TotalPurchases += invoice.TotalAmount;

        _db.PurchaseInvoices.Add(invoice);
        await _db.SaveChangesAsync();

        // ترحيل القيد المحاسبي
        var invoiceWithSupplier = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .FirstAsync(i => i.Id == invoice.Id);

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var accountingInner = scope.ServiceProvider.GetRequiredService<IAccountingService>();
            var dbInner = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try 
            {
                var invoiceInner = await dbInner.PurchaseInvoices
                    .Include(i => i.Supplier)
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

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdatePurchaseStatusDto dto)
    {
        var inv = await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted);
        if (inv == null) return NotFound();
        inv.Status    = dto.Status;
        inv.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        if (dto.Status == PurchaseInvoiceStatus.Returned)
        {
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
    public async Task<IActionResult> Delete(int id)
    {
        var inv = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted);
        if (inv == null) return NotFound();
        inv.Supplier.TotalPurchases -= inv.TotalAmount;
        inv.IsDeleted = true;
        inv.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record UpdatePurchaseStatusDto(PurchaseInvoiceStatus Status);

// ══════════════════════════════════════════════════════
// SUPPLIER PAYMENTS (VOUCHERS)
// ══════════════════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
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
                p.AccountName, p.Notes
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
            CreatedByUserId   = User.FindFirstValue(ClaimTypes.NameIdentifier),
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
