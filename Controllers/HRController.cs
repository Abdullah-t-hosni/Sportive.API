using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Authorization;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

// ══════════════════════════════════════════════════════
// 1. EMPLOYEES (الموظفين)
// ══════════════════════════════════════════════════════

[ApiController]
[Route("api/employees")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class EmployeesController : ControllerBase
{
    private readonly AppDbContext          _db;
    private readonly SequenceService       _seq;
    private readonly AccountingCoreService _core;

    public EmployeesController(AppDbContext db, SequenceService seq, AccountingCoreService core)
        => (_db, _seq, _core) = (db, seq, core);

    private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    [HttpGet]
    [RequireModulePermission(ModuleKeys.Hr)]
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
                e.AppUserId, e.AppUser != null ? e.AppUser.FullName : null
            )).ToListAsync();

        return Ok(new PaginatedResult<EmployeeDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpGet("basic")]
    public async Task<IActionResult> GetBasic()
    {
        var list = await _db.Employees
            .Where(e => e.Status == EmployeeStatus.Active || (int)e.Status == 0)
            .OrderBy(e => e.Name)
            .Select(e => new EmployeeBasicDto(
                e.Id, e.EmployeeNumber, e.Name, e.JobTitle, e.DepartmentId, 
                e.Department != null ? e.Department.Name : null, 
                e.BaseSalary, e.TransportationAllowance, e.CommunicationAllowance, e.BonusAmount, e.FixedAllowance,
                e.Advances.Where(a => a.Status != AdvanceStatus.FullyDeducted).Sum(a => a.Amount - a.DeductedAmount),
                e.Bonuses.Where(b => b.PayrollRunId == null && b.CashAccountId == null).Sum(b => b.Amount),
                e.Deductions.Where(d => d.PayrollRunId == null && d.CashAccountId == null).Sum(d => d.Amount),
                (int)e.Status))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id}")]
    [RequireModulePermission(ModuleKeys.Hr)]
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
            e.AppUserId, e.AppUser?.FullName));
    }

    [HttpPost]
    [RequireModulePermission(ModuleKeys.Hr, requireEdit: true)]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("اسم الموظف مطلوب.");

        if (!string.IsNullOrEmpty(dto.AppUserId))
        {
            var conflict = await _db.Employees.AnyAsync(e => e.AppUserId == dto.AppUserId);
            if (conflict) return BadRequest(new { message = "هذا الحساب مرتبط بموظف آخر بالفعل." });
        }

        var empNo = await _seq.NextAsync("EMP", async (db, pattern) =>
        {
            var max = await db.Employees
                .Where(e => EF.Functions.Like(e.EmployeeNumber, pattern))
                .Select(e => e.EmployeeNumber).ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                      .DefaultIfEmpty(0).Max();
        });

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
            Status           = EmployeeStatus.Active,
            CreatedAt        = TimeHelper.GetEgyptTime(),
            CreatedByUserId  = UserId
        };

        _db.Employees.Add(emp);
        await _db.SaveChangesAsync();
        return Ok(new { id = emp.Id, employeeNumber = emp.EmployeeNumber });
    }

    // PATCH /api/employees/{id}/link-user — ربط/فك الربط مع حساب النظام
    [HttpPatch("{id}/link-user")]
    [RequireModulePermission(ModuleKeys.Hr, requireEdit: true)]
    public async Task<IActionResult> LinkUser(int id, [FromBody] LinkUserDto dto)
    {
        var emp = await _db.Employees.FindAsync(id);
        if (emp == null) return NotFound();

        if (!string.IsNullOrEmpty(dto.AppUserId))
        {
            var userExists = await _db.Users.AnyAsync(u => u.Id == dto.AppUserId);
            if (!userExists) return BadRequest(new { message = "حساب المستخدم غير موجود." });

            var conflict = await _db.Employees
                .AnyAsync(e => e.AppUserId == dto.AppUserId && e.Id != id);
            if (conflict)
                return BadRequest(new { message = "هذا الحساب مرتبط بموظف آخر بالفعل." });
        }

        emp.AppUserId = string.IsNullOrEmpty(dto.AppUserId) ? null : dto.AppUserId;
        emp.UpdatedAt = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();

        return Ok(new { message = dto.AppUserId != null ? "تم ربط الحساب بنجاح." : "تم فك ربط الحساب." });
    }

    [HttpPut("{id}")]
    [RequireModulePermission(ModuleKeys.Hr, requireEdit: true)]
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
        emp.UpdatedAt         = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [RequireModulePermission(ModuleKeys.Hr, requireEdit: true)]
    public async Task<IActionResult> Delete(int id)
    {
        var emp = await _db.Employees
            .Include(e => e.PayrollItems)
            .Include(e => e.Advances)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (emp == null) return NotFound();
        if (emp.PayrollItems.Any() || emp.Advances.Any())
            return BadRequest("لا يمكن حذف موظف له معاملات — قم بإنهاء الخدمة بدلاً من الحذف.");

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

        if (id == 0) return await GetGeneralStatement(from, to, hrAccountIds);

        var emp = await _db.Employees.FindAsync(id);
        if (emp == null) return NotFound();
        if (emp.AccountId.HasValue) hrAccountIds.Add(emp.AccountId.Value);

        hrAccountIds = hrAccountIds.Distinct().ToList();

        var preEntries = await _db.JournalLines
            .Where(l => l.EmployeeId == id && hrAccountIds.Contains(l.AccountId) && l.JournalEntry.EntryDate < from && l.JournalEntry.Status == JournalEntryStatus.Posted)
            .Select(l => new { l.Debit, l.Credit })
            .ToListAsync();
        
        var openingBalance = preEntries.Sum(e => e.Debit - e.Credit);

        var lines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.EmployeeId == id && hrAccountIds.Contains(l.AccountId) && l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to && l.JournalEntry.Status == JournalEntryStatus.Posted)
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
            0, "الكل / ALL", "000", "GENERAL_REPORT", acc?.NameAr ?? "رواتب مستحقة موظفين",
            from, to, openingBalance, rows,
            rows.Sum(r => r.Debit), rows.Sum(r => r.Credit), runningBalance
        ));
    }
}

