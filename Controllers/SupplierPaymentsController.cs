using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Services;
using System.Security.Claims;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class SupplierPaymentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAccountingService _accounting;

    public SupplierPaymentsController(AppDbContext db, IAccountingService accounting)
    {
        _db = db;
        _accounting = accounting;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? supplierId = null,
        [FromQuery] int? purchaseInvoiceId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.SupplierPayments
            .Include(p => p.Supplier)
            .Include(p => p.Invoice)
            .AsQueryable();

        if (supplierId.HasValue) q = q.Where(p => p.SupplierId == supplierId.Value);
        if (purchaseInvoiceId.HasValue) q = q.Where(p => p.PurchaseInvoiceId == purchaseInvoiceId.Value);
        if (fromDate.HasValue) q = q.Where(p => p.PaymentDate >= fromDate.Value);
        if (toDate.HasValue) q = q.Where(p => p.PaymentDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(p => p.PaymentDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new SupplierPaymentSummaryDto(
                p.Id, p.PaymentNumber, p.Supplier.Name, 
                p.Invoice != null ? p.Invoice.InvoiceNumber : null,
                p.PaymentDate, p.Amount, p.PaymentMethod.ToString(), p.AccountName, p.Notes,
                p.AttachmentUrl, p.AttachmentPublicId
            )).ToListAsync();

        return Ok(new PaginatedResult<SupplierPaymentSummaryDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var p = await _db.SupplierPayments
            .Include(p => p.Supplier)
            .Include(p => p.Invoice)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (p == null) return NotFound();

        return Ok(new SupplierPaymentSummaryDto(
            p.Id, p.PaymentNumber, p.Supplier.Name, 
            p.Invoice != null ? p.Invoice.InvoiceNumber : null,
            p.PaymentDate, p.Amount, p.PaymentMethod.ToString(), p.AccountName, p.Notes,
            p.AttachmentUrl, p.AttachmentPublicId
        ));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSupplierPaymentDto dto)
    {
        if (dto.Amount <= 0) return BadRequest(new { message = "المبلغ يجب أن يكون أكبر من صفر" });

        // Debug: Log info to see ID
        // Note: dto.SupplierId might be 0 if parsing fails or ID is missing
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId);
        
        if (supplier == null) 
        {
            // First fallback, search by Name if ID is 0? (Not recommended but the user is blocked)
            // Actually, just returning the error with better message
            return BadRequest(new { message = $"المورد المطلوب غير موجود (ID: {dto.SupplierId})" });
        }

        PurchaseInvoice? invoice = null;
        if (dto.PurchaseInvoiceId.HasValue && dto.PurchaseInvoiceId > 0)
        {
            invoice = await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == dto.PurchaseInvoiceId.Value);
            if (invoice == null) return BadRequest(new { message = "الفاتورة المحددة غير موجودة" });
        }

        var year = DateTime.UtcNow.Year % 100;
        var count = await _db.SupplierPayments.CountAsync() + 1;
        var pNo = $"PV-{year}{count:D4}";

        var payment = new SupplierPayment
        {
            PaymentNumber = pNo,
            SupplierId = dto.SupplierId,
            PurchaseInvoiceId = (dto.PurchaseInvoiceId > 0) ? dto.PurchaseInvoiceId : null,
            Amount = dto.Amount,
            PaymentDate = dto.PaymentDate,
            PaymentMethod = dto.PaymentMethod,
            AccountName = dto.AccountName,
            Notes = dto.Notes ?? $"سند دفع للمورد {supplier.Name}",
            ReferenceNumber = dto.ReferenceNumber,
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            AttachmentUrl = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId,
            CreatedAt = DateTime.UtcNow,
        };

        _db.SupplierPayments.Add(payment);

        // Update Supplier/Invoice Balances
        supplier.TotalPaid += dto.Amount;
        if (invoice != null)
        {
            invoice.PaidAmount += dto.Amount;
            if (invoice.PaidAmount >= invoice.TotalAmount) invoice.Status = PurchaseInvoiceStatus.Paid;
            else if (invoice.PaidAmount > 0) invoice.Status = PurchaseInvoiceStatus.PartPaid;
        }

        await _db.SaveChangesAsync();

        // Accounting Trigger
        try
        {
            await _accounting.PostSupplierPaymentAsync(payment);
        }
        catch (Exception ex)
        {
            // Log accounting error but don't stop the payment
            Console.WriteLine($"[Accounting] Failed to post supplier payment {pNo}: {ex.Message}");
        }

        return CreatedAtAction(nameof(GetById), new { id = payment.Id }, payment);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await _db.SupplierPayments
            .Include(p => p.Supplier)
            .Include(p => p.Invoice)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (p == null) return NotFound();

        // Reverse balances
        p.Supplier.TotalPaid -= p.Amount;
        if (p.Invoice != null)
        {
            p.Invoice.PaidAmount -= p.Amount;
            if (p.Invoice.PaidAmount <= 0) p.Invoice.Status = PurchaseInvoiceStatus.Draft;
            else if (p.Invoice.PaidAmount < p.Invoice.TotalAmount) p.Invoice.Status = PurchaseInvoiceStatus.PartPaid;
        }

        // Search and delete linked Journal Entries
        var entry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Reference == p.PaymentNumber && e.Type == JournalEntryType.PaymentVoucher);
        if (entry != null)
        {
            _db.JournalEntries.Remove(entry);
        }

        _db.SupplierPayments.Remove(p);

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
