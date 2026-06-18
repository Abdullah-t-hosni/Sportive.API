using System.Security.Claims;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;
using Sportive.API.Extensions;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/employee-advances")]
[RequirePermission(ModuleKeys.HrAdvances)]
public class EmployeeAdvancesController : ControllerBase
{
    private readonly AppDbContext    _db;
    private readonly SequenceService _seq;
    private readonly IAccountingService _accounting;
    private readonly AccountingCoreService _core;
    private readonly ITranslator _t;
    private readonly IAuditService _audit;

    public EmployeeAdvancesController(AppDbContext db, SequenceService seq, IAccountingService accounting, AccountingCoreService core, ITranslator t, IAuditService audit)
        => (_db, _seq, _accounting, _core, _t, _audit) = (db, seq, accounting, core, t, audit);
    private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? employeeId = null,
        [FromQuery] AdvanceStatus? status = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        // ⚡ DATA INTEGRITY FIX: Auto-correct advance status based on DeductedAmount
        var mismatchedAdvances = await _db.EmployeeAdvances
            .Where(a => (a.DeductedAmount >= a.Amount && a.Status != AdvanceStatus.FullyDeducted) ||
                        (a.DeductedAmount > 0 && a.DeductedAmount < a.Amount && a.Status != AdvanceStatus.PartiallyDeducted) ||
                        (a.DeductedAmount == 0 && a.Status != AdvanceStatus.Pending))
            .ToListAsync();

        if (mismatchedAdvances.Any())
        {
            foreach (var adv in mismatchedAdvances)
            {
                if (adv.DeductedAmount >= adv.Amount)
                    adv.Status = AdvanceStatus.FullyDeducted;
                else if (adv.DeductedAmount > 0)
                    adv.Status = AdvanceStatus.PartiallyDeducted;
                else
                    adv.Status = AdvanceStatus.Pending;
                
                adv.UpdatedAt = TimeHelper.GetEgyptTime();
            }
            await _db.SaveChangesAsync();
        }

        var q = _db.EmployeeAdvances
            .Include(a => a.Employee)
            .Include(a => a.CashAccount)
            .AsQueryable();

        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        if (!canViewAll)
        {
            int? isolatedBranchId = User.GetBranchId();
            if (isolatedBranchId.HasValue)
            {
                q = q.Where(a => a.Employee.BranchId == isolatedBranchId.Value);
            }
        }

        if (employeeId.HasValue) q = q.Where(a => a.EmployeeId == employeeId.Value);
        if (status.HasValue)     q = q.Where(a => a.Status == status.Value);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(a => a.AdvanceDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new EmployeeAdvanceDto(
                a.Id, a.AdvanceNumber, a.EmployeeId, a.Employee.Name,
                a.AdvanceDate, a.Amount, a.DeductedAmount, a.Amount - a.DeductedAmount,
                a.Status, a.Reason, a.Notes,
                a.CashAccountId, a.CashAccount != null ? a.CashAccount.NameAr : null,
                a.JournalEntryId, a.CreatedAt, a.CostCenter
            )).ToListAsync();

