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

// 1. EMPLOYEES (الموظفين)

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
                e.WorkHoursPerDay, e.OvertimeMultiplier, e.DaysPerMonth
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
            e.WorkHoursPerDay, e.OvertimeMultiplier, e.DaysPerMonth));
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
}

// 2. PAYROLL RUNS 
[ApiController]
[Route("api/payroll")]
[RequirePermission(ModuleKeys.HrPayroll)]
public class PayrollController : ControllerBase
{
    private readonly AppDbContext    _db;
    private readonly SequenceService _seq;
    private readonly AccountingCoreService _core;
    private readonly ITranslator _t;

    public PayrollController(AppDbContext db, SequenceService seq, AccountingCoreService core, ITranslator t)
        => (_db, _seq, _core, _t) = (db, seq, core, t);
    private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? year = null, [FromQuery] int? month = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.PayrollRuns.Include(p => p.Items).AsQueryable();
        if (year.HasValue)  q = q.Where(p => p.PeriodYear  == year.Value);
        if (month.HasValue) q = q.Where(p => p.PeriodMonth == month.Value);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(p => p.PeriodYear).ThenByDescending(p => p.PeriodMonth)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new PayrollRunSummaryDto(
                p.Id, p.PayrollNumber, p.PeriodYear, p.PeriodMonth,
                p.TotalNetPayable, p.Items.Count, (int)p.Status, p.JournalEntryId, p.PaymentJournalEntryId, p.CreatedAt))
            .ToListAsync();

        return Ok(new PaginatedResult<PayrollRunSummaryDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var run = await _db.PayrollRuns
            .Include(p => p.Items).ThenInclude(i => i.Employee)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (run == null) return NotFound();
        return Ok(ToDto(run));
    }

    // POST /api/payroll 
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePayrollRunDto dto)
    {
        try 
        {
            if (dto == null || dto.Items == null || !dto.Items.Any()) 
                return BadRequest(new { message = _t.Get("HR.PayrollMinOneEmployee") });

            var lang = Request.Headers["Accept-Language"].ToString().StartsWith("en") ? "en" : "ar";

            var existing = await _db.PayrollRuns.FirstOrDefaultAsync(p => p.PeriodYear == dto.PeriodYear && p.PeriodMonth == dto.PeriodMonth);
            if (existing != null)
            {
                return Conflict(new { 
                    message = lang == "ar" ? _t.Get("HR.PayrollAlreadyExists", dto.PeriodMonth.ToString(), dto.PeriodYear.ToString(), existing.PayrollNumber) : $"A payroll run for {dto.PeriodMonth}/{dto.PeriodYear} already exists with number ({existing.PayrollNumber}).",
                    existingId = existing.Id
                });
            }

            var payNo = await _seq.NextAsync("PAY");

            var run = new PayrollRun
            {
                PayrollNumber              = payNo,
                PeriodYear                 = dto.PeriodYear,
                PeriodMonth                = dto.PeriodMonth,
                Notes                      = dto.Notes?.Trim(),
                WagesExpenseAccountId      = dto.WagesExpenseAccountId,
                AccruedSalariesAccountId   = dto.AccruedSalariesAccountId,
                DeductionRevenueAccountId  = dto.DeductionRevenueAccountId,
                AdvancesAccountId          = dto.AdvancesAccountId,
                Status                     = PayrollStatus.Draft,
                CreatedAt                  = TimeHelper.GetEgyptTime(),
                CreatedByUserId            = UserId
            };

            decimal totalBasic = 0, totalTrans = 0, totalComm = 0, totalBonus = 0, totalFixedAll = 0, totalDed = 0, totalAdv = 0, totalAbsence = 0, totalOvertime = 0;
            decimal totalCommission = 0;

            var startOfPeriod = new DateTime(dto.PeriodYear, dto.PeriodMonth, 1);
            var endOfPeriod = startOfPeriod.AddMonths(1).AddDays(-1);
            
            var orders = await _db.Orders
                .Include(o => o.Items)
                .Where(o => o.CreatedAt >= startOfPeriod && o.CreatedAt <= endOfPeriod && o.Status != OrderStatus.Cancelled)
                .ToListAsync();

            foreach (var itemDto in dto.Items)
            {
                var emp = await _db.Employees
                    .Include(e => e.CommissionSetting)
                    .ThenInclude(s => s != null ? s.Tiers : null)
                    .FirstOrDefaultAsync(e => e.Id == itemDto.EmployeeId);
                    
                if (emp == null) continue;

                var basic = itemDto.OverrideBasicSalary ?? emp.BaseSalary;
                var trans = itemDto.TransportationAllowance;
                var comm  = itemDto.CommunicationAllowance;
                var bonus = itemDto.BonusAmount;
                var fixAll = itemDto.FixedAllowance;

                // Calculate Commission
                decimal earnedCommission = 0;
                if (emp.CommissionSetting != null)
                {
                    var empOrders = orders.Where(o => 
                        o.SalesPersonId == emp.AppUserId || 
                        o.SalesPersonId == emp.Id.ToString()
                    ).ToList();

                    var scheme = emp.CommissionSetting.CommissionSchemeId != null 
                        ? await _db.CommissionSchemes.Include(s => s.Tiers).FirstOrDefaultAsync(s => s.Id == emp.CommissionSetting.CommissionSchemeId)
                        : null;

                    var basis = scheme != null ? scheme.Basis : emp.CommissionSetting.Basis;
                    var type = scheme != null ? scheme.Type : emp.CommissionSetting.Type;
                    var defaultRate = scheme != null ? scheme.DefaultRate : emp.CommissionSetting.DefaultRate;
                    var targetAmount = scheme != null ? scheme.TargetAmount : emp.CommissionSetting.TargetAmount;
                    var tiersList = scheme != null 
                        ? scheme.Tiers.Select(t => new { t.MinAmount, t.MaxAmount, t.Rate }).ToList() 
                        : emp.CommissionSetting.Tiers.Select(t => new { t.MinAmount, t.MaxAmount, t.Rate }).ToList();

                    var returnsAmount = empOrders.Sum(o => o.Status == OrderStatus.Returned ? o.TotalAmount : o.Items.Sum(i => i.Quantity > 0 ? (i.TotalPrice / i.Quantity) * i.ReturnedQuantity : 0));

                    decimal relevantSales = basis == CommissionBasis.NetSales 
                        ? empOrders.Sum(o => o.TotalAmount) - returnsAmount
                        : empOrders.Sum(o => o.SubTotal) - returnsAmount;

                    if (type == CommissionType.TargetAchievementTiers || relevantSales >= targetAmount)
                    {
                        if (type == CommissionType.PercentageOfSales)
                        {
                            earnedCommission = relevantSales * (defaultRate / 100);
                        }
                        else if (type == CommissionType.FixedAmountPerItem)
                        {
                            var orderIds = empOrders.Select(o => o.Id).ToList();
                            var itemsCount = await _db.OrderItems
                                .Where(oi => orderIds.Contains(oi.OrderId))
                                .SumAsync(oi => oi.Quantity);
                            
                            earnedCommission = itemsCount * defaultRate;
                        }
                        else if (type == CommissionType.TieredPercentage)
                        {
                            var sortedTiers = tiersList.OrderBy(t => t.MinAmount).ToList();
                            var applicableTier = sortedTiers.LastOrDefault(t => relevantSales >= t.MinAmount && relevantSales <= t.MaxAmount);
                            
                            if (applicableTier != null)
                            {
                                earnedCommission = relevantSales * (applicableTier.Rate / 100);
                            }
                            else
                            {
                                var lastTier = sortedTiers.LastOrDefault();
                                if (lastTier != null && relevantSales > lastTier.MaxAmount)
                                {
                                    earnedCommission = relevantSales * (lastTier.Rate / 100);
                                }
                                else
                                {
                                    earnedCommission = relevantSales * (defaultRate / 100);
                                }
                            }
                        }
                        else if (type == CommissionType.TargetAchievementTiers)
                        {
                            var sortedTiers = tiersList.OrderBy(t => t.MinAmount).ToList();
                            decimal achievementPercentage = targetAmount > 0 ? (relevantSales / targetAmount) * 100 : 0;
                            var applicableTier = sortedTiers.LastOrDefault(t => achievementPercentage >= t.MinAmount && achievementPercentage <= t.MaxAmount);
                            
                            if (applicableTier != null)
                            {
                                earnedCommission = relevantSales * (applicableTier.Rate / 100);
                            }
                            else
                            {
                                var lastTier = sortedTiers.LastOrDefault();
                                if (lastTier != null && achievementPercentage > lastTier.MaxAmount)
                                {
                                    earnedCommission = relevantSales * (lastTier.Rate / 100);
                                }
                                else
                                {
                                    earnedCommission = relevantSales * (defaultRate / 100);
                                }
                            }
                        }
                    }
                }

                totalBasic += basic;
                totalTrans += trans;
                totalComm  += comm;
                totalBonus += bonus;
                totalFixedAll += fixAll;
                totalDed   += itemDto.DeductionAmount;
                totalAdv   += itemDto.AdvanceDeducted;
                totalAbsence += itemDto.AbsenceDeduction;
                totalOvertime += itemDto.OvertimeAmount;
                totalCommission += earnedCommission;

                run.Items.Add(new PayrollItem
                {
                    EmployeeId      = emp.Id,
                    BasicSalary     = basic,
                    TransportationAllowance = trans,
                    CommunicationAllowance  = comm,
                    BonusAmount     = bonus,
                    FixedAllowance  = fixAll,
                    DeductionAmount = itemDto.DeductionAmount,
                    AdvanceDeducted = itemDto.AdvanceDeducted,
                    AbsenceDays     = itemDto.AbsenceDays,
                    AbsenceDeduction = itemDto.AbsenceDeduction,
                    OvertimeHours   = itemDto.OvertimeHours,
                    OvertimeAmount  = itemDto.OvertimeAmount,
                    CommissionAmount = earnedCommission,
                    Notes           = itemDto.Notes,
                    CreatedAt       = TimeHelper.GetEgyptTime()
                });
            }

            run.TotalBasicSalary      = totalBasic;
            run.TotalTransportation   = totalTrans;
            run.TotalCommunication    = totalComm;
            run.TotalBonuses          = totalBonus;
            run.TotalFixedAllowances  = totalFixedAll;
            run.TotalOvertimeAmount    = totalOvertime;
            run.TotalDeductions       = totalDed;
            run.TotalAbsenceDeduction  = totalAbsence;
            run.TotalAdvancesDeducted = totalAdv;
            run.TotalNetPayable       = totalBasic + totalTrans + totalComm + totalBonus + totalFixedAll + totalOvertime + totalCommission - totalDed - totalAbsence - totalAdv;

            _db.PayrollRuns.Add(run);
            await _db.SaveChangesAsync();
            return Ok(new { id = run.Id, payrollNumber = run.PayrollNumber });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = _t.Get("HR.PayrollCreateError"), details = ex.Message });
        }
    }

    // POST /api/payroll/{id}/post 
    [HttpPost("{id}/post")]
    public async Task<IActionResult> Post(int id)
    {
        var run = await _db.PayrollRuns
            .Include(p => p.Items).ThenInclude(i => i.Employee)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (run == null) return NotFound();
        if (run.Status == PayrollStatus.Posted)
            return BadRequest(new { message = _t.Get("HR.PayrollAlreadyPosted") });

        var mapDict = await _core.GetSafeSystemMappingsAsync();

        var wagesAccId   = run.WagesExpenseAccountId ?? await _core.GetRequiredMappedAccountAsync(MappingKeys.SalaryExpense, mapDict);
        var accrualAccId = run.AccruedSalariesAccountId ?? await _core.GetRequiredMappedAccountAsync(MappingKeys.SalariesPayable, mapDict);
        var dedAccId     = run.DeductionRevenueAccountId ?? await _core.GetRequiredMappedAccountAsync(MappingKeys.EmployeeDeductions, mapDict);
        var advAccId     = run.AdvancesAccountId ?? await _core.GetRequiredMappedAccountAsync(MappingKeys.EmployeeAdvances, mapDict);
        var overtimeAccId = await _core.GetRequiredMappedAccountAsync(MappingKeys.OvertimeExpense, mapDict);
        
        var transAccId   = run.TotalTransportation > 0 ? await _core.GetRequiredMappedAccountAsync(MappingKeys.TransportationAllowanceExpense, mapDict) : 0;
        var commAccId    = run.TotalCommunication > 0 ? await _core.GetRequiredMappedAccountAsync(MappingKeys.CommunicationAllowanceExpense, mapDict) : 0;
        var bonusAccId   = (run.TotalBonuses > 0 || run.Items.Any(i => i.BonusAmount > 0)) ? await _core.GetRequiredMappedAccountAsync(MappingKeys.EmployeeBonuses, mapDict) : 0;
        var fixAllAccId  = run.TotalFixedAllowances > 0 ? await _core.GetRequiredMappedAccountAsync(MappingKeys.FixedAllowanceExpense, mapDict) : 0;

        JournalEntry? je = null;

        if (true) // Always proceed with strict mappings
        {
            var jeNo = await _seq.NextAsync("JE");

            var totalGross = run.TotalBasicSalary + run.TotalBonuses;

            je = new JournalEntry
            {
                EntryNumber     = jeNo,
                EntryDate       = TimeHelper.GetEgyptTime(),
                Type            = JournalEntryType.Payroll,
                Status          = JournalEntryStatus.Posted,
                Description     = _t.Get("HR.PayrollRunDescription", run.PeriodMonth, run.PeriodYear),
                Reference       = run.PayrollNumber,
                CreatedByUserId = UserId,
                CreatedAt       = TimeHelper.GetEgyptTime(),
                Lines           = new List<JournalLine>()
            };

            var itemsByCostCenter = run.Items.GroupBy(i => i.Employee?.CostCenter ?? OrderSource.General);

            foreach (var group in itemsByCostCenter)
            {
                var cc = group.Key;
                var ccBasic = group.Sum(i => i.BasicSalary) - group.Sum(i => i.AbsenceDeduction); // Net of absence
                var ccOvertime = group.Sum(i => i.OvertimeAmount);
                var ccTrans = group.Sum(i => i.TransportationAllowance);
                var ccComm  = group.Sum(i => i.CommunicationAllowance);
                var ccFix   = group.Sum(i => i.FixedAllowance);
                var ccBonus = group.Sum(i => i.BonusAmount);
                var ccDed   = group.Sum(i => i.DeductionAmount); // Absence is handled via ccBasic netting

                if (ccBasic > 0)
                {
                    je.Lines.Add(new JournalLine { AccountId = wagesAccId, Debit = ccBasic, Description = _t.Get("HR.BasicSalaryDesc", cc, run.PeriodMonth, run.PeriodYear), CostCenter = cc });
                }
                else if (ccBasic < 0)
                {
                    je.Lines.Add(new JournalLine { AccountId = wagesAccId, Credit = Math.Abs(ccBasic), Description = _t.Get("HR.BasicSalaryDesc", cc, run.PeriodMonth, run.PeriodYear), CostCenter = cc });
                }

                if (ccOvertime > 0)
                {
                    je.Lines.Add(new JournalLine { AccountId = overtimeAccId, Debit = ccOvertime, Description = _t.Get("HR.OvertimeDesc", cc, run.PeriodMonth, run.PeriodYear), CostCenter = cc });
                }
                
                if (ccTrans > 0)
                {
                    je.Lines.Add(new JournalLine { AccountId = transAccId, Debit = ccTrans, Description = _t.Get("HR.TransAllowanceDesc", cc, run.PeriodMonth, run.PeriodYear), CostCenter = cc });
                }
                if (ccComm > 0)
                {
                    je.Lines.Add(new JournalLine { AccountId = commAccId, Debit = ccComm, Description = _t.Get("HR.CommAllowanceDesc", cc, run.PeriodMonth, run.PeriodYear), CostCenter = cc });
                }
                if (ccFix > 0)
                {
                    je.Lines.Add(new JournalLine { AccountId = fixAllAccId, Debit = ccFix, Description = _t.Get("HR.FixedAllowanceDesc", cc, run.PeriodMonth, run.PeriodYear), CostCenter = cc });
                }
                if (ccBonus > 0)
                {
                    je.Lines.Add(new JournalLine { AccountId = bonusAccId, Debit = ccBonus, Description = _t.Get("HR.BonusDesc", cc, run.PeriodMonth, run.PeriodYear), CostCenter = cc });
                }
                if (ccDed > 0)
                {
                    je.Lines.Add(new JournalLine { AccountId = dedAccId, Credit = ccDed, Description = _t.Get("HR.DeductionDesc", cc, run.PeriodMonth, run.PeriodYear), CostCenter = cc });
                }
            }

            foreach (var item in run.Items)
            {
                var employeeCC = item.Employee?.CostCenter ?? OrderSource.General;

                var grossEarnings = item.BasicSalary + item.TransportationAllowance + item.CommunicationAllowance + item.FixedAllowance + item.BonusAmount + item.OvertimeAmount;
                if (grossEarnings > 0)
                {
                    je.Lines.Add(new JournalLine
                    {
                        AccountId   = accrualAccId,
                        Debit       = 0,
                        Credit      = grossEarnings,
                        Description = _t.Get("HR.GrossEarningsDesc", item.Employee?.Name ?? "", run.PeriodMonth, run.PeriodYear),
                        EmployeeId  = item.EmployeeId,
                        CostCenter  = employeeCC
                    });
                }

                if (item.AdvanceDeducted > 0)
                {
                    je.Lines.Add(new JournalLine
                    {
                        AccountId   = accrualAccId,
                        Debit       = item.AdvanceDeducted,
                        Credit      = 0,
                        Description = _t.Get("HR.AdvanceDeductionDesc", item.Employee?.Name ?? "", run.PeriodMonth, run.PeriodYear),
                        EmployeeId  = item.EmployeeId,
                        CostCenter  = employeeCC
                    });
                    
                    je.Lines.Add(new JournalLine
                    {
                        AccountId   = advAccId,
                        Debit       = 0,
                        Credit      = item.AdvanceDeducted,
                        Description = _t.Get("HR.AdvanceSettlementDesc", item.Employee?.Name ?? "", run.PeriodMonth, run.PeriodYear),
                        CostCenter  = employeeCC
                    });
                }

                if (item.DeductionAmount > 0)
                {
                    je.Lines.Add(new JournalLine
                    {
                        AccountId   = accrualAccId,
                        Debit       = item.DeductionAmount,
                        Credit      = 0,
                        Description = _t.Get("HR.DeductionLogDesc", item.Employee?.Name ?? "", run.PeriodMonth, run.PeriodYear),
                        EmployeeId  = item.EmployeeId,
                        CostCenter  = employeeCC
                    });
                }
                
                if (item.AbsenceDeduction > 0)
                {
                    je.Lines.Add(new JournalLine
                    {
                        AccountId   = accrualAccId,
                        Debit       = item.AbsenceDeduction,
                        Credit      = 0,
                        Description = _t.Get("HR.AbsenceDeductionDesc", item.Employee?.Name ?? "", run.PeriodMonth, run.PeriodYear),
                        EmployeeId  = item.EmployeeId,
                        CostCenter  = employeeCC
                    });
                }
            }

            _db.JournalEntries.Add(je);

            foreach (var item in run.Items.Where(i => i.AdvanceDeducted > 0))
            {
                var pendingAdvances = await _db.EmployeeAdvances
                    .Where(a => a.EmployeeId == item.EmployeeId && a.Status != AdvanceStatus.FullyDeducted)
                    .OrderBy(a => a.AdvanceDate)
                    .ToListAsync();

                var remaining = item.AdvanceDeducted;
                foreach (var adv in pendingAdvances)
                {
                    if (remaining <= 0) break;
                    var canDeduct = Math.Min(adv.RemainingAmount, remaining);
                    adv.DeductedAmount += canDeduct;
                    adv.Status = adv.DeductedAmount >= adv.Amount
                        ? AdvanceStatus.FullyDeducted
                        : AdvanceStatus.PartiallyDeducted;
                    remaining -= canDeduct;
                    adv.UpdatedAt = TimeHelper.GetEgyptTime();
                }
            }

            var empIds = run.Items.Select(i => i.EmployeeId).ToList();
            
            var pendingBonuses = await _db.EmployeeBonuses
                .Where(b => empIds.Contains(b.EmployeeId) && b.PayrollRunId == null && b.CashAccountId == null)
                .ToListAsync();
            foreach (var b in pendingBonuses) b.PayrollRunId = run.Id;

            var pendingDeductions = await _db.EmployeeDeductions
                .Where(d => empIds.Contains(d.EmployeeId) && d.PayrollRunId == null && d.CashAccountId == null)
                .ToListAsync();
            foreach (var d in pendingDeductions) d.PayrollRunId = run.Id;
        }

        run.Status    = PayrollStatus.Posted;
        run.UpdatedAt = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        if (je != null) { run.JournalEntryId = je.Id; await _db.SaveChangesAsync(); }

        return Ok(new { id = run.Id, payrollNumber = run.PayrollNumber, journalEntryId = je?.Id });
    }

    [HttpPost("{id}/pay")]
    public async Task<IActionResult> Pay(int id, [FromBody] PayPayrollDto dto)
    {
        var run = await _db.PayrollRuns
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (run == null) return NotFound();
        if (run.Status != PayrollStatus.Posted)
            return BadRequest(new { message = _t.Get("HR.PayrollMustBePostedToPay") });

        var mapDict = await _core.GetSafeSystemMappingsAsync();
        var accrualAccId = run.AccruedSalariesAccountId ?? await _core.GetRequiredMappedAccountAsync(MappingKeys.SalariesPayable, mapDict);
        var cashAcc = await _db.Accounts.FindAsync(dto.CashAccountId);
        
        if (cashAcc == null) return BadRequest(new { message = _t.Get("Accounting.PaymentVoucher.AccountNotFound") });

        var jeNo = await _seq.NextAsync("JE");
        var now = TimeHelper.GetEgyptTime();
        var payJe = new JournalEntry
        {
            EntryNumber     = jeNo,
            EntryDate       = dto.PaymentDate.Date.Add(now.TimeOfDay),
            Type            = JournalEntryType.PaymentVoucher,
            Status          = JournalEntryStatus.Posted,
            Description     = _t.Get("HR.PayrollPaymentDescription", run.PayrollNumber, run.PeriodMonth, run.PeriodYear),
            Reference       = run.PayrollNumber,
            CreatedByUserId = UserId,
            CreatedAt       = now,
            Lines           = new List<JournalLine>()
        };

        // 1. Debit Accrued Salaries (Clear Liability) - Distributed by Employee
        foreach (var item in run.Items.Where(i => i.NetPayable > 0))
        {
            payJe.Lines.Add(new JournalLine
            {
                AccountId = accrualAccId,
                Debit     = item.NetPayable,
                Credit    = 0,
                Description = _t.Get("HR.PayrollPaymentLineDescription", item.Employee?.Name ?? "", run.PayrollNumber),
                EmployeeId  = item.EmployeeId,
                CostCenter  = item.Employee?.CostCenter ?? OrderSource.General
            });
        }

        // 2. Credit Cash/Bank (Release Money)
        payJe.Lines.Add(new JournalLine
        {
            AccountId = cashAcc.Id,
            Debit     = 0,
            Credit    = run.TotalNetPayable,
            Description = _t.Get("HR.PayrollPaymentFrom", cashAcc.NameAr)
        });

        _db.JournalEntries.Add(payJe);
        await _db.SaveChangesAsync();

        run.Status = PayrollStatus.Paid;
        run.PaymentJournalEntryId = payJe.Id;
        run.UpdatedAt = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();

        return Ok(new { id = run.Id, status = (int)run.Status, paymentJournalEntryId = payJe.Id });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var run = await _db.PayrollRuns.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
        if (run == null) return NotFound();

        bool isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        
        if (run.Status == PayrollStatus.Posted && !isAdmin)
            return BadRequest(new { message = _t.Get("HR.PayrollCannotDelete") });

        if (run.Status == PayrollStatus.Posted)
        {
            if (run.JournalEntryId.HasValue)
            {
                var je = await _db.JournalEntries.FindAsync(run.JournalEntryId.Value);
                if (je != null) _db.JournalEntries.Remove(je);
            }

            foreach (var item in run.Items.Where(i => i.AdvanceDeducted > 0))
            {
                var advances = await _db.EmployeeAdvances
                    .Where(a => a.EmployeeId == item.EmployeeId && a.DeductedAmount > 0)
                    .OrderByDescending(a => a.AdvanceDate) // عكس الحركات من الأحدث للأقدم
                    .ToListAsync();

                var toRestore = item.AdvanceDeducted;
                foreach (var adv in advances)
                {
                    if (toRestore <= 0) break;
                    var restored = Math.Min(adv.DeductedAmount, toRestore);
                    adv.DeductedAmount -= restored;
                    
                    if (adv.DeductedAmount <= 0) adv.Status = AdvanceStatus.Pending;
                    else if (adv.DeductedAmount < adv.Amount) adv.Status = AdvanceStatus.PartiallyDeducted;
                    else adv.Status = AdvanceStatus.FullyDeducted;

                    toRestore -= restored;
                    adv.UpdatedAt = TimeHelper.GetEgyptTime();
                }
            }

            var bonuses = await _db.EmployeeBonuses.Where(b => b.PayrollRunId == run.Id).ToListAsync();
            foreach (var b in bonuses) b.PayrollRunId = null;

            var deductions = await _db.EmployeeDeductions.Where(d => d.PayrollRunId == run.Id).ToListAsync();
            foreach (var d in deductions) d.PayrollRunId = null;
        }

        _db.PayrollRuns.Remove(run);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static PayrollRunDto ToDto(PayrollRun run) => new(
        run.Id, run.PayrollNumber, run.PeriodYear, run.PeriodMonth,
        run.TotalBasicSalary, run.TotalTransportation, run.TotalCommunication, run.TotalBonuses,
        run.TotalFixedAllowances,
        run.TotalOvertimeAmount,
        run.TotalDeductions + run.TotalAbsenceDeduction,
        run.TotalAdvancesDeducted, run.TotalNetPayable, (int)run.Status, run.Notes,
        run.JournalEntryId, run.PaymentJournalEntryId, run.CreatedAt,
        run.Items.Select(i => new PayrollItemDto(
            i.Id, i.EmployeeId, i.Employee.Name, i.Employee.EmployeeNumber,
            i.Employee.JobTitle, i.BasicSalary, i.TransportationAllowance, i.CommunicationAllowance, i.BonusAmount,
            i.FixedAllowance,
            i.DeductionAmount, i.AdvanceDeducted, i.AbsenceDays, i.AbsenceDeduction, i.OvertimeHours, i.OvertimeAmount, i.CommissionAmount, i.NetPayable, i.Notes
        )).ToList()
    );
}

