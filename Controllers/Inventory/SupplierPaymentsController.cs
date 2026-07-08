using System.Security.Claims;
using Sportive.API.Attributes;
using Sportive.API.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Services;
using System.Security.Claims;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.SupplierVouchers)]
public class SupplierPaymentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAccountingService _accounting;
    private readonly SequenceService _seq;
    private readonly ILogger<SupplierPaymentsController> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITranslator _t;
    private readonly IAuditService _audit;

    public SupplierPaymentsController(AppDbContext db, IAccountingService accounting, SequenceService seq, ILogger<SupplierPaymentsController> logger, IServiceScopeFactory scopeFactory, ITranslator t, IAuditService audit)
    {
        _db = db;
        _accounting = accounting;
        _seq = seq;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _t = t;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? supplierId = null,
        [FromQuery] int? purchaseInvoiceId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? search = null,
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

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLower();
            q = q.Where(p => 
                p.PaymentNumber.ToLower().Contains(searchLower) ||
                p.Supplier.Name.ToLower().Contains(searchLower) ||
                (p.Invoice != null && p.Invoice.InvoiceNumber.ToLower().Contains(searchLower)) ||
                (p.AccountName != null && p.AccountName.ToLower().Contains(searchLower)) ||
                (p.Notes != null && p.Notes.ToLower().Contains(searchLower)) ||
                (p.ReferenceNumber != null && p.ReferenceNumber.ToLower().Contains(searchLower))
            );
        }

        if (supplierId.HasValue)
        {
            var pQueryDto = q.Select(p => new SupplierPaymentSummaryDto(
                p.Id, p.PaymentNumber, p.Supplier.Name, 
                p.Invoice != null ? p.Invoice.InvoiceNumber : null,
                p.PaymentDate, p.Amount, p.PaymentMethod.ToString(), p.AccountName, p.Notes,
                p.AttachmentUrl, p.AttachmentPublicId,
                p.CostCenter,
                p.CostCenter == OrderSource.Website ? _t.Get("SupplierPayments.Website") : (p.CostCenter == OrderSource.POS ? _t.Get("SupplierPayments.POS") : _t.Get("SupplierPayments.General")),
                p.SupplierId,
                p.PurchaseInvoiceId,
                p.CashAccountId,
                p.ReferenceNumber
            ));

            var allP = await pQueryDto.ToListAsync();

            var jQuery = _db.JournalLines
                .Where(l => l.SupplierId == supplierId.Value && l.Debit > 0 && l.Supplier != null && l.AccountId == l.Supplier.MainAccountId && l.JournalEntry.Type == JournalEntryType.Manual)
                .Select(l => new SupplierPaymentSummaryDto(
                    -l.Id, 
                    l.JournalEntry.EntryNumber, 
                    l.Supplier!.Name, 
                    l.PurchaseInvoice != null ? l.PurchaseInvoice.InvoiceNumber : null,
                    l.JournalEntry.EntryDate, 
                    l.Debit, 
                    "Manual", 
                    l.Account.NameAr, 
                    l.Description ?? l.JournalEntry.Description,
                    l.JournalEntry.AttachmentUrl, 
                    l.JournalEntry.AttachmentPublicId,
                    l.JournalEntry.CostCenter,
                    l.JournalEntry.CostCenter == OrderSource.Website ? _t.Get("SupplierPayments.Website") : (l.JournalEntry.CostCenter == OrderSource.POS ? _t.Get("SupplierPayments.POS") : _t.Get("SupplierPayments.General")),
                    l.SupplierId,
                    l.PurchaseInvoiceId,
                    null,
                    l.JournalEntry.Reference
                ));

            var allJ = await jQuery.ToListAsync();

            var combined = allP.Concat(allJ).OrderByDescending(x => x.PaymentDate).ToList();
            var totalItems = combined.Count;
            var itemsList = combined.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Ok(new PaginatedResult<SupplierPaymentSummaryDto>(itemsList, totalItems, page, pageSize,
                (int)Math.Ceiling((double)totalItems / pageSize)));
        }

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(p => p.PaymentDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new SupplierPaymentSummaryDto(
                p.Id, p.PaymentNumber, p.Supplier.Name, 
                p.Invoice != null ? p.Invoice.InvoiceNumber : null,
                p.PaymentDate, p.Amount, p.PaymentMethod.ToString(), p.AccountName, p.Notes,
                p.AttachmentUrl, p.AttachmentPublicId,
                p.CostCenter,
                p.CostCenter == OrderSource.Website ? _t.Get("SupplierPayments.Website") : (p.CostCenter == OrderSource.POS ? _t.Get("SupplierPayments.POS") : _t.Get("SupplierPayments.General")),
                p.SupplierId,
                p.PurchaseInvoiceId,
                p.CashAccountId,
                p.ReferenceNumber
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
            p.AttachmentUrl, p.AttachmentPublicId,
            p.CostCenter,
            p.CostCenter == OrderSource.Website ? _t.Get("SupplierPayments.Website") : (p.CostCenter == OrderSource.POS ? _t.Get("SupplierPayments.POS") : _t.Get("SupplierPayments.General")),
            p.SupplierId,
            p.PurchaseInvoiceId,
            p.CashAccountId,
            p.ReferenceNumber
        ));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSupplierPaymentDto dto)
    {
        if (dto.Amount <= 0) return BadRequest(new { message = _t.Get("SupplierPayments.AmountGreaterThanZero") });

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId);
        if (supplier == null)
            return BadRequest(new { message = _t.Get("SupplierPayments.SupplierNotFound", dto.SupplierId.ToString()) });

        PurchaseInvoice? invoice = null;
        if (dto.PurchaseInvoiceId.HasValue && dto.PurchaseInvoiceId > 0)
        {
            invoice = await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == dto.PurchaseInvoiceId.Value);
            if (invoice == null) return BadRequest(new { message = _t.Get("SupplierPayments.InvoiceNotFound") });
        }

        var pNo = await _seq.NextAsync("SP");

        var payment = new SupplierPayment
        {
            PaymentNumber = pNo,
            SupplierId = dto.SupplierId,
            PurchaseInvoiceId = (dto.PurchaseInvoiceId > 0) ? dto.PurchaseInvoiceId : null,
            Amount = dto.Amount,
            PaymentDate = dto.PaymentDate,
            PaymentMethod = dto.PaymentMethod,
            AccountName = dto.AccountName,
            CashAccountId = (dto.CashAccountId > 0) ? dto.CashAccountId : null,
            Notes = dto.Notes ?? _t.Get("SupplierPayments.PaymentDescription", supplier.Name),
            ReferenceNumber = dto.ReferenceNumber,
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            AttachmentUrl = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId,
            CreatedAt = TimeHelper.GetEgyptTime(),
            CostCenter = dto.CostCenter
        };

        _db.SupplierPayments.Add(payment);

        if (invoice != null)
        {
            var remaining = invoice.TotalAmount - invoice.PaidAmount - invoice.ReturnedAmount;
            if (dto.Amount > remaining + 0.1m)
            {
                return BadRequest(new { message = _t.Get("SupplierPayments.AmountExceedsDebt", dto.Amount.ToString(), remaining.ToString()) });
            }
            invoice.PaidAmount += dto.Amount;
            var netTotal = invoice.TotalAmount - invoice.ReturnedAmount;
            invoice.Status = invoice.PaidAmount >= netTotal - 0.1m ? PurchaseInvoiceStatus.Paid : PurchaseInvoiceStatus.PartPaid;
        }

        // Update Supplier Balance
        supplier.TotalPaid += dto.Amount;

        await _db.SaveChangesAsync();

        // ðŸ’¡ Ensure accounting sync gets the correct ID
        _ = PostSupplierPaymentWithRetryAsync(payment.Id, pNo);
        
        try { await _audit.LogAsync("CreateSupplierPayment", "SupplierPayment", payment.Id.ToString(), $"Created supplier payment {payment.PaymentNumber} for {payment.Amount}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

        return CreatedAtAction(nameof(GetById), new { id = payment.Id }, new SupplierPaymentSummaryDto(
            payment.Id, payment.PaymentNumber, supplier.Name,
            invoice?.InvoiceNumber,
            payment.PaymentDate, payment.Amount, payment.PaymentMethod.ToString(),
            payment.AccountName, payment.Notes,
            payment.AttachmentUrl, payment.AttachmentPublicId,
            payment.CostCenter,
            payment.CostCenter == OrderSource.Website ? _t.Get("SupplierPayments.Website") : (payment.CostCenter == OrderSource.POS ? _t.Get("SupplierPayments.POS") : _t.Get("SupplierPayments.General")),
            payment.SupplierId,
            payment.PurchaseInvoiceId,
            payment.CashAccountId,
            payment.ReferenceNumber
        ));
    }

    [HttpPut("{id}")]
    [RequirePermission(ModuleKeys.SupplierVouchers, requireEdit: true)]
    public async Task<IActionResult> Update(int id, [FromBody] CreateSupplierPaymentDto dto)
    {
        if (dto.Amount <= 0) return BadRequest(new { message = _t.Get("SupplierPayments.AmountGreaterThanZero") });

        var payment = await _db.SupplierPayments
            .Include(p => p.Supplier)
            .Include(p => p.Invoice)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (payment == null) return NotFound();

        decimal oldAmount = payment.Amount;

        // 1. Reverse old supplier total paid
        payment.Supplier.TotalPaid -= oldAmount;

        // 2. Reverse old invoice paid amount
        if (payment.Invoice != null)
        {
            payment.Invoice.PaidAmount -= oldAmount;
            var oldNetTotal = payment.Invoice.TotalAmount - payment.Invoice.ReturnedAmount;
            if (payment.Invoice.PaidAmount <= 0) 
                payment.Invoice.Status = PurchaseInvoiceStatus.Received;
            else 
                payment.Invoice.Status = payment.Invoice.PaidAmount >= oldNetTotal - 0.1m ? PurchaseInvoiceStatus.Paid : PurchaseInvoiceStatus.PartPaid;
        }

        // 3. Load new Supplier if changed
        var newSupplier = payment.Supplier;
        if (payment.SupplierId != dto.SupplierId)
        {
            newSupplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId);
            if (newSupplier == null)
                return BadRequest(new { message = _t.Get("SupplierPayments.SupplierNotFound", dto.SupplierId.ToString()) });
        }

        // 4. Load new Invoice if changed or added
        PurchaseInvoice? newInvoice = null;
        if (dto.PurchaseInvoiceId.HasValue && dto.PurchaseInvoiceId > 0)
        {
            if (payment.PurchaseInvoiceId == dto.PurchaseInvoiceId.Value)
            {
                newInvoice = payment.Invoice;
            }
            else
            {
                newInvoice = await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == dto.PurchaseInvoiceId.Value);
                if (newInvoice == null) return BadRequest(new { message = _t.Get("SupplierPayments.InvoiceNotFound") });
            }
        }

        // 5. Validate new Amount against remaining invoice debt
        if (newInvoice != null)
        {
            var remaining = newInvoice.TotalAmount - newInvoice.PaidAmount - newInvoice.ReturnedAmount;
            if (dto.Amount > remaining + 0.1m)
            {
                return BadRequest(new { message = _t.Get("SupplierPayments.AmountExceedsDebt", dto.Amount.ToString(), remaining.ToString()) });
            }
            newInvoice.PaidAmount += dto.Amount;
            var netTotal = newInvoice.TotalAmount - newInvoice.ReturnedAmount;
            newInvoice.Status = newInvoice.PaidAmount >= netTotal - 0.1m ? PurchaseInvoiceStatus.Paid : PurchaseInvoiceStatus.PartPaid;
        }

        // 6. Update Supplier Balance
        newSupplier.TotalPaid += dto.Amount;

        // 7. Update payment properties
        payment.SupplierId = dto.SupplierId;
        payment.Supplier = newSupplier;
        payment.PurchaseInvoiceId = (dto.PurchaseInvoiceId > 0) ? dto.PurchaseInvoiceId : null;
        payment.Invoice = newInvoice;
        payment.Amount = dto.Amount;
        payment.PaymentDate = dto.PaymentDate;
        payment.PaymentMethod = dto.PaymentMethod;
        payment.AccountName = dto.AccountName;
        payment.CashAccountId = (dto.CashAccountId > 0) ? dto.CashAccountId : null;
        payment.Notes = dto.Notes ?? _t.Get("SupplierPayments.PaymentDescription", newSupplier.Name);
        payment.ReferenceNumber = dto.ReferenceNumber;
        payment.AttachmentUrl = dto.AttachmentUrl;
        payment.AttachmentPublicId = dto.AttachmentPublicId;
        payment.CostCenter = dto.CostCenter;

        // 8. Delete old Journal Entry
        var entry = await _db.JournalEntries.FirstOrDefaultAsync(e => 
            e.Type == JournalEntryType.PaymentVoucher && 
            e.Reference != null &&
            e.Reference.Trim().ToLower() == payment.PaymentNumber.Trim().ToLower());
        
        if (entry != null)
        {
            _db.JournalEntries.Remove(entry);
        }

        await _db.SaveChangesAsync();

        // 9. Post updated Journal Entry
        _ = PostSupplierPaymentWithRetryAsync(payment.Id, payment.PaymentNumber);
        
        try { await _audit.LogAsync("UpdateSupplierPayment", "SupplierPayment", id.ToString(), $"Updated supplier payment {payment.PaymentNumber}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

        return Ok(new SupplierPaymentSummaryDto(
            payment.Id, payment.PaymentNumber, newSupplier.Name,
            newInvoice?.InvoiceNumber,
            payment.PaymentDate, payment.Amount, payment.PaymentMethod.ToString(),
            payment.AccountName, payment.Notes,
            payment.AttachmentUrl, payment.AttachmentPublicId,
            payment.CostCenter,
            payment.CostCenter == OrderSource.Website ? _t.Get("SupplierPayments.Website") : (payment.CostCenter == OrderSource.POS ? _t.Get("SupplierPayments.POS") : _t.Get("SupplierPayments.General")),
            payment.SupplierId,
            payment.PurchaseInvoiceId,
            payment.CashAccountId,
            payment.ReferenceNumber
        ));
    }

    [HttpDelete("{id}")]
    [RequirePermission(ModuleKeys.SupplierVouchers, requireEdit: true)]
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
            if (p.Invoice.PaidAmount <= 0) p.Invoice.Status = PurchaseInvoiceStatus.Received;
            else if (p.Invoice.PaidAmount < p.Invoice.TotalAmount) p.Invoice.Status = PurchaseInvoiceStatus.PartPaid;
        }

        // Search and delete linked Journal Entries
        var entry = await _db.JournalEntries.FirstOrDefaultAsync(e => 
            e.Type == JournalEntryType.PaymentVoucher && 
            e.Reference != null &&
            e.Reference.Trim().ToLower() == p.PaymentNumber.Trim().ToLower());
        
        if (entry != null)
        {
            _db.JournalEntries.Remove(entry);
        }

        _db.SupplierPayments.Remove(p);

        await _db.SaveChangesAsync();
        
        var pNo = p.PaymentNumber;
        try { await _audit.LogAsync("DeleteSupplierPayment", "SupplierPayment", id.ToString(), $"Deleted supplier payment {pNo}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

        return NoContent();
    }

    private async Task PostSupplierPaymentWithRetryAsync(int paymentId, string paymentNumber)
    {
        var tenantContextCurrent = HttpContext.RequestServices.GetRequiredService<Sportive.API.Interfaces.ITenantContext>();
        var tenant = tenantContextCurrent.CurrentTenant;
        
        _ = Task.Run(async () =>
        {
            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    if (tenant != null) scope.ServiceProvider.GetRequiredService<Sportive.API.Interfaces.ITenantContext>().SetTenant(tenant);
                    
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
        });
    }

    [HttpPatch("{id}/link-invoice")]
    [RequirePermission(ModuleKeys.SupplierVouchers, requireEdit: true)]
    public async Task<IActionResult> LinkInvoice(int id, [FromBody] LinkInvoiceDto dto)
    {
        if (id < 0)
        {
            var lineId = -id;
            var line = await _db.JournalLines.Include(l => l.JournalEntry).FirstOrDefaultAsync(l => l.Id == lineId);
            if (line == null) return NotFound();
            if (line.PurchaseInvoiceId.HasValue) return BadRequest(new { message = "هذا القيد مرتبط بالفعل بفاتورة." });

            var inv = await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == dto.PurchaseInvoiceId && i.SupplierId == line.SupplierId);
            if (inv == null) return BadRequest(new { message = "الفاتورة غير موجودة أو لا تخص هذا المورد." });

            var rem = inv.TotalAmount - inv.PaidAmount - inv.ReturnedAmount;
            if (rem <= 0) return BadRequest(new { message = "هذه الفاتورة مسددة بالكامل." });

            if (line.Debit > rem) return BadRequest(new { message = "قيمة القيد اليدوي أكبر من المتبقي للفاتورة. لا يمكن تجزئة القيد آلياً." });

            line.PurchaseInvoiceId = inv.Id;
            inv.PaidAmount += line.Debit;
            
            await _db.SaveChangesAsync();
            await _accounting.SyncEntityBalancesAsync();
            return Ok(new { message = "تم ربط القيد اليدوي بالفاتورة بنجاح." });
        }

        var payment = await _db.SupplierPayments
            .Include(p => p.Supplier)
            .Include(p => p.Invoice)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (payment == null) return NotFound();
        if (payment.PurchaseInvoiceId.HasValue) return BadRequest(new { message = "هذا السند مرتبط بالفعل بفاتورة." });

        var invoice = await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == dto.PurchaseInvoiceId && i.SupplierId == payment.SupplierId);
        if (invoice == null) return BadRequest(new { message = "الفاتورة غير موجودة أو لا تخص هذا المورد." });

        var remaining = invoice.TotalAmount - invoice.PaidAmount - invoice.ReturnedAmount;
        if (remaining <= 0) return BadRequest(new { message = "هذه الفاتورة مسددة بالكامل." });

        var newlySplitAdvancePayments = new List<SupplierPayment>();

        if (payment.Amount > remaining)
        {
            // Split the payment
            decimal remainder = payment.Amount - remaining;

            // Delete original journal entry for the payment to replace it with split ones
            var oldJE = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Reference == payment.PaymentNumber && e.Type == JournalEntryType.PaymentVoucher);
            if (oldJE != null) _db.JournalEntries.Remove(oldJE);

            // The current payment takes the remaining amount of the invoice and gets linked
            payment.Amount = remaining;
            payment.PurchaseInvoiceId = invoice.Id;
            payment.Invoice = invoice;

            // Create new payment for the rest (keeps the advance nature)
            var newPNo = await _seq.NextAsync("SP");
            var splitPayment = new SupplierPayment
            {
                PaymentNumber = newPNo,
                SupplierId = payment.SupplierId,
                Amount = remainder,
                PaymentDate = payment.PaymentDate,
                PaymentMethod = payment.PaymentMethod,
                CashAccountId = payment.CashAccountId,
                AccountName = payment.AccountName,
                Notes = payment.Notes + " (رصيد متبقي بعد الربط)",
                CreatedAt = payment.CreatedAt,
                CostCenter = payment.CostCenter,
                AttachmentUrl = payment.AttachmentUrl,
                AttachmentPublicId = payment.AttachmentPublicId,
                CreatedByUserId = payment.CreatedByUserId
            };
            _db.SupplierPayments.Add(splitPayment);
            newlySplitAdvancePayments.Add(splitPayment);

            invoice.PaidAmount += remaining;
        }
        else
        {
            invoice.PaidAmount += payment.Amount;
            payment.PurchaseInvoiceId = invoice.Id;
            payment.Invoice = invoice;
        }

        var netTotal = invoice.TotalAmount - invoice.ReturnedAmount;
        invoice.Status = invoice.PaidAmount >= netTotal - 0.01m ? PurchaseInvoiceStatus.Paid : PurchaseInvoiceStatus.PartPaid;

        await _db.SaveChangesAsync();

        // Sync journal entries for the modified payment
        _ = PostSupplierPaymentWithRetryAsync(payment.Id, payment.PaymentNumber);
        
        // Sync journal entries for the newly split payments
        if (newlySplitAdvancePayments.Any())
        {
            var newlySplitIds = newlySplitAdvancePayments.Select(p => p.Id).ToList();
            _ = Task.Run(async () => {
                try {
                    using var scope = _scopeFactory.CreateScope();
                    var acc = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                    var innerDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var payments = await innerDb.SupplierPayments.Where(p => newlySplitIds.Contains(p.Id)).ToListAsync();
                    foreach (var sp in payments) { await acc.PostSupplierPaymentAsync(sp); }
                } catch (Exception ex) {
                    _logger.LogError(ex, "Failed to post journal for newly split linked payments.");
                }
            });
        }

        try { await _audit.LogAsync("LinkSupplierPayment", "SupplierPayment", id.ToString(), $"Linked payment {payment.PaymentNumber} to invoice {invoice.InvoiceNumber}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

        return Ok(new { message = "تم الربط بنجاح" });
    }
}

public record LinkInvoiceDto(int PurchaseInvoiceId);

