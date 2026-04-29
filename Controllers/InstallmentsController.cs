using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.AccountingMain)]
public class InstallmentsController : ControllerBase
{
    private readonly AppDbContext _db;

    public InstallmentsController(AppDbContext db) => _db = db;

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // GET /api/installments â€” Ù‚Ø§Ø¦Ù…Ø© Ø§Ù„Ø£Ù‚Ø³Ø§Ø· Ù…Ø¹ ÙÙ„ØªØ±
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int?    customerId = null,
        [FromQuery] string? status     = null,
        [FromQuery] bool?   overdue    = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        var q = _db.CustomerInstallments
            .AsNoTracking()
            .Include(i => i.Customer)
            .AsQueryable();

        if (customerId.HasValue)
            q = q.Where(i => i.CustomerId == customerId);

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<InstallmentStatus>(status, true, out var s))
            q = q.Where(i => i.Status == s);

        if (overdue == true)
            q = q.Where(i => i.DueDate < DateTime.Today && i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Cancelled);

        var total = await q.CountAsync();
        var items = await q
            .OrderBy(i => i.DueDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new {
                i.Id, i.CustomerId,
                CustomerName = i.Customer.FullName,
                CustomerPhone = i.Customer.Phone,
                i.OrderId, i.TotalAmount, i.PaidAmount, i.RemainingAmount,
                i.DueDate, i.Note, i.Status,
                IsOverdue = i.DueDate < DateTime.Today && i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Cancelled
            })
            .ToListAsync();

        return Ok(new { items, totalCount = total, page, pageSize });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // GET /api/installments/{id}
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await _db.CustomerInstallments
            .AsNoTracking()
            .Include(i => i.Customer)
            .Include(i => i.Order)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (item == null) return NotFound();
        return Ok(item);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // GET /api/installments/summary â€” Ù…Ù„Ø®Øµ Ø§Ù„Ù…Ø¯ÙŠÙˆÙ†ÙŠØ§Øª
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var today = DateTime.Today;
        var all = await _db.CustomerInstallments
            .AsNoTracking()
            .Where(i => i.Status != InstallmentStatus.Cancelled)
            .ToListAsync();

        return Ok(new {
            totalOutstanding = all.Where(i => i.Status != InstallmentStatus.Paid).Sum(i => i.RemainingAmount),
            overdueCount     = all.Count(i => i.DueDate < today && i.Status != InstallmentStatus.Paid),
            overdueAmount    = all.Where(i => i.DueDate < today && i.Status != InstallmentStatus.Paid).Sum(i => i.RemainingAmount),
            totalInstallments= all.Count,
        });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // POST /api/installments â€” Ø¥Ù†Ø´Ø§Ø¡ Ù‚Ø³Ø· Ø¬Ø¯ÙŠØ¯
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInstallmentDto dto)
    {
        if (!await _db.Customers.AnyAsync(c => c.Id == dto.CustomerId))
            return BadRequest(new { message = "Ø§Ù„Ø¹Ù…ÙŠÙ„ ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯" });

        var installment = new CustomerInstallment
        {
            CustomerId  = dto.CustomerId,
            OrderId     = dto.OrderId,
            TotalAmount = dto.TotalAmount,
            DueDate     = dto.DueDate,
            Note        = dto.Note,
            Status      = InstallmentStatus.Pending,
        };

        _db.CustomerInstallments.Add(installment);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = installment.Id }, installment);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // POST /api/installments/{id}/pay â€” ØªØ³Ø¬ÙŠÙ„ Ø¯ÙØ¹Ø©
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpPost("{id}/pay")]
    public async Task<IActionResult> RegisterPayment(int id, [FromBody] PayInstallmentDto dto)
    {
        var installment = await _db.CustomerInstallments
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (installment == null) return NotFound();
        if (installment.Status == InstallmentStatus.Paid)
            return BadRequest(new { message = "Ø§Ù„Ù‚Ø³Ø· Ù…Ø³Ø¯ÙŽÙ‘Ø¯ Ø¨Ø§Ù„ÙƒØ§Ù…Ù„ Ù…Ø³Ø¨Ù‚Ø§Ù‹" });
        if (installment.Status == InstallmentStatus.Cancelled)
            return BadRequest(new { message = "Ø§Ù„Ù‚Ø³Ø· Ù…Ù„ØºÙŠ" });

        if (dto.Amount <= 0 || dto.Amount > installment.RemainingAmount)
            return BadRequest(new { message = $"Ø§Ù„Ù…Ø¨Ù„Øº ÙŠØ¬Ø¨ Ø£Ù† ÙŠÙƒÙˆÙ† Ø¨ÙŠÙ† 1 Ùˆ {installment.RemainingAmount:N2}" });

        var collectedBy = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        _db.InstallmentPayments.Add(new InstallmentPayment
        {
            CustomerInstallmentId = id,
            Amount      = dto.Amount,
            PaymentDate = TimeHelper.GetEgyptTime(),
            Note        = dto.Note,
            CollectedBy = collectedBy,
        });

        installment.PaidAmount += dto.Amount;
        installment.UpdatedAt  = TimeHelper.GetEgyptTime();

        if (installment.PaidAmount >= installment.TotalAmount)
            installment.Status = InstallmentStatus.Paid;
        else if (installment.DueDate < DateTime.Today)
            installment.Status = InstallmentStatus.Overdue;
        else
            installment.Status = InstallmentStatus.Partial;

        await _db.SaveChangesAsync();
        return Ok(new {
            installment.Id, installment.PaidAmount, installment.RemainingAmount, installment.Status
        });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // PUT /api/installments/{id} â€” ØªØ¹Ø¯ÙŠÙ„ Ø§Ù„Ù‚Ø³Ø·
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpPut("{id}")]
    [RequirePermission(ModuleKeys.AccountingMain, requireEdit: true)]
    public async Task<IActionResult> Update(int id, [FromBody] CreateInstallmentDto dto)
    {
        var installment = await _db.CustomerInstallments.FindAsync(id);
        if (installment == null) return NotFound();

        installment.TotalAmount = dto.TotalAmount;
        installment.DueDate     = dto.DueDate;
        installment.Note        = dto.Note;
        installment.UpdatedAt   = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return Ok(installment);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // DELETE /api/installments/{id} â€” Ø­Ø°Ù Ù‚Ø³Ø·
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpDelete("{id}")]
    [RequirePermission(ModuleKeys.AccountingMain, requireEdit: true)]
    public async Task<IActionResult> Delete(int id)
    {
        var installment = await _db.CustomerInstallments
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (installment == null) return NotFound();

        _db.InstallmentPayments.RemoveRange(installment.Payments);
        _db.CustomerInstallments.Remove(installment);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // POST /api/installments/sync-overdue â€” ØªØ­Ø¯ÙŠØ« Ø­Ø§Ù„Ø© Ø§Ù„Ù…ØªØ£Ø®Ø±Ø©
    // ÙŠÙ…ÙƒÙ† Ø§Ø³ØªØ¯Ø¹Ø§Ø¤Ù‡Ø§ Ù…Ù† Hosted Service Ø£Ùˆ ÙŠØ¯ÙˆÙŠØ§Ù‹
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpPost("sync-overdue")]
    [RequirePermission(ModuleKeys.AccountingMain, requireEdit: true)]
    public async Task<IActionResult> SyncOverdue()
    {
        var today = DateTime.Today;
        var overdue = await _db.CustomerInstallments
            .Where(i => i.DueDate < today
                     && i.Status != InstallmentStatus.Paid
                     && i.Status != InstallmentStatus.Cancelled
                     && i.Status != InstallmentStatus.Overdue)
            .ToListAsync();

        var now = TimeHelper.GetEgyptTime();
        foreach (var i in overdue) { i.Status = InstallmentStatus.Overdue; i.UpdatedAt = now; }
        await _db.SaveChangesAsync();
        return Ok(new { updated = overdue.Count });
    }
}

public record CreateInstallmentDto(int CustomerId, int? OrderId, decimal TotalAmount, DateTime DueDate, string? Note);
public record PayInstallmentDto(decimal Amount, string? Note);

