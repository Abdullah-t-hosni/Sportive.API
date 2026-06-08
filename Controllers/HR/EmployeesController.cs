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
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/employees")]
[RequirePermission(ModuleKeys.HrPayroll)]
public class EmployeesController : ControllerBase
{
    private readonly AppDbContext          _db;
    private readonly SequenceService       _seq;
    private readonly AccountingCoreService _core;
    private readonly ITranslator _t;

    public EmployeesController(AppDbContext db, SequenceService seq, AccountingCoreService core, ITranslator t)
        => (_db, _seq, _core, _t) = (db, seq, core, t);

    private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    [HttpGet]
    [RequirePermission(ModuleKeys.Hr)]
    [AllowPosAccess]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search     = null,
        [FromQuery] int?    departmentId = null,
        [FromQuery] EmployeeStatus? status = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.Employees
            .Include(e => e.Account)
            .Include(e => e.AppUser)
            .Include(e => e.Department)
            .AsQueryable();

        if (status.HasValue)
        {
            if (status.Value == EmployeeStatus.Active)
                q = q.Where(e => e.Status == EmployeeStatus.Active || (int)e.Status == 0);
            else
                q = q.Where(e => e.Status == status.Value);
        }
        if (departmentId.HasValue)       q = q.Where(e => e.DepartmentId == departmentId.Value);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(e => e.Name.Contains(search) || e.EmployeeNumber.Contains(search)
                           || (e.Phone != null && e.Phone.Contains(search))
                           || (e.NationalId != null && e.NationalId.Contains(search)));

        var total = await q.CountAsync();
        var items = await q.OrderBy(e => e.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new EmployeeDto(
                e.Id, e.EmployeeNumber, e.Name, e.Phone, e.Email, e.NationalId,
                e.JobTitle, e.DepartmentId, e.Department != null ? e.Department.Name : null,
                e.HireDate, e.TerminationDate,
                e.BaseSalary, e.TransportationAllowance, e.CommunicationAllowance, e.BonusAmount, e.FixedAllowance,
                e.BankAccount, (int)e.Status, e.Notes,
                e.AttachmentUrl, e.AttachmentPublicId,
                e.CreatedAt,
                e.AppUserId, e.AppUser != null ? e.AppUser.FullName : null,
                e.CostCenter,
                e.WorkHoursPerDay, e.OvertimeMultiplier, e.DaysPerMonth,
                e.AttendanceMode, e.ShiftStartTime, e.WeeklyDaysOff
            )).ToListAsync();

        return Ok(new PaginatedResult<EmployeeDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpGet("basic")]
    [AllowPosAccess]
    public async Task<IActionResult> GetBasic([FromQuery] int? year = null, [FromQuery] int? month = null)
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

        DateTime? endOfPeriod = null;
        if (year.HasValue && month.HasValue)
        {
            endOfPeriod = new DateTime(year.Value, month.Value, 1).AddMonths(1).AddTicks(-1);
        }

