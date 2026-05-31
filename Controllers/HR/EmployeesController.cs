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

        var empNo = await _seq.NextAsync("EMP");
        if (dto.DepartmentId.HasValue)
        {
            var dept = await _db.Departments.FindAsync(dto.DepartmentId.Value);
            if (dept != null)
            {
                var deptPrefix = SequenceService.GetDepartmentPrefix(dept.Name);
                empNo = empNo.Replace("EMP-", deptPrefix + "-");
            }
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
            .ToListAsync();

        var rows = new List<EmployeeStatementRowDto>();
        var runningBalance = openingBalance;

        foreach (var l in lines)
        {
            runningBalance += (l.Debit - l.Credit);
            rows.Add(new EmployeeStatementRowDto(
                l.JournalEntry.EntryDate,
                l.JournalEntry.EntryNumber,
                l.JournalEntry.Type.ToString(),
                l.Description ?? l.JournalEntry.Description ?? "",
                l.Debit,
                l.Credit,
                runningBalance
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
            .ToListAsync();

        var rows = new List<EmployeeStatementRowDto>();
        var runningBalance = openingBalance;

        foreach (var l in lines)
        {
            runningBalance += (l.Debit - l.Credit);
            rows.Add(new EmployeeStatementRowDto(
                l.JournalEntry.EntryDate,
                l.JournalEntry.EntryNumber,
                l.JournalEntry.Type.ToString(),
                $"[{l.Employee?.Name}] " + (l.Description ?? l.JournalEntry.Description ?? ""),
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
            .OrderBy(e => e.Id)
            .ToListAsync();

        int counter = 0;
        foreach (var emp in employees)
        {
            counter++;
            string prefix = "EMP";
            if (emp.Department != null)
            {
                prefix = SequenceService.GetDepartmentPrefix(emp.Department.Name);
            }

            emp.EmployeeNumber = $"{prefix}-{counter:D4}";
        }

        await _db.SaveChangesAsync();

        // Also seed the global "EMP" sequence counter to the total count
        var now = TimeHelper.GetEgyptTime();
        var seq = await _db.DbSequences
            .FirstOrDefaultAsync(s => s.Prefix == "EMP" && s.Stamp == string.Empty);

        if (seq == null)
        {
            seq = new DbSequence
            {
                Prefix = "EMP",
                Stamp = string.Empty,
                LastValue = counter,
                LastUpdatedAt = now
            };
            _db.DbSequences.Add(seq);
        }
        else
        {
            if (seq.LastValue < counter)
            {
                seq.LastValue = counter;
                seq.LastUpdatedAt = now;
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new { message = $"Successfully updated {employees.Count} employees globally and seeded the EMP sequence to {counter}." });
    }
}