// 3. ADVANCES 

[ApiController]
[Route("api/employee-advances")]
[RequirePermission(ModuleKeys.HrPayroll)]
public class EmployeeAdvancesController : ControllerBase
{
    private readonly AppDbContext    _db;
    private readonly SequenceService _seq;
    private readonly IAccountingService _accounting;
    private readonly AccountingCoreService _core;
    private readonly ITranslator _t;

    public EmployeeAdvancesController(AppDbContext db, SequenceService seq, IAccountingService accounting, AccountingCoreService core, ITranslator t)
        => (_db, _seq, _accounting, _core, _t) = (db, seq, accounting, core, t);
    private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? employeeId = null,
        [FromQuery] AdvanceStatus? status = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.EmployeeAdvances
            .Include(a => a.Employee)
            .Include(a => a.CashAccount)
            .AsQueryable();

        if (employeeId.HasValue) q = q.Where(a => a.EmployeeId == employeeId.Value);
        if (status.HasValue)     q = q.Where(a => a.Status == status.Value);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(a => a.AdvanceDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new EmployeeAdvanceDto(
                a.Id, a.AdvanceNumber, a.EmployeeId, a.Employee.Name,
                a.AdvanceDate, a.Amount, a.DeductedAmount, a.Amount - a.DeductedAmount,
                (int)a.Status, a.Reason, a.Notes,
                a.CashAccountId, a.CashAccount != null ? a.CashAccount.NameAr : null,
                a.JournalEntryId, a.CreatedAt, a.CostCenter
            )).ToListAsync();

