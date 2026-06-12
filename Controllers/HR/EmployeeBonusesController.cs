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
[Route("api/employee-bonuses")]
[RequirePermission(ModuleKeys.HrAdvances)]
public class EmployeeBonusesController : ControllerBase
{
    private readonly AppDbContext    _db;
    private readonly SequenceService _seq;
    private readonly IAccountingService _accounting;
    private readonly AccountingCoreService _core;
    private readonly ITranslator _t;

    public EmployeeBonusesController(AppDbContext db, SequenceService seq, IAccountingService accounting, AccountingCoreService core, ITranslator t)
        => (_db, _seq, _accounting, _core, _t) = (db, seq, accounting, core, t);
    private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? employeeId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.EmployeeBonuses
            .Include(b => b.Employee)
            .Include(b => b.CashAccount)
            .AsQueryable();

        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        if (!canViewAll)
        {
            int? isolatedBranchId = User.GetBranchId();
            if (isolatedBranchId.HasValue)
            {
                q = q.Where(b => b.Employee.BranchId == isolatedBranchId.Value);
            }
        }

        if (employeeId.HasValue) q = q.Where(b => b.EmployeeId == employeeId.Value);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(b => b.BonusDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(b => new EmployeeBonusDto(
                b.Id, b.BonusNumber, b.EmployeeId, b.Employee.Name,
                b.BonusDate, b.Amount, (int)b.BonusType, b.Reason, b.Notes,
                b.PayrollRunId, b.CashAccountId, b.CashAccount != null ? b.CashAccount.NameAr : null,
                b.JournalEntryId, b.CreatedAt, b.CostCenter
            )).ToListAsync();

        return Ok(new PaginatedResult<EmployeeBonusDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpPost]
    [RequirePermission(ModuleKeys.HrAdvances, requireEdit: true)]
    public async Task<IActionResult> Create([FromBody] CreateBonusDto dto)
    {
        var emp = await _db.Employees.FindAsync(dto.EmployeeId);
        if (emp == null) return NotFound();

        var mapDict = await _core.GetSafeSystemMappingsAsync();

        // 🎯 UNIFIED VOUCHER SYSTEM: Create a PaymentVoucher record for this bonus (if cash disbursement)
        if (dto.CashAccountId.HasValue && dto.CashAccountId > 0)
        {
            if (!mapDict.TryGetValue(MappingKeys.SalaryExpense, out var bonusExpenseAccId) || bonusExpenseAccId == null)
                return BadRequest(new { message = _t.Get("HR.BonusAccountNotSet") });
        }

        // Retry logic for sequence generation
        for (int retry = 0; retry < 3; retry++)
        {
            try
            {
                var bonNo = await _seq.NextAsync("BON");

                var bonus = new EmployeeBonus
                {
                    BonusNumber = bonNo,
                    EmployeeId = dto.EmployeeId,
                    BonusDate = dto.BonusDate,
                    Amount = dto.Amount,
                    BonusType = dto.BonusType,
                    Reason = dto.Reason?.Trim(),
                    Notes = dto.Notes?.Trim(),
                    CashAccountId = dto.CashAccountId,
                    CostCenter = dto.BonusCostCenter ?? emp.CostCenter, // Use provided or fall back to employee default
                    CreatedAt = TimeHelper.GetEgyptTime(),
                    CreatedByUserId = UserId
                };

                _db.EmployeeBonuses.Add(bonus);

                if (dto.CashAccountId.HasValue && dto.CashAccountId > 0)
                {
                    // Get account again from dict
                    mapDict.TryGetValue(MappingKeys.SalaryExpense, out var bonusExpenseAccId);

                    var voucher = new PaymentVoucher
                    {
                        VoucherNumber = bonus.BonusNumber,
                        VoucherDate = bonus.BonusDate,
                        Amount = bonus.Amount,
                        CashAccountId = bonus.CashAccountId.GetValueOrDefault(),
                        ToAccountId = bonusExpenseAccId.GetValueOrDefault(),
                        EmployeeId = emp.Id,
                        PaymentMethod = VoucherPaymentMethod.Cash,
                        Description = $"مكافأة موظف — {emp.Name}",
                        Reference = bonus.BonusNumber,
                        CreatedAt = TimeHelper.GetEgyptTime(),
                        CreatedByUserId = UserId,
                        CostCenter = bonus.CostCenter
                    };
                    _db.PaymentVouchers.Add(voucher);
                    await _db.SaveChangesAsync();

                    await _accounting.PostPaymentVoucherAsync(voucher);
                    bonus.JournalEntryId = voucher.JournalEntryId;
                    await _db.SaveChangesAsync();
                }
                else
                {
                    await _db.SaveChangesAsync();
                }

                return Ok(new { id = bonus.Id, bonusNumber = bonus.BonusNumber, journalEntryId = bonus.JournalEntryId });
            }
            catch (DbUpdateException ex) when (retry < 2 && (ex.InnerException?.Message.Contains("duplicate") == true || ex.Message.Contains("duplicate") == true))
            {
                _db.ChangeTracker.Clear();
            }
        }

        return StatusCode(409, new { message = _t.Get("HR.BonusDuplicateNumber") });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var bon = await _db.EmployeeBonuses.FindAsync(id);
        if (bon == null) return NotFound();
        if (bon.PayrollRunId.HasValue)
            return BadRequest(new { message = _t.Get("HR.BonusCannotDelete") });

        var voucher = await _db.PaymentVouchers.FirstOrDefaultAsync(v => v.Reference == bon.BonusNumber);
        if (voucher != null)
        {
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

        _db.EmployeeBonuses.Remove(bon);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{id}")]
    [RequirePermission(ModuleKeys.HrAdvances, requireEdit: true)]
    public async Task<IActionResult> Update(int id, [FromBody] CreateBonusDto dto)
    {
        var bon = await _db.EmployeeBonuses.FindAsync(id);
        if (bon == null) return NotFound();
        if (bon.PayrollRunId.HasValue)
            return BadRequest(new { message = _t.Get("HR.BonusCannotEdit") });

        bon.Amount = dto.Amount;
        bon.BonusDate = dto.BonusDate;
        bon.BonusType = dto.BonusType;
        bon.Reason = dto.Reason?.Trim();
        bon.Notes = dto.Notes?.Trim();
        bon.CashAccountId = dto.CashAccountId;
        bon.CostCenter = dto.BonusCostCenter ?? bon.CostCenter;
        bon.UpdatedAt = TimeHelper.GetEgyptTime();

        var voucher = await _db.PaymentVouchers.FirstOrDefaultAsync(v => v.Reference == bon.BonusNumber);
        if (voucher != null)
        {
            voucher.Amount = bon.Amount;
            voucher.VoucherDate = bon.BonusDate;
            voucher.CashAccountId = bon.CashAccountId ?? 0;
            voucher.CostCenter = bon.CostCenter;
            
            await _accounting.PostPaymentVoucherAsync(voucher);
            bon.JournalEntryId = voucher.JournalEntryId;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
