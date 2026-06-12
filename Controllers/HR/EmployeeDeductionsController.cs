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
[Route("api/employee-deductions")]
[RequirePermission(ModuleKeys.HrVouchers)]
public class EmployeeDeductionsController : ControllerBase
{
    private readonly AppDbContext    _db;
    private readonly SequenceService _seq;
    private readonly IAccountingService _accounting;
    private readonly AccountingCoreService _core;
    private readonly ITranslator _t;

    public EmployeeDeductionsController(AppDbContext db, SequenceService seq, IAccountingService accounting, AccountingCoreService core, ITranslator t)
        => (_db, _seq, _accounting, _core, _t) = (db, seq, accounting, core, t);
    private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? employeeId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.EmployeeDeductions
            .Include(d => d.Employee)
            .Include(d => d.CashAccount)
            .AsQueryable();

        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        if (!canViewAll)
        {
            int? isolatedBranchId = User.GetBranchId();
            if (isolatedBranchId.HasValue)
            {
                q = q.Where(d => d.Employee.BranchId == isolatedBranchId.Value);
            }
        }

        if (employeeId.HasValue) q = q.Where(d => d.EmployeeId == employeeId.Value);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(d => d.DeductionDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new EmployeeDeductionDto(
                d.Id, d.DeductionNumber, d.EmployeeId, d.Employee.Name,
                d.DeductionDate, d.Amount, (int)d.DeductionType, d.Reason, d.Notes,
                d.PayrollRunId, d.CashAccountId, d.CashAccount != null ? d.CashAccount.NameAr : null,
                d.JournalEntryId, d.CreatedAt, d.CostCenter
            )).ToListAsync();

        return Ok(new PaginatedResult<EmployeeDeductionDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDeductionDto dto)
    {
        var emp = await _db.Employees.FindAsync(dto.EmployeeId);
        if (emp == null) return NotFound();

        var dedNo = await _seq.NextAsync("DED");

        var ded = new EmployeeDeduction
        {
            DeductionNumber = dedNo,
            EmployeeId      = dto.EmployeeId,
            DeductionDate   = dto.DeductionDate,
            Amount          = dto.Amount,
            DeductionType   = dto.DeductionType,
            Reason          = dto.Reason?.Trim(),
            Notes           = dto.Notes?.Trim(),
            CashAccountId   = dto.CashAccountId,
            CostCenter      = dto.CostCenter ?? emp.CostCenter,
            CreatedAt       = TimeHelper.GetEgyptTime(),
            CreatedByUserId = UserId
        };

        _db.EmployeeDeductions.Add(ded);

        // 🎯 UNIFIED VOUCHER SYSTEM: Create a ReceiptVoucher record for this deduction (if cash)
        if (dto.CashAccountId.HasValue)
        {
            var mapDict = await _core.GetSafeSystemMappingsAsync();
            if (!mapDict.TryGetValue(MappingKeys.EmployeeDeductions, out var deductionRevenueAccId) || deductionRevenueAccId == null)
                return BadRequest(new { message = _t.Get("HR.DeductionAccountNotSet") });

            var voucher = new ReceiptVoucher
            {
                VoucherNumber = ded.DeductionNumber,
                VoucherDate = ded.DeductionDate,
                Amount = ded.Amount,
                CashAccountId = ded.CashAccountId ?? 0,
                FromAccountId = deductionRevenueAccId.Value,
                EmployeeId = emp.Id,
                PaymentMethod = VoucherPaymentMethod.Cash,
                Description = $"تحصيل خصم فوري — {emp.Name}",
                Reference = ded.DeductionNumber,
                CreatedAt = TimeHelper.GetEgyptTime(),
                CreatedByUserId = UserId,
                CostCenter = ded.CostCenter
            };
            _db.ReceiptVouchers.Add(voucher);
            await _db.SaveChangesAsync();

            await _accounting.PostReceiptVoucherAsync(voucher);
            ded.JournalEntryId = voucher.JournalEntryId;
            await _db.SaveChangesAsync();
        }
        else
        {
            await _db.SaveChangesAsync();
        }

        return Ok(new { id = ded.Id, deductionNumber = ded.DeductionNumber, journalEntryId = ded.JournalEntryId });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var ded = await _db.EmployeeDeductions.FindAsync(id);
        if (ded == null) return NotFound();
        if (ded.PayrollRunId.HasValue)
            return BadRequest(new { message = _t.Get("HR.DeductionCannotDelete") });

        _db.EmployeeDeductions.Remove(ded);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{id}")]
    [RequirePermission(ModuleKeys.HrVouchers, requireEdit: true)]
    public async Task<IActionResult> Update(int id, [FromBody] CreateDeductionDto dto)
    {
        var ded = await _db.EmployeeDeductions.FindAsync(id);
        if (ded == null) return NotFound();
        if (ded.PayrollRunId.HasValue)
            return BadRequest(new { message = _t.Get("HR.DeductionCannotEdit") });

        ded.Amount = dto.Amount;
        ded.DeductionDate = dto.DeductionDate;
        ded.DeductionType = dto.DeductionType;
        ded.Reason = dto.Reason?.Trim();
        ded.Notes = dto.Notes?.Trim();
        ded.CashAccountId = dto.CashAccountId;
        ded.CostCenter = dto.CostCenter ?? ded.CostCenter;
        ded.UpdatedAt = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