        return Ok(new PaginatedResult<EmployeeAdvanceDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpPost]
    [RequirePermission(ModuleKeys.Hr, requireEdit: true)]
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
                 if (je != null) _db.JournalEntries.Remove(je);
             }
             _db.PaymentVouchers.Remove(voucher);
        }

        _db.EmployeeAdvances.Remove(adv);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{id}")]
    [RequirePermission(ModuleKeys.Hr, requireEdit: true)]
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
        return NoContent();
    }
}

// 4. BONUSES 
[ApiController]
[Route("api/employee-bonuses")]
[RequirePermission(ModuleKeys.HrPayroll)]
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
    [RequirePermission(ModuleKeys.Hr, requireEdit: true)]
    public async Task<IActionResult> Create([FromBody] CreateBonusDto dto)
    {
        var emp = await _db.Employees.FindAsync(dto.EmployeeId);
        if (emp == null) return NotFound();

        var mapDict = await _core.GetSafeSystemMappingsAsync();

        // Ã°Å¸Å½Â¯ UNIFIED VOUCHER SYSTEM: Create a PaymentVoucher record for this bonus (if cash disbursement)
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
                 if (je != null) _db.JournalEntries.Remove(je);
             }
             _db.PaymentVouchers.Remove(voucher);
        }

        _db.EmployeeBonuses.Remove(bon);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{id}")]
    [RequirePermission(ModuleKeys.Hr, requireEdit: true)]
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