// ══════════════════════════════════════════════════════
// 2. PAYROLL RUNS (مسير الرواتب)
// ══════════════════════════════════════════════════════

[ApiController]
[Route("api/payroll")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class PayrollController : ControllerBase
{
    private readonly AppDbContext    _db;
    private readonly SequenceService _seq;
    private readonly AccountingCoreService _core;

    public PayrollController(AppDbContext db, SequenceService seq, AccountingCoreService core)
        => (_db, _seq, _core) = (db, seq, core);

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
                p.TotalNetPayable, p.Items.Count, (int)p.Status, p.JournalEntryId, p.CreatedAt))
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

    // POST /api/payroll — إنشاء مسير رواتب (مسودة)
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePayrollRunDto dto)
    {
        try 
        {
            if (dto == null || dto.Items == null || !dto.Items.Any()) 
                return BadRequest("يجب إضافة موظف واحد على الأقل.");

            var lang = Request.Headers["Accept-Language"].ToString().StartsWith("en") ? "en" : "ar";

            // تحقق من عدم تكرار نفس الشهر
            var existing = await _db.PayrollRuns.FirstOrDefaultAsync(p => p.PeriodYear == dto.PeriodYear && p.PeriodMonth == dto.PeriodMonth);
            if (existing != null)
            {
                return Conflict(new { 
                    message = lang == "ar" ? $"يوجد مسير رواتب لشهر {dto.PeriodMonth}/{dto.PeriodYear} مسبقاً برقم ({existing.PayrollNumber})." : $"A payroll run for {dto.PeriodMonth}/{dto.PeriodYear} already exists with number ({existing.PayrollNumber}).",
                    existingId = existing.Id
                });
            }

            var payNo = await _seq.NextAsync("PAY", async (db, pattern) =>
            {
                var max = await db.PayrollRuns
                    .Where(p => EF.Functions.Like(p.PayrollNumber, pattern))
                    .Select(p => p.PayrollNumber).ToListAsync();
                return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                          .DefaultIfEmpty(0).Max();
            });

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

            decimal totalBasic = 0, totalTrans = 0, totalComm = 0, totalBonus = 0, totalFixedAll = 0, totalDed = 0, totalAdv = 0;

            foreach (var itemDto in dto.Items)
            {
                var emp = await _db.Employees.FindAsync(itemDto.EmployeeId);
                if (emp == null) continue;

                var basic = itemDto.OverrideBasicSalary ?? emp.BaseSalary;
                var trans = itemDto.TransportationAllowance;
                var comm  = itemDto.CommunicationAllowance;
                var bonus = itemDto.BonusAmount;
                var fixAll = itemDto.FixedAllowance;

                totalBasic += basic;
                totalTrans += trans;
                totalComm  += comm;
                totalBonus += bonus;
                totalFixedAll += fixAll;
                totalDed   += itemDto.DeductionAmount;
                totalAdv   += itemDto.AdvanceDeducted;

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
                    Notes           = itemDto.Notes,
                    CreatedAt       = TimeHelper.GetEgyptTime()
                });
            }

            run.TotalBasicSalary      = totalBasic;
            run.TotalTransportation   = totalTrans;
            run.TotalCommunication    = totalComm;
            run.TotalBonuses          = totalBonus;
            run.TotalFixedAllowances  = totalFixedAll;
            run.TotalDeductions       = totalDed;
            run.TotalAdvancesDeducted = totalAdv;
            run.TotalNetPayable       = totalBasic + totalTrans + totalComm + totalBonus + totalFixedAll - totalDed - totalAdv;

            _db.PayrollRuns.Add(run);
            await _db.SaveChangesAsync();
            return Ok(new { id = run.Id, payrollNumber = run.PayrollNumber });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "خطأ داخلي في الخادم أثناء إنشاء المسير.", details = ex.Message });
        }
    }

    // POST /api/payroll/{id}/post — ترحيل المسير وتوليد القيد المحاسبي
    [HttpPost("{id}/post")]
    public async Task<IActionResult> Post(int id)
    {
        var run = await _db.PayrollRuns
            .Include(p => p.Items).ThenInclude(i => i.Employee)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (run == null) return NotFound();
        if (run.Status == PayrollStatus.Posted)
            return BadRequest("المسير مرحّل بالفعل.");

        // ── توليد القيد المحاسبي ─────────────────────────────
        var mapDict = await _core.GetSafeSystemMappingsAsync();

        var wagesAccId   = run.WagesExpenseAccountId ?? await _core.GetRequiredMappedAccountAsync(MappingKeys.SalaryExpense, mapDict);
        var accrualAccId = run.AccruedSalariesAccountId ?? await _core.GetRequiredMappedAccountAsync(MappingKeys.SalariesPayable, mapDict);
        var dedAccId     = run.DeductionRevenueAccountId ?? await _core.GetRequiredMappedAccountAsync(MappingKeys.EmployeeDeductions, mapDict);
        var advAccId     = run.AdvancesAccountId ?? await _core.GetRequiredMappedAccountAsync(MappingKeys.EmployeeAdvances, mapDict);
        
        var transAccId   = run.TotalTransportation > 0 ? await _core.GetRequiredMappedAccountAsync(MappingKeys.TransportationAllowanceExpense, mapDict) : 0;
        var commAccId    = run.TotalCommunication > 0 ? await _core.GetRequiredMappedAccountAsync(MappingKeys.CommunicationAllowanceExpense, mapDict) : 0;
        var bonusAccId   = (run.TotalBonuses > 0 || run.Items.Any(i => i.BonusAmount > 0)) ? await _core.GetRequiredMappedAccountAsync(MappingKeys.EmployeeBonuses, mapDict) : 0;
        var fixAllAccId  = run.TotalFixedAllowances > 0 ? await _core.GetRequiredMappedAccountAsync(MappingKeys.FixedAllowanceExpense, mapDict) : 0;

        JournalEntry? je = null;

        if (true) // Always proceed with strict mappings
        {
            var jeNo = await _seq.NextAsync("JE", async (db, pattern) =>
            {
                var max = await db.JournalEntries
                    .Where(e => EF.Functions.Like(e.EntryNumber, pattern))
                    .Select(e => e.EntryNumber).ToListAsync();
                return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                          .DefaultIfEmpty(0).Max();
            });

            var totalGross = run.TotalBasicSalary + run.TotalBonuses;

            je = new JournalEntry
            {
                EntryNumber     = jeNo,
                EntryDate       = TimeHelper.GetEgyptTime(),
                Type            = JournalEntryType.Payroll,
                Status          = JournalEntryStatus.Posted,
                Description     = $"مسير رواتب شهر {run.PeriodMonth}/{run.PeriodYear}",
                Reference       = run.PayrollNumber,
                CreatedByUserId = UserId,
                CreatedAt       = TimeHelper.GetEgyptTime(),
                Lines           = new List<JournalLine>()
            };

            // مدين: رواتب وأجور — الأساسي (إجمالي)
            je.Lines.Add(new JournalLine
            {
                AccountId   = wagesAccId,
                Debit       = run.TotalBasicSalary,
                Credit      = 0,
                Description = $"إجمالي الرواتب الأساسية — {run.PeriodMonth}/{run.PeriodYear}"
            });

            // مدين: بدل انتقال (إجمالي)
            if (run.TotalTransportation > 0)
                je.Lines.Add(new JournalLine
                {
                    AccountId   = transAccId,
                    Debit       = run.TotalTransportation,
                    Credit      = 0,
                    Description = $"إجمالي بدلات انتقال — {run.PeriodMonth}/{run.PeriodYear}"
                });

            // مدين: بدل اتصال (إجمالي)
            if (run.TotalCommunication > 0)
                je.Lines.Add(new JournalLine
                {
                    AccountId   = commAccId,
                    Debit       = run.TotalCommunication,
                    Credit      = 0,
                    Description = $"إجمالي بدلات اتصال — {run.PeriodMonth}/{run.PeriodYear}"
                });
            
            // مدين: بدلات ثابتة أخرى (إجمالي)
            if (run.TotalFixedAllowances > 0)
                je.Lines.Add(new JournalLine
                {
                    AccountId   = fixAllAccId,
                    Debit       = run.TotalFixedAllowances,
                    Credit      = 0,
                    Description = $"إجمالي بدلات ثابتة أخرى — {run.PeriodMonth}/{run.PeriodYear}"
                });

            // دائن: إيرادات الخصومات (إجمالي)
            if (run.TotalDeductions > 0)
                je.Lines.Add(new JournalLine
                {
                    AccountId   = dedAccId,
                    Debit       = 0,
                    Credit      = run.TotalDeductions,
                    Description = $"إجمالي خصومات الموظفين (جزاءات) — {run.PeriodMonth}/{run.PeriodYear}"
                });

            // ── تفصيل الحركات لكل موظف (المكافآت، السلف، صافي الراتب) ────────────────
            foreach (var item in run.Items)
            {
                // 1. مكافآت الموظف (مدين)
                if (item.BonusAmount > 0)
                {
                    je.Lines.Add(new JournalLine
                    {
                        AccountId   = bonusAccId,
                        Debit       = item.BonusAmount,
                        Credit      = 0,
                        Description = $"مكافأة: {item.Employee?.Name} — {run.PeriodMonth}/{run.PeriodYear}",
                        EmployeeId  = item.EmployeeId
                    });
                }

                // 2. خصم السلفة للموظف (دائن)
                if (item.AdvanceDeducted > 0)
                {
                    je.Lines.Add(new JournalLine
                    {
                        AccountId   = advAccId,
                        Debit       = 0,
                        Credit      = item.AdvanceDeducted,
                        Description = $"خصم سلفة: {item.Employee?.Name} — {run.PeriodMonth}/{run.PeriodYear}",
                        EmployeeId  = item.EmployeeId
                    });
                }

                // 3. صافي الراتب المستحق للموظف (دائن)
                if (item.NetPayable > 0)
                {
                    // نستخدم دائماً حساب الرواتب المستحقة العام لضمان التوجيه الصحيح
                    // مع الاحتفاظ بربط الموظف بالسطر للتحليل
                    je.Lines.Add(new JournalLine
                    {
                        AccountId   = accrualAccId,
                        Debit       = 0,
                        Credit      = item.NetPayable,
                        Description = $"صافي الراتب المستحق: {item.Employee?.Name} — {run.PeriodMonth}/{run.PeriodYear}",
                        EmployeeId  = item.EmployeeId
                    });
                }
            }

            _db.JournalEntries.Add(je);

            // تحديث حالة السلف المخصومة
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

            // ربط المكافآت والخصومات المعلقة بهذا المسير (لإغلاقها)
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

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var run = await _db.PayrollRuns.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
        if (run == null) return NotFound();

        bool isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        
        if (run.Status == PayrollStatus.Posted && !isAdmin)
            return BadRequest("لا يمكن حذف مسير مرحّل إلا بواسطة المدير.");

        // إذا كان مرحلاً، نحتاج لعكس كافة الحركات المالية لضمان سلامة الحسابات
        if (run.Status == PayrollStatus.Posted)
        {
            // 1. حذف القيد المحاسبي المرتبط
            if (run.JournalEntryId.HasValue)
            {
                var je = await _db.JournalEntries.FindAsync(run.JournalEntryId.Value);
                if (je != null) _db.JournalEntries.Remove(je);
            }

            // 2. استرجاع السلف المخصومة (إعادة المبالغ لأرصدة الموظفين)
            foreach (var item in run.Items.Where(i => i.AdvanceDeducted > 0))
            {
                var advances = await _db.EmployeeAdvances
                    .Where(a => a.EmployeeId == item.EmployeeId && a.DeductedAmount > 0)
                    .OrderByDescending(a => a.AdvanceDate) // نعكس الحركات من الأحدث للأقدم
                    .ToListAsync();

                var toRestore = item.AdvanceDeducted;
                foreach (var adv in advances)
                {
                    if (toRestore <= 0) break;
                    var restored = Math.Min(adv.DeductedAmount, toRestore);
                    adv.DeductedAmount -= restored;
                    
                    // تحديث حالة السلفة بناءً على المبلغ المتبقي
                    if (adv.DeductedAmount <= 0) adv.Status = AdvanceStatus.Pending;
                    else if (adv.DeductedAmount < adv.Amount) adv.Status = AdvanceStatus.PartiallyDeducted;
                    else adv.Status = AdvanceStatus.FullyDeducted;

                    toRestore -= restored;
                    adv.UpdatedAt = TimeHelper.GetEgyptTime();
                }
            }

            // 3. فك ارتباط المكافآت والخصومات المعلقة (لتعود متاحة للمسيرات القادمة)
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
        run.TotalDeductions,
        run.TotalAdvancesDeducted, run.TotalNetPayable, (int)run.Status, run.Notes,
        run.JournalEntryId, run.CreatedAt,
        run.Items.Select(i => new PayrollItemDto(
            i.Id, i.EmployeeId, i.Employee.Name, i.Employee.EmployeeNumber,
            i.Employee.JobTitle, i.BasicSalary, i.TransportationAllowance, i.CommunicationAllowance, i.BonusAmount,
            i.FixedAllowance,
            i.DeductionAmount, i.AdvanceDeducted, i.NetPayable, i.Notes
        )).ToList()
    );
}

// ══════════════════════════════════════════════════════
// 3. ADVANCES (السلف)
// ══════════════════════════════════════════════════════

[ApiController]
[Route("api/employee-advances")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class EmployeeAdvancesController : ControllerBase
{
    private readonly AppDbContext    _db;
    private readonly SequenceService _seq;
    private readonly IAccountingService _accounting;
    private readonly AccountingCoreService _core;

    public EmployeeAdvancesController(AppDbContext db, SequenceService seq, IAccountingService accounting, AccountingCoreService core)
        => (_db, _seq, _accounting, _core) = (db, seq, accounting, core);

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
                a.JournalEntryId, a.CreatedAt
            )).ToListAsync();

        return Ok(new PaginatedResult<EmployeeAdvanceDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpPost]
    [RequireModulePermission(ModuleKeys.Hr, requireEdit: true)]
    public async Task<IActionResult> Create([FromBody] CreateAdvanceDto dto)
    {
        var emp = await _db.Employees.FindAsync(dto.EmployeeId);
        if (emp == null) return NotFound("الموظف غير موجود.");

        var mapDict = await _core.GetSafeSystemMappingsAsync();

        // 🎯 UNIFIED VOUCHER SYSTEM: Create a PaymentVoucher record for this advance (if cash disbursement)
        if (dto.CashAccountId.HasValue && dto.CashAccountId > 0)
        {
            if (!mapDict.TryGetValue(MappingKeys.EmployeeAdvances, out var advAccId) || advAccId == null)
                return BadRequest("لم يتم ضبط حساب سلف الموظفين في الإعدادات.");
        }

        // Retry logic for sequence generation to handle race conditions
        for (int retry = 0; retry < 3; retry++)
        {
            try
            {
                var advNo = await _seq.NextAsync("ADV", async (db, pattern) =>
                {
                    var max = await db.EmployeeAdvances
                        .Where(a => EF.Functions.Like(a.AdvanceNumber, pattern))
                        .Select(a => a.AdvanceNumber).ToListAsync();
                    return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                              .DefaultIfEmpty(0).Max();
                });

                var advance = new EmployeeAdvance
                {
                    AdvanceNumber = advNo,
                    EmployeeId = dto.EmployeeId,
                    AdvanceDate = dto.AdvanceDate,
                    Amount = dto.Amount,
                    Reason = dto.Reason?.Trim(),
                    Notes = dto.Notes?.Trim(),
                    CashAccountId = dto.CashAccountId,
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
                        CashAccountId = advance.CashAccountId.Value,
                        ToAccountId = advAccId.Value,
                        EmployeeId = emp.Id,
                        PaymentMethod = VoucherPaymentMethod.Cash,
                        Description = $"سلفة موظف — {emp.Name}",
                        Reference = advance.AdvanceNumber,
                        CreatedAt = TimeHelper.GetEgyptTime(),
                        CreatedByUserId = UserId
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

        return StatusCode(409, "فشل إنشاء السلفة بسبب تضارب في الترقيم التلقائي. يرجى المحاولة مرة أخرى.");
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var adv = await _db.EmployeeAdvances.FindAsync(id);
        if (adv == null) return NotFound();
        if (adv.Status != AdvanceStatus.Pending)
            return BadRequest("لا يمكن حذف سلفة بدأ خصمها.");

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
    [RequireModulePermission(ModuleKeys.Hr, requireEdit: true)]
    public async Task<IActionResult> Update(int id, [FromBody] CreateAdvanceDto dto)
    {
        var adv = await _db.EmployeeAdvances.FindAsync(id);
        if (adv == null) return NotFound();
        if (adv.Status != AdvanceStatus.Pending)
            return BadRequest("لا يمكن تعديل سلفة بدأ خصمها بالفعل.");

        adv.Amount = dto.Amount;
        adv.AdvanceDate = dto.AdvanceDate;
        adv.Reason = dto.Reason?.Trim();
        adv.Notes = dto.Notes?.Trim();
        adv.CashAccountId = dto.CashAccountId;
        adv.UpdatedAt = TimeHelper.GetEgyptTime();

        // Update voucher if exists
        var voucher = await _db.PaymentVouchers.FirstOrDefaultAsync(v => v.Reference == adv.AdvanceNumber);
        if (voucher != null)
        {
            voucher.Amount = adv.Amount;
            voucher.VoucherDate = adv.AdvanceDate;
            voucher.CashAccountId = adv.CashAccountId ?? 0;
            voucher.Description = $"تعديل سلفة موظف — {adv.AdvanceNumber}";
            
            // Re-post to update journal entry
            await _accounting.PostPaymentVoucherAsync(voucher);
            adv.JournalEntryId = voucher.JournalEntryId;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }
}

// ══════════════════════════════════════════════════════
// 4. BONUSES (المكافآت)
// ══════════════════════════════════════════════════════

[ApiController]
[Route("api/employee-bonuses")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class EmployeeBonusesController : ControllerBase
{
    private readonly AppDbContext    _db;
    private readonly SequenceService _seq;
    private readonly IAccountingService _accounting;
    private readonly AccountingCoreService _core;

    public EmployeeBonusesController(AppDbContext db, SequenceService seq, IAccountingService accounting, AccountingCoreService core)
        => (_db, _seq, _accounting, _core) = (db, seq, accounting, core);

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
                b.JournalEntryId, b.CreatedAt
            )).ToListAsync();

        return Ok(new PaginatedResult<EmployeeBonusDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpPost]
    [RequireModulePermission(ModuleKeys.Hr, requireEdit: true)]
    public async Task<IActionResult> Create([FromBody] CreateBonusDto dto)
    {
        var emp = await _db.Employees.FindAsync(dto.EmployeeId);
        if (emp == null) return NotFound("الموظف غير موجود.");

        var mapDict = await _core.GetSafeSystemMappingsAsync();

        // 🎯 UNIFIED VOUCHER SYSTEM: Create a PaymentVoucher record for this bonus (if cash disbursement)
        if (dto.CashAccountId.HasValue && dto.CashAccountId > 0)
        {
            if (!mapDict.TryGetValue(MappingKeys.SalaryExpense, out var bonusExpenseAccId) || bonusExpenseAccId == null)
                return BadRequest("لم يتم ضبط حساب مصروف الرواتب (للمكافآت) في الإعدادات.");
        }

        // Retry logic for sequence generation
        for (int retry = 0; retry < 3; retry++)
        {
            try
            {
                var bonNo = await _seq.NextAsync("BON", async (db, pattern) =>
                {
                    var max = await db.EmployeeBonuses
                        .Where(b => EF.Functions.Like(b.BonusNumber, pattern))
                        .Select(b => b.BonusNumber).ToListAsync();
                    return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                              .DefaultIfEmpty(0).Max();
                });

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
                        CashAccountId = bonus.CashAccountId.Value,
                        ToAccountId = bonusExpenseAccId.Value,
                        EmployeeId = emp.Id,
                        PaymentMethod = VoucherPaymentMethod.Cash,
                        Description = $"مكافأة موظف — {emp.Name}",
                        Reference = bonus.BonusNumber,
                        CreatedAt = TimeHelper.GetEgyptTime(),
                        CreatedByUserId = UserId
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

        return StatusCode(409, "فشل إنشاء المكافأة بسبب تضارب في الترقيم التلقائي. يرجى المحاولة مرة أخرى.");
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var bon = await _db.EmployeeBonuses.FindAsync(id);
        if (bon == null) return NotFound();
        if (bon.PayrollRunId.HasValue)
            return BadRequest("لا يمكن حذف مكافأة مرتبطة بمسير رواتب.");

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
    [RequireModulePermission(ModuleKeys.Hr, requireEdit: true)]
    public async Task<IActionResult> Update(int id, [FromBody] CreateBonusDto dto)
    {
        var bon = await _db.EmployeeBonuses.FindAsync(id);
        if (bon == null) return NotFound();
        if (bon.PayrollRunId.HasValue)
            return BadRequest("لا يمكن تعديل مكافأة مربوطة بمسير رواتب.");

        bon.Amount = dto.Amount;
        bon.BonusDate = dto.BonusDate;
        bon.BonusType = dto.BonusType;
        bon.Reason = dto.Reason?.Trim();
        bon.Notes = dto.Notes?.Trim();
        bon.CashAccountId = dto.CashAccountId;
        bon.UpdatedAt = TimeHelper.GetEgyptTime();

        var voucher = await _db.PaymentVouchers.FirstOrDefaultAsync(v => v.Reference == bon.BonusNumber);
        if (voucher != null)
        {
            voucher.Amount = bon.Amount;
            voucher.VoucherDate = bon.BonusDate;
            voucher.CashAccountId = bon.CashAccountId ?? 0;
            
            await _accounting.PostPaymentVoucherAsync(voucher);
            bon.JournalEntryId = voucher.JournalEntryId;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }
}

// ══════════════════════════════════════════════════════
// 5. DEDUCTIONS (الخصومات)
// ══════════════════════════════════════════════════════

[ApiController]
[Route("api/employee-deductions")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class EmployeeDeductionsController : ControllerBase
{
    private readonly AppDbContext    _db;
    private readonly SequenceService _seq;
    private readonly IAccountingService _accounting;
    private readonly AccountingCoreService _core;

    public EmployeeDeductionsController(AppDbContext db, SequenceService seq, IAccountingService accounting, AccountingCoreService core)
        => (_db, _seq, _accounting, _core) = (db, seq, accounting, core);

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
                d.JournalEntryId, d.CreatedAt
            )).ToListAsync();

        return Ok(new PaginatedResult<EmployeeDeductionDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDeductionDto dto)
    {
        var emp = await _db.Employees.FindAsync(dto.EmployeeId);
        if (emp == null) return NotFound("الموظف غير موجود.");

        var dedNo = await _seq.NextAsync("DED", async (db, pattern) =>
        {
            var max = await db.EmployeeDeductions
                .Where(d => EF.Functions.Like(d.DeductionNumber, pattern))
                .Select(d => d.DeductionNumber).ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                      .DefaultIfEmpty(0).Max();
        });

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
            CreatedAt       = TimeHelper.GetEgyptTime(),
            CreatedByUserId = UserId
        };

        _db.EmployeeDeductions.Add(ded);

        // 🎯 UNIFIED VOUCHER SYSTEM: Create a ReceiptVoucher record for this deduction (if cash)
        if (dto.CashAccountId.HasValue)
        {
            var mapDict = await _core.GetSafeSystemMappingsAsync();
            if (!mapDict.TryGetValue(MappingKeys.EmployeeDeductions, out var deductionRevenueAccId) || deductionRevenueAccId == null)
                return BadRequest("لم يتم ضبط حساب إيراد خصومات الموظفين في الإعدادات.");

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
                CostCenter = OrderSource.Website
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
            return BadRequest("لا يمكن حذف خصم مرتبط بمسير رواتب.");

        _db.EmployeeDeductions.Remove(ded);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{id}")]
    [RequireModulePermission(ModuleKeys.Hr, requireEdit: true)]
    public async Task<IActionResult> Update(int id, [FromBody] CreateDeductionDto dto)
    {
        var ded = await _db.EmployeeDeductions.FindAsync(id);
        if (ded == null) return NotFound();
        if (ded.PayrollRunId.HasValue)
            return BadRequest("لا يمكن تعديل خصم مربوط بمسير رواتب.");

        ded.Amount = dto.Amount;
        ded.DeductionDate = dto.DeductionDate;
        ded.DeductionType = dto.DeductionType;
        ded.Reason = dto.Reason?.Trim();
        ded.Notes = dto.Notes?.Trim();
        ded.CashAccountId = dto.CashAccountId;
        ded.UpdatedAt = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return NoContent();
    }
}

// ══════════════════════════════════════════════════════
// 6. DEPARTMENTS (الأقسام / الفئات)
// ══════════════════════════════════════════════════════

[ApiController]
[Route("api/departments")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class DepartmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    public DepartmentsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _db.Departments
            .Include(d => d.Employees)
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentDto(d.Id, d.Name, d.Description, d.Employees.Count))
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentDto dto)
    {
        var dept = new Department { Name = dto.Name.Trim(), Description = dto.Description?.Trim() };
        _db.Departments.Add(dept);
        await _db.SaveChangesAsync();
        return Ok(new { id = dept.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateDepartmentDto dto)
    {
        var dept = await _db.Departments.FindAsync(id);
        if (dept == null) return NotFound();
        dept.Name = dto.Name.Trim();
        dept.Description = dto.Description?.Trim();
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var dept = await _db.Departments.Include(d => d.Employees).FirstOrDefaultAsync(d => d.Id == id);
        if (dept == null) return NotFound();
        if (dept.Employees.Any()) return BadRequest("لا يمكن حذف قسم به موظفين.");
        _db.Departments.Remove(dept);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

// ── DTOs إضافية ──────────────────────────────────────────
public record LinkUserDto(string? AppUserId);
