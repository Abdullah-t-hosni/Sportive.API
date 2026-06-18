using System.Security.Claims;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using Sportive.API.Interfaces;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.Installments)]
public class InstallmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITranslator _t;
    private readonly IAccountingService _accounting;
    private readonly SequenceService _seq;
    private readonly IAuditService _audit;

    public InstallmentsController(AppDbContext db, ITranslator t, IAccountingService accounting, SequenceService seq, IAuditService audit)
    {
        _db = db;
        _t = t;
        _accounting = accounting;
        _seq = seq;
        _audit = audit;
    }

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

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInstallmentDto dto)
    {
        if (!await _db.Customers.AnyAsync(c => c.Id == dto.CustomerId))
            return BadRequest(new { message = _t.Get("Customers.NotFound") });

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
        try { await _audit.LogAsync("CreateInstallment", "CustomerInstallment", installment.Id.ToString(), $"Created installment of {dto.TotalAmount} for customer {dto.CustomerId}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return CreatedAtAction(nameof(GetById), new { id = installment.Id }, installment);
    }

    [HttpPost("{id}/pay")]
    public async Task<IActionResult> RegisterPayment(int id, [FromBody] PayInstallmentDto dto)
    {
        var installment = await _db.CustomerInstallments
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (installment == null) return NotFound();
        if (installment.Status == InstallmentStatus.Paid)
            return BadRequest(new { message = _t.Get("Installments.AlreadyPaid") });
        if (installment.Status == InstallmentStatus.Cancelled)
            return BadRequest(new { message = _t.Get("Installments.Cancelled") });

        if (dto.Amount <= 0 || dto.Amount > installment.RemainingAmount)
            return BadRequest(new { message = _t.Get("Installments.InvalidAmount", installment.RemainingAmount.ToString("N2")) });

        var cashAccount = await _db.Accounts.FindAsync(dto.CashAccountId);
        if (cashAccount == null) return BadRequest(new { message = _t.Get("Accounting.ReceiptVoucher.AccountNotFound") });
        if (!cashAccount.CanReceivePayment && (!User.IsInRole("SuperAdmin") && !User.IsInRole("Admin")))
            return BadRequest(new { message = _t.Get("Accounting.ReceiptVoucher.AccountCannotReceivePayment", cashAccount.NameAr) });

        var customerAccountId = await _db.AccountSystemMappings
            .Where(m => m.Key == MappingKeys.Customer.ToLower())
            .Select(m => (int?)m.AccountId)
            .FirstOrDefaultAsync();

        if (customerAccountId == null) return BadRequest(new { message = "حساب العملاء العام غير مربوط في إعدادات المحاسبة" });

        var collectedBy = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        
        var vNo = await _seq.NextAsync("RV");
        var voucher = new ReceiptVoucher {
            VoucherNumber = vNo,
            VoucherDate = TimeHelper.GetEgyptTime(),
            Amount = dto.Amount,
            CashAccountId = dto.CashAccountId,
            FromAccountId = customerAccountId.Value,
            CustomerId = installment.CustomerId,
            PaymentMethod = VoucherPaymentMethod.Cash,
            Reference = $"Installment-{id}",
            Description = dto.Note ?? $"تحصيل قسط للعميل",
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            CreatedAt = TimeHelper.GetEgyptTime(),
            OrderId = installment.OrderId
        };

        _db.ReceiptVouchers.Add(voucher);

        var payment = new InstallmentPayment
        {
            CustomerInstallmentId = id,
            Amount      = dto.Amount,
            PaymentDate = TimeHelper.GetEgyptTime(),
            Note        = dto.Note,
            CollectedBy = collectedBy,
            ReceiptVoucher = voucher
        };
        _db.InstallmentPayments.Add(payment);

        installment.PaidAmount += dto.Amount;
        installment.UpdatedAt  = TimeHelper.GetEgyptTime();

        if (installment.PaidAmount >= installment.TotalAmount)
            installment.Status = InstallmentStatus.Paid;
        else if (installment.DueDate < DateTime.Today)
            installment.Status = InstallmentStatus.Overdue;
        else
            installment.Status = InstallmentStatus.Partial;

        await _db.SaveChangesAsync();
        await _accounting.PostReceiptVoucherAsync(voucher, installment.OrderId);
        Hangfire.BackgroundJob.Enqueue<IAccountingService>(a => a.SyncEntityBalancesAsync());
        
        try { await _audit.LogAsync("RegisterInstallmentPayment", "CustomerInstallment", id.ToString(), $"Registered payment of {dto.Amount} for installment {id}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

        return Ok(new {
            installment.Id, installment.PaidAmount, installment.RemainingAmount, installment.Status
        });
    }

    [HttpPut("{id}")]
    [RequirePermission(ModuleKeys.Installments, requireEdit: true)]
    public async Task<IActionResult> Update(int id, [FromBody] CreateInstallmentDto dto)
    {
        var installment = await _db.CustomerInstallments.FindAsync(id);
        if (installment == null) return NotFound();

        installment.TotalAmount = dto.TotalAmount;
        installment.DueDate     = dto.DueDate;
        installment.Note        = dto.Note;
        installment.UpdatedAt   = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        try { await _audit.LogAsync("UpdateInstallment", "CustomerInstallment", id.ToString(), $"Updated installment {id}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return Ok(installment);
    }

    [HttpDelete("{id}")]
    [RequirePermission(ModuleKeys.Installments, requireEdit: true)]
    public async Task<IActionResult> Delete(int id)
    {
        var installment = await _db.CustomerInstallments
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (installment == null) return NotFound();

        _db.InstallmentPayments.RemoveRange(installment.Payments);
        _db.CustomerInstallments.Remove(installment);
        await _db.SaveChangesAsync();
        try { await _audit.LogAsync("DeleteInstallment", "CustomerInstallment", id.ToString(), $"Deleted installment {id}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return NoContent();
    }

    [HttpPost("sync-ledger")]
    [RequirePermission(ModuleKeys.Installments, requireEdit: true)]
    public async Task<IActionResult> SyncLedgerBalances()
    {
        var customerAccountId = await _db.AccountSystemMappings
            .Where(m => m.Key == MappingKeys.Customer.ToLower())
            .Select(m => (int?)m.AccountId)
            .FirstOrDefaultAsync();

        if (customerAccountId == null) return BadRequest(new { message = "حساب العملاء غير مربوط" });

        var ledgerBalances = await _db.JournalLines
            .Where(l => l.AccountId == customerAccountId.Value && l.CustomerId != null)
            .GroupBy(l => l.CustomerId!.Value)
            .Select(g => new { CustomerId = g.Key, Balance = g.Sum(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0 })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Balance);

        var pendingInstallmentsList = await _db.CustomerInstallments
            .Where(i => i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Cancelled)
            .OrderBy(i => i.DueDate)
            .ToListAsync();
            
        var pendingInstallmentsDict = pendingInstallmentsList
            .GroupBy(i => i.CustomerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var customers = await _db.Customers.Select(c => c.Id).ToListAsync();

        int updatedCount = 0;
        int createdCount = 0;
        var egyptTime = TimeHelper.GetEgyptTime();

        foreach (var cId in customers)
        {
            decimal ledgerBalance = ledgerBalances.ContainsKey(cId) ? ledgerBalances[cId] : 0;
            var pendingInsts = pendingInstallmentsDict.ContainsKey(cId) ? pendingInstallmentsDict[cId] : new List<CustomerInstallment>();
            
            var installmentsBalance = pendingInsts.Sum(i => i.RemainingAmount);

            if (installmentsBalance > ledgerBalance)
            {
                var amountToPayOff = installmentsBalance - ledgerBalance;
                foreach (var inst in pendingInsts)
                {
                    if (amountToPayOff <= 0) break;

                    var payAmount = Math.Min(amountToPayOff, inst.RemainingAmount);
                    amountToPayOff -= payAmount;

                    var payment = new InstallmentPayment
                    {
                        CustomerInstallmentId = inst.Id,
                        Amount = payAmount,
                        PaymentDate = egyptTime,
                        Note = "تسوية آلية مع الحسابات العامة (رصيد دفتر الأستاذ)",
                        CollectedBy = "System Sync"
                    };
                    _db.InstallmentPayments.Add(payment);

                    inst.PaidAmount += payAmount;
                    inst.UpdatedAt = egyptTime;

                    if (inst.PaidAmount >= inst.TotalAmount)
                        inst.Status = InstallmentStatus.Paid;
                    else if (inst.DueDate < DateTime.Today)
                        inst.Status = InstallmentStatus.Overdue;
                    else
                        inst.Status = InstallmentStatus.Partial;
                    
                    updatedCount++;
                }
            }
            else if (ledgerBalance > installmentsBalance)
            {
                var missingDebt = ledgerBalance - installmentsBalance;
                var inst = new CustomerInstallment
                {
                    CustomerId = cId,
                    TotalAmount = missingDebt,
                    PaidAmount = 0,
                    DueDate = egyptTime.Date,
                    Note = "مديونية عامة مستوردة من رصيد الحسابات (تسوية)",
                    Status = InstallmentStatus.Overdue,
                    CreatedAt = egyptTime,
                    UpdatedAt = egyptTime
                };
                _db.CustomerInstallments.Add(inst);
                createdCount++;
            }
        }

        await _db.SaveChangesAsync();
        try { await _audit.LogAsync("SyncInstallmentsLedger", "CustomerInstallment", "", $"Synced ledger. Updated {updatedCount}, Created {createdCount}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return Ok(new { message = $"تمت المزامنة بنجاح. تسوية أقساط: {updatedCount} | إنشاء أقساط جديدة: {createdCount}" });
    }

    [HttpPost("sync-overdue")]
    [RequirePermission(ModuleKeys.Installments, requireEdit: true)]
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
        try { await _audit.LogAsync("SyncOverdueInstallments", "CustomerInstallment", "", $"Marked {overdue.Count} installments as overdue", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return Ok(new { updated = overdue.Count });
    }
}

public record CreateInstallmentDto(int CustomerId, int? OrderId, decimal TotalAmount, DateTime DueDate, string? Note);
public record PayInstallmentDto(decimal Amount, int CashAccountId, string? Note);