// 5. DEDUCTIONS 

[ApiController]
[Route("api/employee-deductions")]
[RequirePermission(ModuleKeys.HrPayroll)]
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

        // Ã°Å¸Å½Â¯ UNIFIED VOUCHER SYSTEM: Create a ReceiptVoucher record for this deduction (if cash)
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
    [RequirePermission(ModuleKeys.Hr, requireEdit: true)]
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

// 6. DEPARTMENTS 

[ApiController]
[Route("api/departments")]
[RequirePermission(ModuleKeys.HrPayroll)]
public class DepartmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITranslator _t;
    public DepartmentsController(AppDbContext db, ITranslator t) { _db = db; _t = t; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DepartmentDto>>> GetDepartments()
    {
        return await _db.Departments
            .Include(d => d.ParentDepartment)
            .Include(d => d.Manager)
            .Select(d => new DepartmentDto(
                d.Id, 
                d.Name, 
                d.Description, 
                d.Employees.Count, 
                d.WorkHoursPerDay, 
                d.OvertimeMultiplier, 
                d.DaysPerMonth,
                d.ParentDepartmentId,
                d.ParentDepartment != null ? d.ParentDepartment.Name : null,
                d.ManagerEmployeeId,
                d.Manager != null ? d.Manager.Name : null
            ))
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<DepartmentDto>> CreateDepartment(CreateDepartmentDto dto)
    {
        var dept = new Department 
        { 
            Name = dto.Name, 
            Description = dto.Description,
            WorkHoursPerDay = dto.WorkHoursPerDay,
            OvertimeMultiplier = dto.OvertimeMultiplier,
            DaysPerMonth = dto.DaysPerMonth,
            ParentDepartmentId = dto.ParentDepartmentId,
            ManagerEmployeeId = dto.ManagerEmployeeId
        };
        _db.Departments.Add(dept);
        await _db.SaveChangesAsync();
        
        return Ok(new DepartmentDto(
            dept.Id, 
            dept.Name, 
            dept.Description, 
            0, 
            dept.WorkHoursPerDay, 
            dept.OvertimeMultiplier, 
            dept.DaysPerMonth,
            dept.ParentDepartmentId,
            null,
            dept.ManagerEmployeeId,
            null
        ));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateDepartment(int id, CreateDepartmentDto dto)
    {
        var dept = await _db.Departments.FindAsync(id);
        if (dept == null) return NotFound();

        dept.Name = dto.Name;
        dept.Description = dto.Description;
        dept.WorkHoursPerDay = dto.WorkHoursPerDay;
        dept.OvertimeMultiplier = dto.OvertimeMultiplier;
        dept.DaysPerMonth = dto.DaysPerMonth;
        dept.ParentDepartmentId = dto.ParentDepartmentId;
        dept.ManagerEmployeeId = dto.ManagerEmployeeId;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id}/apply-to-employees")]
    public async Task<IActionResult> BulkUpdateEmployeesFromDepartment(int id)
    {
        var dept = await _db.Departments.Include(d => d.Employees).FirstOrDefaultAsync(d => d.Id == id);
        if (dept == null) return NotFound();

        foreach (var emp in dept.Employees)
        {
            emp.WorkHoursPerDay = dept.WorkHoursPerDay;
            emp.OvertimeMultiplier = dept.OvertimeMultiplier;
            emp.DaysPerMonth = dept.DaysPerMonth;
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = $"Settings applied to {dept.Employees.Count} employees." });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var dept = await _db.Departments.Include(d => d.Employees).FirstOrDefaultAsync(d => d.Id == id);
        if (dept == null) return NotFound();
        if (dept.Employees.Any()) return BadRequest(new { message = _t.Get("HR.DepartmentHasEmployees") });
        
        var hasSubDepts = await _db.Departments.AnyAsync(d => d.ParentDepartmentId == id);
        if (hasSubDepts) return BadRequest(new { message = "لا يمكن حذف قسم يحتوي على أقسام فرعية." });

        _db.Departments.Remove(dept);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

// 7. COMMISSIONS

[ApiController]
[Route("api/commissions")]
[RequirePermission(ModuleKeys.HrPayroll)]
public class EmployeeCommissionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITranslator _t;
    public EmployeeCommissionsController(AppDbContext db, ITranslator t) { _db = db; _t = t; }

    [HttpGet("{employeeId}")]
    public async Task<ActionResult<CommissionSettingDto>> GetCommissionSetting(int employeeId)
    {
        var setting = await _db.EmployeeCommissionSettings
            .Include(s => s.Tiers)
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId);

        if (setting == null)
        {
            return Ok(new CommissionSettingDto(0, employeeId, CommissionType.PercentageOfSales, CommissionBasis.NetSales, 0, 0, new List<CommissionTierDto>()));
        }

        return Ok(new CommissionSettingDto(
            setting.Id,
            setting.EmployeeId,
            setting.Type,
            setting.Basis,
            setting.DefaultRate,
            setting.TargetAmount,
            setting.Tiers.Select(t => new CommissionTierDto(t.Id, t.MinAmount, t.MaxAmount, t.Rate)).ToList()
        ));
    }

    [HttpPut("{employeeId}")]
    public async Task<IActionResult> UpdateCommissionSetting(int employeeId, UpdateCommissionSettingDto dto)
    {
        var setting = await _db.EmployeeCommissionSettings
            .Include(s => s.Tiers)
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId);

        if (setting == null)
        {
            setting = new EmployeeCommissionSetting { EmployeeId = employeeId, CreatedAt = TimeHelper.GetEgyptTime() };
            _db.EmployeeCommissionSettings.Add(setting);
        }

        setting.Type = dto.Type;
        setting.Basis = dto.Basis;
        setting.DefaultRate = dto.DefaultRate;
        setting.TargetAmount = dto.TargetAmount;
        setting.CommissionSchemeId = dto.CommissionSchemeId;

        // Update Tiers
        _db.CommissionTiers.RemoveRange(setting.Tiers);
        setting.Tiers = dto.Tiers.Select(t => new CommissionTier
        {
            MinAmount = t.MinAmount,
            MaxAmount = t.MaxAmount,
            Rate = t.Rate,
            CreatedAt = TimeHelper.GetEgyptTime()
        }).ToList();

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("summary")]
    public async Task<ActionResult<IEnumerable<EmployeeCommissionSummaryDto>>> GetCommissionsSummary()
    {
        var startOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        
        var orders = await _db.Orders
            .Include(o => o.Items)
            .Where(o => o.CreatedAt >= startOfMonth && o.Status != OrderStatus.Cancelled)
            .ToListAsync();

        var employees = await _db.Employees
            .Include(e => e.Department)
            .Include(e => e.CommissionSetting)
            .ThenInclude(s => s != null ? s.Tiers : null)
            .ToListAsync();

        var groups = await _db.CommissionGroups
            .Include(g => g.Members)
            .Include(g => g.Tiers)
            .Include(g => g.CommissionScheme)
            .ThenInclude(s => s != null ? s.Tiers : null)
            .ToListAsync();

        var employeeGroupCommissions = new Dictionary<int, decimal>();
        var employeeGroupSales = new Dictionary<int, decimal>();
        var employeeGroupName = new Dictionary<int, string>();

        foreach (var g in groups)
        {
            var memberUserIds = g.Members.Select(m => m.AppUserId).Where(id => id != null).ToList();
            var memberIds = g.Members.Select(m => m.Id.ToString()).ToList();
            
            var groupOrders = orders.Where(o => 
                memberUserIds.Contains(o.SalesPersonId) || 
                memberIds.Contains(o.SalesPersonId)
            ).ToList();
            
            var scheme = g.CommissionSchemeId != null 
                ? await _db.CommissionSchemes.Include(s => s.Tiers).FirstOrDefaultAsync(s => s.Id == g.CommissionSchemeId)
                : null;

            var basis = scheme != null ? scheme.Basis : g.Basis;
            var type = scheme != null ? scheme.Type : g.Type;
            var defaultRate = scheme != null ? scheme.DefaultRate : g.DefaultRate;
            var targetAmount = scheme != null ? scheme.TargetAmount : g.TargetAmount;
            var tiersList = scheme != null 
                ? scheme.Tiers.Select(t => new { t.MinAmount, t.MaxAmount, t.Rate }).ToList() 
                : g.Tiers.Select(t => new { t.MinAmount, t.MaxAmount, t.Rate }).ToList();

            var returnsAmount = groupOrders.Sum(o => o.Status == OrderStatus.Returned ? o.TotalAmount : o.Items.Sum(i => i.Quantity > 0 ? (i.TotalPrice / i.Quantity) * i.ReturnedQuantity : 0));

            decimal relevantSales = basis == CommissionBasis.NetSales 
                ? groupOrders.Sum(o => o.TotalAmount) - returnsAmount
                : groupOrders.Sum(o => o.SubTotal) - returnsAmount;

            decimal earnedCommission = 0;

            if (type == CommissionType.TargetAchievementTiers || relevantSales >= targetAmount)
            {
                if (type == CommissionType.PercentageOfSales)
                {
                    earnedCommission = relevantSales * (defaultRate / 100);
                }
                else if (type == CommissionType.FixedAmountPerItem)
                {
                    var orderIds = groupOrders.Select(o => o.Id).ToList();
                    var itemsCount = await _db.OrderItems
                        .Where(oi => orderIds.Contains(oi.OrderId))
                        .SumAsync(oi => oi.Quantity);
                    
                    earnedCommission = itemsCount * defaultRate;
                }
                else if (type == CommissionType.TieredPercentage)
                {
                    var sortedTiers = tiersList.OrderBy(t => t.MinAmount).ToList();
                    var applicableTier = sortedTiers.LastOrDefault(t => relevantSales >= t.MinAmount && relevantSales <= t.MaxAmount);
                    
                    if (applicableTier != null)
                    {
                        earnedCommission = relevantSales * (applicableTier.Rate / 100);
                    }
                    else
                    {
                        var lastTier = sortedTiers.LastOrDefault();
                        if (lastTier != null && relevantSales > lastTier.MaxAmount)
                        {
                            earnedCommission = relevantSales * (lastTier.Rate / 100);
                        }
                        else
                        {
                            earnedCommission = relevantSales * (defaultRate / 100);
                        }
                    }
                }
                else if (type == CommissionType.TargetAchievementTiers)
                {
                    var sortedTiers = tiersList.OrderBy(t => t.MinAmount).ToList();
                    decimal achievementPercentage = targetAmount > 0 ? (relevantSales / targetAmount) * 100 : 0;
                    var applicableTier = sortedTiers.LastOrDefault(t => achievementPercentage >= t.MinAmount && achievementPercentage <= t.MaxAmount);
                    
                    if (applicableTier != null)
                    {
                        earnedCommission = relevantSales * (applicableTier.Rate / 100);
                    }
                    else
                    {
                        var lastTier = sortedTiers.LastOrDefault();
                        if (lastTier != null && achievementPercentage > lastTier.MaxAmount)
                        {
                            earnedCommission = relevantSales * (lastTier.Rate / 100);
                        }
                        else
                        {
                            earnedCommission = relevantSales * (defaultRate / 100);
                        }
                    }
                }
            }

            if (g.Members.Any())
            {
                var share = earnedCommission / g.Members.Count;
                var salesShare = relevantSales;
                
                foreach (var m in g.Members)
                {
                    employeeGroupCommissions[m.Id] = share;
                    employeeGroupSales[m.Id] = salesShare;
                    employeeGroupName[m.Id] = g.Name;
                }
            }
        }

        var result = new List<EmployeeCommissionSummaryDto>();

        foreach (var e in employees)
        {
            decimal earnedCommission = 0;
            decimal relevantSales = 0;
            bool isGroup = false;
            string? groupName = null;
            
            if (employeeGroupCommissions.TryGetValue(e.Id, out var groupComm))
            {
                earnedCommission = groupComm;
                relevantSales = employeeGroupSales.GetValueOrDefault(e.Id, 0);
                isGroup = true;
                groupName = employeeGroupName.GetValueOrDefault(e.Id);
                
                result.Add(new EmployeeCommissionSummaryDto(
                    e.Id, e.Name, e.JobTitle, null, 
                    CommissionType.PercentageOfSales, CommissionBasis.NetSales, 0, 0,
                    relevantSales, earnedCommission,
                    e.DepartmentId, e.Department?.Name,
                    isGroup, groupName
                ));
            }
            else if (e.CommissionSetting != null)
            {
                var empOrders = orders.Where(o => 
                    o.SalesPersonId == e.AppUserId || 
                    o.SalesPersonId == e.Id.ToString()
                ).ToList();

                var scheme = e.CommissionSetting.CommissionSchemeId != null 
                    ? await _db.CommissionSchemes.Include(s => s.Tiers).FirstOrDefaultAsync(s => s.Id == e.CommissionSetting.CommissionSchemeId)
                    : null;

                var basis = scheme != null ? scheme.Basis : e.CommissionSetting.Basis;
                var type = scheme != null ? scheme.Type : e.CommissionSetting.Type;
                var defaultRate = scheme != null ? scheme.DefaultRate : e.CommissionSetting.DefaultRate;
                var targetAmount = scheme != null ? scheme.TargetAmount : e.CommissionSetting.TargetAmount;
                var tiersList = scheme != null 
                    ? scheme.Tiers.Select(t => new { t.MinAmount, t.MaxAmount, t.Rate }).ToList() 
                    : e.CommissionSetting.Tiers.Select(t => new { t.MinAmount, t.MaxAmount, t.Rate }).ToList();

                var returnsAmount = empOrders.Sum(o => o.Status == OrderStatus.Returned ? o.TotalAmount : o.Items.Sum(i => i.Quantity > 0 ? (i.TotalPrice / i.Quantity) * i.ReturnedQuantity : 0));

                relevantSales = basis == CommissionBasis.NetSales 
                    ? empOrders.Sum(o => o.TotalAmount) - returnsAmount
                    : empOrders.Sum(o => o.SubTotal) - returnsAmount;

                if (type == CommissionType.TargetAchievementTiers || relevantSales >= targetAmount)
                {
                    if (type == CommissionType.PercentageOfSales)
                    {
                        earnedCommission = relevantSales * (defaultRate / 100);
                    }
                    else if (type == CommissionType.FixedAmountPerItem)
                    {
                        var orderIds = empOrders.Select(o => o.Id).ToList();
                        var itemsCount = await _db.OrderItems
                            .Where(oi => orderIds.Contains(oi.OrderId))
                            .SumAsync(oi => oi.Quantity);
                        
                        earnedCommission = itemsCount * defaultRate;
                    }
                    else if (type == CommissionType.TieredPercentage)
                    {
                        var sortedTiers = tiersList.OrderBy(t => t.MinAmount).ToList();
                        var applicableTier = sortedTiers.LastOrDefault(t => relevantSales >= t.MinAmount && relevantSales <= t.MaxAmount);
                        
                        if (applicableTier != null)
                        {
                            earnedCommission = relevantSales * (applicableTier.Rate / 100);
                        }
                        else
                        {
                            var lastTier = sortedTiers.LastOrDefault();
                            if (lastTier != null && relevantSales > lastTier.MaxAmount)
                            {
                                earnedCommission = relevantSales * (lastTier.Rate / 100);
                            }
                            else
                            {
                                earnedCommission = relevantSales * (defaultRate / 100);
                            }
                        }
                    }
                    else if (type == CommissionType.TargetAchievementTiers)
                    {
                        var sortedTiers = tiersList.OrderBy(t => t.MinAmount).ToList();
                        decimal achievementPercentage = targetAmount > 0 ? (relevantSales / targetAmount) * 100 : 0;
                        var applicableTier = sortedTiers.LastOrDefault(t => achievementPercentage >= t.MinAmount && achievementPercentage <= t.MaxAmount);
                        
                        if (applicableTier != null)
                        {
                            earnedCommission = relevantSales * (applicableTier.Rate / 100);
                        }
                        else
                        {
                            var lastTier = sortedTiers.LastOrDefault();
                            if (lastTier != null && achievementPercentage > lastTier.MaxAmount)
                            {
                                earnedCommission = relevantSales * (lastTier.Rate / 100);
                            }
                            else
                            {
                                earnedCommission = relevantSales * (defaultRate / 100);
                            }
                        }
                    }
                }

                result.Add(new EmployeeCommissionSummaryDto(
                    e.Id,
                    e.Name,
                    e.JobTitle,
                    e.CommissionSetting?.CommissionSchemeId,
                    type,
                    basis,
                    defaultRate,
                    targetAmount,
                    relevantSales,
                    earnedCommission,
                    e.DepartmentId,
                    e.Department?.Name
                ));
            }
            else
            {
                result.Add(new EmployeeCommissionSummaryDto(
                    e.Id,
                    e.Name,
                    e.JobTitle,
                    null,
                    CommissionType.PercentageOfSales,
                    CommissionBasis.NetSales,
                    0,
                    0,
                    0,
                    0,
                    e.DepartmentId,
                    e.Department?.Name
                ));
            }
        }

        return Ok(result);
    }
}