        return Ok(new PaginatedResult<EmployeeAdvanceDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpPost]
    [RequirePermission(ModuleKeys.HrAdvances, requireEdit: true)]
    public async Task<IActionResult> Create([FromBody] CreateAdvanceDto dto)
    {
        var emp = await _db.Employees.FindAsync(dto.EmployeeId);
        if (emp == null) return NotFound();

        var mapDict = await _core.GetSafeSystemMappingsAsync();

        //  UNIFIED VOUCHER SYSTEM: Create a PaymentVoucher record for this advance (if cash disbursement)
        if (dto.CashAccountId.HasValue && dto.CashAccountId > 0)
        {
            if (!mapDict.TryGetValue(MappingKeys.EmployeeAdvances, out var advAccId) || advAccId == null)
                return BadRequest(new { message = _t.Get("HR.SalaryAccountNotSet") });
        }

        // Retry logic for sequence generation to handle race conditions
        for (int retry = 0; retry < 3; retry++)
        {
            try
            {
                var advNo = await _seq.NextAsync("ADV");

                var advance = new EmployeeAdvance
                {
                    AdvanceNumber = advNo,
                    EmployeeId = dto.EmployeeId,
                    AdvanceDate = dto.AdvanceDate,
                    Amount = dto.Amount,
                    Reason = dto.Reason?.Trim(),
                    Notes = dto.Notes?.Trim(),
                    CashAccountId = dto.CashAccountId,
                    CostCenter = dto.CostCenter ?? emp.CostCenter,
                    Status = AdvanceStatus.Pending,
                    CreatedAt = TimeHelper.GetEgyptTime(),
                    CreatedByUserId = UserId
                };

                _db.EmployeeAdvances.Add(advance);

                if (dto.CashAccountId.HasValue && dto.CashAccountId > 0)
                {
                    // Get account again from dict (safe because we checked above)
                    mapDict.TryGetValue(MappingKeys.EmployeeAdvances, out var advAccId);

                    var voucher = new PaymentVoucher
                    {
                        VoucherNumber = advance.AdvanceNumber,
                        VoucherDate = advance.AdvanceDate,
                        Amount = advance.Amount,
                        CashAccountId = advance.CashAccountId.GetValueOrDefault(),
                        ToAccountId = advAccId.GetValueOrDefault(),
                        EmployeeId = emp.Id,
                        PaymentMethod = VoucherPaymentMethod.Cash,
                        Description = $"سلفة موظف — {emp.Name}",
                        Reference = advance.AdvanceNumber,
                        CreatedAt = TimeHelper.GetEgyptTime(),
                        CreatedByUserId = UserId,
                        CostCenter = advance.CostCenter
                    };
                    _db.PaymentVouchers.Add(voucher);
                    await _db.SaveChangesAsync();

                    await _accounting.PostPaymentVoucherAsync(voucher);
                    advance.JournalEntryId = voucher.JournalEntryId;
                    await _db.SaveChangesAsync();
                }
                else
                {
                    await _db.SaveChangesAsync();
                }
                
                try { await _audit.LogAsync("CreateEmployeeAdvance", "EmployeeAdvance", advance.Id.ToString(), $"Created advance {advance.AdvanceNumber} for employee {emp.Name}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

                return Ok(new { id = advance.Id, advanceNumber = advance.AdvanceNumber, journalEntryId = advance.JournalEntryId });
            }
            catch (DbUpdateException ex) when (retry < 2 && (ex.InnerException?.Message.Contains("duplicate") == true || ex.Message.Contains("duplicate") == true))
            {
                // Number conflict, will retry with fresh MAX
                _db.ChangeTracker.Clear();
            }
        }

        return StatusCode(409, new { message = _t.Get("HR.AdvanceDuplicateNumber") });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var adv = await _db.EmployeeAdvances.FindAsync(id);
        if (adv == null) return NotFound();
        if (adv.Status != AdvanceStatus.Pending)
            return BadRequest(new { message = _t.Get("HR.AdvanceCannotDelete") });

        // Delete associated voucher and journal if unposted/partial? 
        // Actually PaymentVoucher.Reference = AdvanceNumber
        var voucher = await _db.PaymentVouchers.FirstOrDefaultAsync(v => v.Reference == adv.AdvanceNumber);
        if (voucher != null)
        {
             // If there's a journal entry, we should ideally reverse it or delete it if unposted
             if (voucher.JournalEntryId.HasValue)
             {
                 var je = await _db.JournalEntries.Include(j => j.Lines).FirstOrDefaultAsync(j => j.Id == voucher.JournalEntryId);
                 if (je != null)
                 {
                     var childReversals = await _db.JournalEntries
                         .Include(j => j.Lines)
                         .Where(j => j.ReversalOfId == je.Id)
                         .ToListAsync();
                     if (childReversals.Any())
                     {
                         foreach (var child in childReversals)
                         {
                             _db.JournalLines.RemoveRange(child.Lines);
                         }
                         _db.JournalEntries.RemoveRange(childReversals);
                     }
                     _db.JournalLines.RemoveRange(je.Lines);
                     _db.JournalEntries.Remove(je);
                 }
             }
             _db.PaymentVouchers.Remove(voucher);
         }

        var advanceNumber = adv.AdvanceNumber;
        _db.EmployeeAdvances.Remove(adv);
        await _db.SaveChangesAsync();
        try { await _audit.LogAsync("DeleteEmployeeAdvance", "EmployeeAdvance", id.ToString(), $"Deleted advance {advanceNumber}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return NoContent();
    }

    [HttpPut("{id}")]
    [RequirePermission(ModuleKeys.HrAdvances, requireEdit: true)]
    public async Task<IActionResult> Update(int id, [FromBody] CreateAdvanceDto dto)
    {
        var adv = await _db.EmployeeAdvances.FindAsync(id);
        if (adv == null) return NotFound();
        if (adv.Status != AdvanceStatus.Pending)
            return BadRequest(new { message = _t.Get("HR.AdvanceCannotDelete") });

        adv.Amount = dto.Amount;
        adv.AdvanceDate = dto.AdvanceDate;
        adv.Reason = dto.Reason?.Trim();
        adv.Notes = dto.Notes?.Trim();
        adv.CashAccountId = dto.CashAccountId;
        adv.CostCenter = dto.CostCenter ?? adv.CostCenter;
        adv.UpdatedAt = TimeHelper.GetEgyptTime();

        // Update voucher if exists
        var voucher = await _db.PaymentVouchers.FirstOrDefaultAsync(v => v.Reference == adv.AdvanceNumber);
        if (voucher != null)
        {
            voucher.Amount = adv.Amount;
            voucher.VoucherDate = adv.AdvanceDate;
            voucher.CashAccountId = adv.CashAccountId ?? 0;
            voucher.Description = $"تعديل سلفة موظف — {adv.AdvanceNumber}";
            voucher.CostCenter = adv.CostCenter;
            
            // Re-post to update journal entry
            await _accounting.PostPaymentVoucherAsync(voucher);
            adv.JournalEntryId = voucher.JournalEntryId;
        }

        await _db.SaveChangesAsync();
        try { await _audit.LogAsync("UpdateEmployeeAdvance", "EmployeeAdvance", id.ToString(), $"Updated advance {adv.AdvanceNumber}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return NoContent();
    }
}