        return Ok(await _db.Employees
            .OrderBy(e => e.Name)
            .Select(e => new EmployeeBasicDto(
                e.Id, e.EmployeeNumber, e.Name, e.JobTitle, e.DepartmentId, 
                e.Department != null ? e.Department.Name : null, 
                e.BaseSalary, e.TransportationAllowance, e.CommunicationAllowance, e.BonusAmount, e.FixedAllowance,
                e.Advances.Where(a => a.Status != AdvanceStatus.FullyDeducted && (!endOfPeriod.HasValue || a.AdvanceDate <= endOfPeriod.Value)).Sum(a => a.Amount - a.DeductedAmount),
                e.Bonuses.Where(b => b.PayrollRunId == null && b.CashAccountId == null && (!endOfPeriod.HasValue || b.BonusDate <= endOfPeriod.Value)).Sum(b => b.Amount),
                e.Deductions.Where(d => d.PayrollRunId == null && d.CashAccountId == null && (!endOfPeriod.HasValue || d.DeductionDate <= endOfPeriod.Value)).Sum(d => d.Amount),
                (int)e.Status,
                e.WorkHoursPerDay, e.OvertimeMultiplier, e.DaysPerMonth))
            .ToListAsync());
    }

    [HttpGet("{id}")]
    [RequirePermission(ModuleKeys.Hr)]
    public async Task<IActionResult> GetById(int id)
    {
        var e = await _db.Employees
            .Include(x => x.Account)
            .Include(x => x.AppUser)
            .Include(x => x.Department)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        return Ok(new EmployeeDto(
            e.Id, e.EmployeeNumber, e.Name, e.Phone, e.Email, e.NationalId,
            e.JobTitle, e.DepartmentId, e.Department?.Name, e.HireDate, e.TerminationDate,
            e.BaseSalary, e.TransportationAllowance, e.CommunicationAllowance, e.BonusAmount, e.FixedAllowance, e.BankAccount, (int)e.Status, e.Notes,
            e.AttachmentUrl, e.AttachmentPublicId,
            e.CreatedAt,
            e.AppUserId, e.AppUser?.FullName,
            e.CostCenter,
            e.WorkHoursPerDay, e.OvertimeMultiplier, e.DaysPerMonth,
            e.AttendanceMode, e.ShiftStartTime, e.WeeklyDaysOff));
    }

    [HttpPost]
    [RequirePermission(ModuleKeys.Hr, requireEdit: true)]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(_t.Get("HR.NameRequired"));

        if (!string.IsNullOrEmpty(dto.AppUserId))
        {
            var conflict = await _db.Employees.AnyAsync(e => e.AppUserId == dto.AppUserId);
            if (conflict) return BadRequest(new { message = _t.Get("HR.AccountLinkedAlready") });
        }

        string prefix = "EMP";
        if (dto.DepartmentId.HasValue)
        {
            var dept = await _db.Departments.FindAsync(dto.DepartmentId.Value);
            if (dept != null)
            {
                prefix = SequenceService.GetDepartmentPrefix(dept.Name);
            }
        }

        string empNo;
        if (prefix == "ADM")
        {
            empNo = await _seq.NextAsync("ADM");
        }
        else
        {
            empNo = await _seq.NextAsync("EMP");
            empNo = empNo.Replace("EMP-", prefix + "-");
        }

        var core = (AccountingCoreService)HttpContext.RequestServices.GetService(typeof(AccountingCoreService))!;
        var maps = await core.GetSafeSystemMappingsAsync();
        var defaultAccId = maps.TryGetValue(MappingKeys.SalariesPayable.ToLower(), out var accId) ? accId : null;

        var emp = new Employee
        {
            EmployeeNumber   = empNo,
            Name             = dto.Name.Trim(),
            Phone            = dto.Phone?.Trim(),
            Email            = dto.Email?.Trim().ToLower(),
            NationalId       = dto.NationalId?.Trim(),
            JobTitle         = dto.JobTitle?.Trim(),
            DepartmentId     = dto.DepartmentId,
            HireDate         = dto.HireDate,
            BaseSalary       = dto.BaseSalary,
            TransportationAllowance = dto.TransportationAllowance,
            CommunicationAllowance  = dto.CommunicationAllowance,
            BonusAmount             = dto.BonusAmount,
            FixedAllowance          = dto.FixedAllowance,
            BankAccount      = dto.BankAccount?.Trim(),
            Notes            = dto.Notes?.Trim(),
            AttachmentUrl    = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId,
            AccountId        = defaultAccId,
            AppUserId        = string.IsNullOrEmpty(dto.AppUserId) ? null : dto.AppUserId,
            CostCenter       = dto.CostCenter,
            Status           = EmployeeStatus.Active,
            WorkHoursPerDay  = dto.WorkHoursPerDay,
            OvertimeMultiplier = dto.OvertimeMultiplier,
            DaysPerMonth     = dto.DaysPerMonth,
            AttendanceMode   = dto.AttendanceMode,
            ShiftStartTime   = dto.ShiftStartTime ?? "09:00",
            WeeklyDaysOff    = dto.WeeklyDaysOff ?? "Friday",
            CreatedAt        = TimeHelper.GetEgyptTime(),
            CreatedByUserId  = UserId
        };

        _db.Employees.Add(emp);
        await _db.SaveChangesAsync();
        return Ok(new { id = emp.Id, employeeNumber = emp.EmployeeNumber });
    }

    // PATCH /api/employees/{id}/link-user
    [HttpPatch("{id}/link-user")]
    [RequirePermission(ModuleKeys.Hr, requireEdit: true)]
    public async Task<IActionResult> LinkUser(int id, [FromBody] LinkUserDto dto)
    {
        var emp = await _db.Employees.FindAsync(id);
        if (emp == null) return NotFound();

        if (!string.IsNullOrEmpty(dto.AppUserId))
        {
            var userExists = await _db.Users.AnyAsync(u => u.Id == dto.AppUserId);
            if (!userExists) return BadRequest(new { message = _t.Get("HR.UserNotFound") });

            var conflict = await _db.Employees
                .AnyAsync(e => e.AppUserId == dto.AppUserId && e.Id != id);
            if (conflict)
                return BadRequest(new { message = _t.Get("HR.AccountLinkedAlready") });
        }

        emp.AppUserId = string.IsNullOrEmpty(dto.AppUserId) ? null : dto.AppUserId;
        emp.UpdatedAt = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();

        return Ok(new { message = dto.AppUserId != null ? _t.Get("HR.AccountLinked") : _t.Get("HR.AccountUnlinked") });
    }

    [HttpPut("{id}")]
    [RequirePermission(ModuleKeys.Hr, requireEdit: true)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateEmployeeDto dto)
    {
        var emp = await _db.Employees.FindAsync(id);
        if (emp == null) return NotFound();

        emp.Name              = dto.Name.Trim();
        emp.Phone             = dto.Phone?.Trim();
        emp.Email             = dto.Email?.Trim().ToLower();
        emp.NationalId        = dto.NationalId?.Trim();
        emp.JobTitle          = dto.JobTitle?.Trim();
        emp.DepartmentId      = dto.DepartmentId;
        emp.HireDate          = dto.HireDate;
        emp.TerminationDate   = dto.TerminationDate;
        emp.BaseSalary        = dto.BaseSalary;
        emp.TransportationAllowance = dto.TransportationAllowance;
        emp.CommunicationAllowance  = dto.CommunicationAllowance;
        emp.BonusAmount             = dto.BonusAmount;
        emp.FixedAllowance    = dto.FixedAllowance;
        emp.BankAccount       = dto.BankAccount?.Trim();
        emp.Notes             = dto.Notes?.Trim();
        emp.AttachmentUrl     = dto.AttachmentUrl;
        emp.AttachmentPublicId = dto.AttachmentPublicId;
        emp.Status            = dto.Status;
        emp.CostCenter        = dto.CostCenter;
        emp.WorkHoursPerDay   = dto.WorkHoursPerDay;
        emp.OvertimeMultiplier = dto.OvertimeMultiplier;
        emp.DaysPerMonth      = dto.DaysPerMonth;
        emp.AttendanceMode   = dto.AttendanceMode;
        emp.ShiftStartTime   = dto.ShiftStartTime ?? "09:00";
        emp.WeeklyDaysOff    = dto.WeeklyDaysOff ?? "Friday";
        emp.UpdatedAt         = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [RequirePermission(ModuleKeys.Hr, requireEdit: true)]
    public async Task<IActionResult> Delete(int id)
    {
        var emp = await _db.Employees
            .Include(e => e.PayrollItems)
            .Include(e => e.Advances)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (emp == null) return NotFound();
        if (emp.PayrollItems.Any() || emp.Advances.Any())
            return BadRequest(new { message = _t.Get("HR.CannotDeleteEmployee") });

        _db.Employees.Remove(emp);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id}/statement")]
    public async Task<IActionResult> GetStatement(int id, [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var mapDict = await _core.GetSafeSystemMappingsAsync();
        var hrAccountIds = new List<int>();
        
        // تجمع كل الحسابات المتعلقة بالموظفين (رواتب، سلف، مكافآت، خصومات)
        if (mapDict.TryGetValue(MappingKeys.SalariesPayable.ToLower(), out var s1) && s1.HasValue) hrAccountIds.Add(s1.Value);
        if (mapDict.TryGetValue(MappingKeys.EmployeeAdvances.ToLower(), out var s2) && s2.HasValue) hrAccountIds.Add(s2.Value);
        if (mapDict.TryGetValue(MappingKeys.EmployeeBonuses.ToLower(), out var s3) && s3.HasValue) hrAccountIds.Add(s3.Value);
        if (mapDict.TryGetValue(MappingKeys.EmployeeDeductions.ToLower(), out var s4) && s4.HasValue) hrAccountIds.Add(s4.Value);

        // Normalize dates to match Egyptian business day ranges (from 2 AM to 2 AM of next day)
        var egyptFrom = from.Date.AddHours(2);
        var egyptTo   = to.Date.AddDays(1).AddHours(2).AddTicks(-1);

        if (id == 0) return await GetGeneralStatement(egyptFrom, egyptTo, hrAccountIds);

        var accrualAccId = await _core.GetRequiredMappedAccountAsync(MappingKeys.SalariesPayable, mapDict);
        var emp = await _db.Employees.FindAsync(id);
        if (emp == null) return NotFound();

        // في كشف الموظف الفردي، نركز على كل حسابات الموظف (الرواتب المستحقة، السلف، المكافآت، الخصومات) بالإضافة لحسابه الشخصي
        var personalAccountIds = new List<int>(hrAccountIds);
        if (emp.AccountId.HasValue) personalAccountIds.Add(emp.AccountId.Value);
        personalAccountIds = personalAccountIds.Distinct().ToList();

        var preEntries = await _db.JournalLines
            .Where(l => l.EmployeeId == id && personalAccountIds.Contains(l.AccountId) && l.JournalEntry.EntryDate < egyptFrom && l.JournalEntry.Status == JournalEntryStatus.Posted)
            .Select(l => new { l.Debit, l.Credit })
            .ToListAsync();
        
        var openingBalance = preEntries.Sum(e => e.Debit - e.Credit);

        var lines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.EmployeeId == id && personalAccountIds.Contains(l.AccountId) && l.JournalEntry.EntryDate >= egyptFrom && l.JournalEntry.EntryDate <= egyptTo && l.JournalEntry.Status == JournalEntryStatus.Posted)
            .OrderBy(l => l.JournalEntry.EntryDate)
            .ThenBy(l => l.JournalEntryId)
            .ThenBy(l => l.Id)
            .ToListAsync();

        var advances = await _db.EmployeeAdvances
            .Where(a => a.EmployeeId == id)
            .ToDictionaryAsync(a => a.AdvanceNumber, a => new { a.Reason, a.Notes });

        var bonuses = await _db.EmployeeBonuses
            .Where(b => b.EmployeeId == id)
            .ToDictionaryAsync(b => b.BonusNumber, b => new { b.Reason, b.Notes });

        var deductions = await _db.EmployeeDeductions
            .Where(d => d.EmployeeId == id)
            .ToDictionaryAsync(d => d.DeductionNumber, d => new { d.Reason, d.Notes });

        var rows = new List<EmployeeStatementRowDto>();
        var runningBalance = openingBalance;

        foreach (var l in lines)
        {
            runningBalance += (l.Debit - l.Credit);
            
            string? rowNotes = null;
            var refNo = l.JournalEntry.Reference ?? "";
            if (refNo.StartsWith("ADV-") && advances.TryGetValue(refNo, out var advInfo))
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(advInfo.Reason)) parts.Add(advInfo.Reason);
                if (!string.IsNullOrEmpty(advInfo.Notes)) parts.Add(advInfo.Notes);
                rowNotes = parts.Count > 0 ? string.Join(" - ", parts) : null;
            }
            else if (refNo.StartsWith("BON-") && bonuses.TryGetValue(refNo, out var bonInfo))
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(bonInfo.Reason)) parts.Add(bonInfo.Reason);
                if (!string.IsNullOrEmpty(bonInfo.Notes)) parts.Add(bonInfo.Notes);
                rowNotes = parts.Count > 0 ? string.Join(" - ", parts) : null;
            }
            else if (refNo.StartsWith("DED-") && deductions.TryGetValue(refNo, out var dedInfo))
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(dedInfo.Reason)) parts.Add(dedInfo.Reason);
                if (!string.IsNullOrEmpty(dedInfo.Notes)) parts.Add(dedInfo.Notes);
                rowNotes = parts.Count > 0 ? string.Join(" - ", parts) : null;
            }

            var mainDesc = l.Description ?? l.JournalEntry.Description ?? "";
            if ((mainDesc.StartsWith("سند صرف") || mainDesc.StartsWith("سند قبض") || mainDesc.StartsWith("Payment Voucher") || mainDesc.StartsWith("Receipt Voucher")) 
                && !string.IsNullOrEmpty(l.JournalEntry.Description))
            {
                mainDesc = l.JournalEntry.Description;
            }

            rows.Add(new EmployeeStatementRowDto(
                l.JournalEntry.EntryDate,
                l.JournalEntry.EntryNumber,
                l.JournalEntry.Type.ToString(),
                mainDesc,
                l.Debit,
                l.Credit,
                runningBalance,
                Notes: rowNotes
            ));
        }

        return Ok(new EmployeeStatementDto(
            emp.Id, emp.Name, emp.EmployeeNumber, emp.JobTitle, emp.Account?.NameAr ?? "رواتب مستحقة موظفين",
            from, to, openingBalance, rows,
            rows.Sum(r => r.Debit), rows.Sum(r => r.Credit), runningBalance
        ));
    }

    private async Task<IActionResult> GetGeneralStatement(DateTime from, DateTime to, List<int> hrAccountIds)
    {
        var accrualAccId = await _core.GetRequiredMappedAccountAsync(MappingKeys.SalariesPayable);
        var acc = await _db.Accounts.FindAsync(accrualAccId);

        var preEntries = await _db.JournalLines
            .Where(l => l.EmployeeId != null && hrAccountIds.Contains(l.AccountId) && l.JournalEntry.EntryDate < from && l.JournalEntry.Status == JournalEntryStatus.Posted)
            .Select(l => new { l.Debit, l.Credit })
            .ToListAsync();
        
        var openingBalance = preEntries.Sum(e => e.Debit - e.Credit);

        var lines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Include(l => l.Employee)
            .Where(l => l.EmployeeId != null && hrAccountIds.Contains(l.AccountId) && l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to && l.JournalEntry.Status == JournalEntryStatus.Posted)
            .OrderBy(l => l.JournalEntry.EntryDate)
            .ThenBy(l => l.JournalEntryId)
            .ThenBy(l => l.Id)
            .ToListAsync();

        var advances = await _db.EmployeeAdvances
            .ToDictionaryAsync(a => a.AdvanceNumber, a => new { a.Reason, a.Notes });

        var bonuses = await _db.EmployeeBonuses
            .ToDictionaryAsync(b => b.BonusNumber, b => new { b.Reason, b.Notes });

        var deductions = await _db.EmployeeDeductions
            .ToDictionaryAsync(d => d.DeductionNumber, d => new { d.Reason, d.Notes });

        var rows = new List<EmployeeStatementRowDto>();
        var runningBalance = openingBalance;

        foreach (var l in lines)
        {
            runningBalance += (l.Debit - l.Credit);

            string detailDesc = "";
            var refNo = l.JournalEntry.Reference ?? "";
            if (refNo.StartsWith("ADV-") && advances.TryGetValue(refNo, out var advInfo))
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(advInfo.Reason)) parts.Add(advInfo.Reason);
                if (!string.IsNullOrEmpty(advInfo.Notes)) parts.Add(advInfo.Notes);
                detailDesc = parts.Count > 0 ? $" ({string.Join(" - ", parts)})" : "";
            }
            else if (refNo.StartsWith("BON-") && bonuses.TryGetValue(refNo, out var bonInfo))
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(bonInfo.Reason)) parts.Add(bonInfo.Reason);
                if (!string.IsNullOrEmpty(bonInfo.Notes)) parts.Add(bonInfo.Notes);
                detailDesc = parts.Count > 0 ? $" ({string.Join(" - ", parts)})" : "";
            }
            else if (refNo.StartsWith("DED-") && deductions.TryGetValue(refNo, out var dedInfo))
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(dedInfo.Reason)) parts.Add(dedInfo.Reason);
                if (!string.IsNullOrEmpty(dedInfo.Notes)) parts.Add(dedInfo.Notes);
                detailDesc = parts.Count > 0 ? $" ({string.Join(" - ", parts)})" : "";
            }

            var mainDesc = l.Description ?? l.JournalEntry.Description ?? "";
            if ((mainDesc.StartsWith("سند صرف") || mainDesc.StartsWith("سند قبض") || mainDesc.StartsWith("Payment Voucher") || mainDesc.StartsWith("Receipt Voucher")) 
                && !string.IsNullOrEmpty(l.JournalEntry.Description))
            {
                mainDesc = l.JournalEntry.Description;
            }

            var finalDesc = $"[{l.Employee?.Name}] " + mainDesc + detailDesc;

            rows.Add(new EmployeeStatementRowDto(
                l.JournalEntry.EntryDate,
                l.JournalEntry.EntryNumber,
                l.JournalEntry.Type.ToString(),
                finalDesc,
                l.Debit,
                l.Credit,
                runningBalance
            ));
        }

        return Ok(new EmployeeStatementDto(
            0, _t.Get("HR.GeneralStatementName"), "000", "GENERAL_REPORT", acc?.NameAr ?? _t.Get("HR.DefaultAccrualAccountName"),
            from, to, openingBalance, rows,
            rows.Sum(r => r.Debit), rows.Sum(r => r.Credit), runningBalance
        ));
    }

    [HttpPost("backfill-prefixes")]
    [RequirePermission(ModuleKeys.Hr, requireEdit: true)]
    public async Task<IActionResult> BackfillPrefixes()
    {
        var employees = await _db.Employees
            .Include(e => e.Department)
            .ToListAsync();

        int admCounter = 0;
        int otherCounter = 100; // starts at 100, so first other employee is 101

        foreach (var emp in employees)
        {
            string prefix = "EMP";
            if (emp.Department != null)
            {
                prefix = SequenceService.GetDepartmentPrefix(emp.Department.Name);
            }

            if (prefix == "ADM")
            {
                admCounter++;
                emp.EmployeeNumber = $"ADM-{admCounter:D4}";
            }
            else
            {
                otherCounter++;
                emp.EmployeeNumber = $"{prefix}-{otherCounter:D4}";
            }
        }

        await _db.SaveChangesAsync();

        var now = TimeHelper.GetEgyptTime();

        // 1. Seed the "ADM" sequence counter to the max admCounter (or 1 if none)
        var seqAdm = await _db.DbSequences
            .FirstOrDefaultAsync(s => s.Prefix == "ADM" && s.Stamp == string.Empty);

        if (seqAdm == null)
        {
            seqAdm = new DbSequence
            {
                Prefix = "ADM",
                Stamp = string.Empty,
                LastValue = Math.Max(1, admCounter),
                LastUpdatedAt = now
            };
            _db.DbSequences.Add(seqAdm);
        }
        else
        {
            if (seqAdm.LastValue < admCounter)
            {
                seqAdm.LastValue = admCounter;
                seqAdm.LastUpdatedAt = now;
            }
        }

        // 2. Seed the global "EMP" sequence counter to the max otherCounter (or 100 if none)
        var seqEmp = await _db.DbSequences
            .FirstOrDefaultAsync(s => s.Prefix == "EMP" && s.Stamp == string.Empty);

        if (seqEmp == null)
        {
            seqEmp = new DbSequence
            {
                Prefix = "EMP",
                Stamp = string.Empty,
                LastValue = Math.Max(100, otherCounter),
                LastUpdatedAt = now
            };
            _db.DbSequences.Add(seqEmp);
        }
        else
        {
            if (seqEmp.LastValue < otherCounter)
            {
                seqEmp.LastValue = otherCounter;
                seqEmp.LastUpdatedAt = now;
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new { 
            message = $"Successfully updated {employees.Count} employees globally.", 
            details = new { admCount = admCounter, otherCount = otherCounter } 
        });
    }
}