[ApiController]
[Route("api/commissions/schemes")]
[RequirePermission(ModuleKeys.HrPayroll)]
public class CommissionSchemesController : ControllerBase
{
    private readonly AppDbContext _db;
    public CommissionSchemesController(AppDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CommissionSchemeDto>>> GetSchemes()
    {
        var schemes = await _db.CommissionSchemes
            .Include(s => s.Tiers)
            .Select(s => new CommissionSchemeDto(
                s.Id,
                s.Name,
                s.Type,
                s.Basis,
                s.DefaultRate,
                s.TargetAmount,
                s.Tiers.Select(t => new CommissionTierDto(t.Id, t.MinAmount, t.MaxAmount, t.Rate)).ToList()
            ))
            .ToListAsync();

        return Ok(schemes);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CommissionSchemeDto>> GetScheme(int id)
    {
        var s = await _db.CommissionSchemes
            .Include(s => s.Tiers)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (s == null) return NotFound();

        return Ok(new CommissionSchemeDto(
            s.Id,
            s.Name,
            s.Type,
            s.Basis,
            s.DefaultRate,
            s.TargetAmount,
            s.Tiers.Select(t => new CommissionTierDto(t.Id, t.MinAmount, t.MaxAmount, t.Rate)).ToList()
        ));
    }

    [HttpPost]
    public async Task<ActionResult<CommissionSchemeDto>> CreateScheme(UpdateCommissionSchemeDto dto)
    {
        var s = new CommissionScheme
        {
            Name = dto.Name,
            Type = dto.Type,
            Basis = dto.Basis,
            DefaultRate = dto.DefaultRate,
            TargetAmount = dto.TargetAmount,
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        s.Tiers = dto.Tiers.Select(t => new CommissionSchemeTier
        {
            MinAmount = t.MinAmount,
            MaxAmount = t.MaxAmount,
            Rate = t.Rate,
            CreatedAt = TimeHelper.GetEgyptTime()
        }).ToList();

        _db.CommissionSchemes.Add(s);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetScheme), new { id = s.Id }, new CommissionSchemeDto(
            s.Id,
            s.Name,
            s.Type,
            s.Basis,
            s.DefaultRate,
            s.TargetAmount,
            s.Tiers.Select(t => new CommissionTierDto(t.Id, t.MinAmount, t.MaxAmount, t.Rate)).ToList()
        ));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateScheme(int id, UpdateCommissionSchemeDto dto)
    {
        var s = await _db.CommissionSchemes
            .Include(s => s.Tiers)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (s == null) return NotFound();

        s.Name = dto.Name;
        s.Type = dto.Type;
        s.Basis = dto.Basis;
        s.DefaultRate = dto.DefaultRate;
        s.TargetAmount = dto.TargetAmount;

        s.Tiers.Clear();
        foreach (var t in dto.Tiers)
        {
            s.Tiers.Add(new CommissionSchemeTier
            {
                MinAmount = t.MinAmount,
                MaxAmount = t.MaxAmount,
                Rate = t.Rate,
                CreatedAt = TimeHelper.GetEgyptTime()
            });
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteScheme(int id)
    {
        var s = await _db.CommissionSchemes.FindAsync(id);
        if (s == null) return NotFound();

        // Delete settings for employees linked to this scheme
        var linkedSettings = await _db.EmployeeCommissionSettings
            .Where(ecs => ecs.CommissionSchemeId == id)
            .ToListAsync();
        _db.EmployeeCommissionSettings.RemoveRange(linkedSettings);

        _db.CommissionSchemes.Remove(s);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

[ApiController]
[Route("api/commissions/groups")]
[RequirePermission(ModuleKeys.HrPayroll)]
public class CommissionGroupsController : ControllerBase
{
    private readonly AppDbContext _db;
    public CommissionGroupsController(AppDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CommissionGroupDto>>> GetGroups()
    {
        var groups = await _db.CommissionGroups
            .Include(g => g.Members)
            .ToListAsync();

        var result = groups.Select(g => new CommissionGroupDto(
            g.Id,
            g.Name,
            g.Description,
            g.CommissionSchemeId,
            g.Members.Select(m => m.Id).ToList()
        )).ToList();

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CommissionGroupDto>> GetGroup(int id)
    {
        var g = await _db.CommissionGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (g == null) return NotFound();

        return Ok(new CommissionGroupDto(
            g.Id,
            g.Name,
            g.Description,
            g.CommissionSchemeId,
            g.Members.Select(m => m.Id).ToList()
        ));
    }

    [HttpPost]
    public async Task<ActionResult<CommissionGroupDto>> CreateGroup(CreateCommissionGroupDto dto)
    {
        var g = new CommissionGroup
        {
            Name = dto.Name,
            Description = dto.Description,
            CommissionSchemeId = dto.CommissionSchemeId,
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        if (dto.MemberIds != null && dto.MemberIds.Any())
        {
            var members = await _db.Employees.Where(e => dto.MemberIds.Contains(e.Id)).ToListAsync();
            g.Members = members;
        }

        _db.CommissionGroups.Add(g);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetGroup), new { id = g.Id }, new CommissionGroupDto(
            g.Id,
            g.Name,
            g.Description,
            g.CommissionSchemeId,
            g.Members.Select(m => m.Id).ToList()
        ));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateGroup(int id, CreateCommissionGroupDto dto)
    {
        var g = await _db.CommissionGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (g == null) return NotFound();

        g.Name = dto.Name;
        g.Description = dto.Description;
        g.CommissionSchemeId = dto.CommissionSchemeId;
        g.UpdatedAt = TimeHelper.GetEgyptTime();

        // Update Members
        g.Members.Clear();
        if (dto.MemberIds != null && dto.MemberIds.Any())
        {
            var members = await _db.Employees.Where(e => dto.MemberIds.Contains(e.Id)).ToListAsync();
            g.Members = members;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGroup(int id)
    {
        var g = await _db.CommissionGroups.FindAsync(id);
        if (g == null) return NotFound();

        _db.CommissionGroups.Remove(g);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

//DTOs 
public record LinkUserDto(string? AppUserId);
public record CommissionSettingDto(int Id, int EmployeeId, CommissionType Type, CommissionBasis Basis, decimal DefaultRate, decimal TargetAmount, List<CommissionTierDto> Tiers);
public record CommissionTierDto(int Id, decimal MinAmount, decimal MaxAmount, decimal Rate);
public record UpdateCommissionSettingDto(CommissionType Type, CommissionBasis Basis, decimal DefaultRate, decimal TargetAmount, int? CommissionSchemeId, List<CreateCommissionTierDto> Tiers);
public record CreateCommissionTierDto(decimal MinAmount, decimal MaxAmount, decimal Rate);
public record CommissionSchemeDto(int Id, string Name, CommissionType Type, CommissionBasis Basis, decimal DefaultRate, decimal TargetAmount, List<CommissionTierDto> Tiers);
public record UpdateCommissionSchemeDto(string Name, CommissionType Type, CommissionBasis Basis, decimal DefaultRate, decimal TargetAmount, List<CreateCommissionTierDto> Tiers);
public record EmployeeCommissionSummaryDto(int EmployeeId, string Name, string? JobTitle, int? CommissionSchemeId, CommissionType Type, CommissionBasis Basis, decimal DefaultRate, decimal TargetAmount, decimal TotalSales, decimal EarnedCommission, int? DepartmentId = null, string? DepartmentName = null, bool IsGroup = false, string? GroupName = null);

public record CommissionGroupDto(int Id, string Name, string? Description, int? CommissionSchemeId, List<int> MemberIds);
public record CreateCommissionGroupDto(string Name, string? Description, int? CommissionSchemeId, List<int> MemberIds);


