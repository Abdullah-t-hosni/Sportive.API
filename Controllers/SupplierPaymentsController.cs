using Sportive.API.Utils;
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
    private readonly SequenceService _seq;
    private readonly ILogger<SupplierPaymentsController> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public SupplierPaymentsController(AppDbContext db, IAccountingService accounting, SequenceService seq, ILogger<SupplierPaymentsController> logger, IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _accounting = accounting;
        _seq = seq;
        _logger = logger;
        _scopeFactory = scopeFactory;
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
            .AsNoTracking()
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

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId);
        if (supplier == null)
            return BadRequest(new { message = $"المورد المطلوب غير موجود (ID: {dto.SupplierId})" });

        PurchaseInvoice? invoice = null;
        if (dto.PurchaseInvoiceId.HasValue && dto.PurchaseInvoiceId > 0)
        {
            invoice = await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == dto.PurchaseInvoiceId.Value);
            if (invoice == null) return BadRequest(new { message = "الفاتورة المحددة غير موجودة" });
        }

        var pNo = await _seq.NextAsync("SP", async (db, pattern) =>
        {
            var max = await db.SupplierPayments
                .Where(p => EF.Functions.Like(p.PaymentNumber, pattern))
                .Select(p => p.PaymentNumber)
                .ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                      .DefaultIfEmpty(0).Max();
        });

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
            CreatedAt = TimeHelper.GetEgyptTime(),
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

        _ = PostSupplierPaymentWithRetryAsync(payment.Id, pNo);

        return CreatedAtAction(nameof(GetById), new { id = payment.Id }, new SupplierPaymentSummaryDto(
            payment.Id, payment.PaymentNumber, supplier.Name,
            invoice?.InvoiceNumber,
            payment.PaymentDate, payment.Amount, payment.PaymentMethod.ToString(),
            payment.AccountName, payment.Notes,
            payment.AttachmentUrl, payment.AttachmentPublicId
        ));
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

    private async Task PostSupplierPaymentWithRetryAsync(int paymentId, string paymentNumber)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var payment = await db.SupplierPayments.FirstAsync(p => p.Id == paymentId);
                await accounting.PostSupplierPaymentAsync(payment);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex,
                    "[Accounting] Supplier payment journal attempt {Attempt}/{Max} failed for {Number}. Retrying...",
                    attempt, maxAttempts, paymentNumber);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Accounting] Supplier payment journal permanently failed for {Number} after {Max} attempts.",
                    paymentNumber, maxAttempts);
            }
        }
    }
}
