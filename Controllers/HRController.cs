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
    private readonly AppDbContext    _db;
    private readonly SequenceService _seq;

    public EmployeesController(AppDbContext db, SequenceService seq)
        => (_db, _seq) = (db, seq);

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

        if (status.HasValue)             q = q.Where(e => e.Status == status.Value);
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
                e.BaseSalary, e.FixedAllowance, e.FixedDeduction,
                e.BankAccount, (int)e.Status, e.Notes,
                e.AttachmentUrl, e.AttachmentPublicId,
                e.AccountId, e.Account != null ? e.Account.NameAr : null,
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
            .Where(e => e.Status == EmployeeStatus.Active)
            .OrderBy(e => e.Name)
            .Select(e => new EmployeeBasicDto(e.Id, e.EmployeeNumber, e.Name, e.JobTitle, e.DepartmentId, e.Department != null ? e.Department.Name : null, e.BaseSalary, (int)e.Status))
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
            e.BaseSalary, e.FixedAllowance, e.FixedDeduction, e.BankAccount, (int)e.Status, e.Notes,
            e.AttachmentUrl, e.AttachmentPublicId,
            e.AccountId, e.Account?.NameAr, e.CreatedAt,
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
            FixedAllowance   = dto.FixedAllowance,
            FixedDeduction   = dto.FixedDeduction,
            BankAccount      = dto.BankAccount?.Trim(),
            Notes            = dto.Notes?.Trim(),
            AttachmentUrl    = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId,
            AccountId        = dto.AccountId ?? defaultAccId,
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
        emp.FixedAllowance    = dto.FixedAllowance;
        emp.FixedDeduction    = dto.FixedDeduction;
        emp.BankAccount       = dto.BankAccount?.Trim();
        emp.Notes             = dto.Notes?.Trim();
        emp.AttachmentUrl     = dto.AttachmentUrl;
        emp.AttachmentPublicId = dto.AttachmentPublicId;
        emp.AccountId         = dto.AccountId;
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
        if (!dto.Items.Any()) return BadRequest("يجب إضافة موظف واحد على الأقل.");

        // تحقق من عدم تكرار نفس الشهر
        if (await _db.PayrollRuns.AnyAsync(p => p.PeriodYear == dto.PeriodYear && p.PeriodMonth == dto.PeriodMonth))
            return BadRequest($"يوجد مسير رواتب لشهر {dto.PeriodMonth}/{dto.PeriodYear} مسبقاً.");

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

        decimal totalBasic = 0, totalBonus = 0, totalDed = 0, totalAdv = 0;

        foreach (var itemDto in dto.Items)
        {
            var emp = await _db.Employees.FindAsync(itemDto.EmployeeId);
            if (emp == null) continue;

            var basic = itemDto.OverrideBasicSalary ?? emp.BaseSalary;
            totalBasic += basic;
            totalBonus += itemDto.BonusAmount;
            totalDed   += itemDto.DeductionAmount;
            totalAdv   += itemDto.AdvanceDeducted;

            run.Items.Add(new PayrollItem
            {
                EmployeeId      = emp.Id,
                BasicSalary     = basic,
                BonusAmount     = itemDto.BonusAmount,
                DeductionAmount = itemDto.DeductionAmount,
                AdvanceDeducted = itemDto.AdvanceDeducted,
                Notes           = itemDto.Notes,
                CreatedAt       = TimeHelper.GetEgyptTime()
            });
        }

        run.TotalBasicSalary      = totalBasic;
        run.TotalBonuses          = totalBonus;
        run.TotalDeductions       = totalDed;
        run.TotalAdvancesDeducted = totalAdv;
        run.TotalNetPayable       = totalBasic + totalBonus - totalDed - totalAdv;

        _db.PayrollRuns.Add(run);
        await _db.SaveChangesAsync();
        return Ok(new { id = run.Id, payrollNumber = run.PayrollNumber });
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

            // مدين: رواتب وأجور — الإجمالي (أساسي + مكافآت)
            je.Lines.Add(new JournalLine
            {
                AccountId   = wagesAccId,
                Debit       = totalGross,
                Credit      = 0,
                Description = $"رواتب وأجور — {run.PeriodMonth}/{run.PeriodYear}"
            });

            // دائن: رواتب مستحقة — الصافي (بعد خصم الخصومات والسلف)
            je.Lines.Add(new JournalLine
            {
                AccountId   = accrualAccId,
                Debit       = 0,
                Credit      = run.TotalNetPayable,
                Description = $"رواتب مستحقة صافي — {run.PeriodMonth}/{run.PeriodYear}"
            });

            // دائن: إيرادات الخصومات (إن وجدت)
            if (run.TotalDeductions > 0)
                je.Lines.Add(new JournalLine
                {
                    AccountId   = dedAccId,
                    Debit       = 0,
                    Credit      = run.TotalDeductions,
                    Description = $"خصومات موظفين — {run.PeriodMonth}/{run.PeriodYear}"
                });

            // دائن: سلف الموظفين (إن وجدت خصومات سلف)
            if (run.TotalAdvancesDeducted > 0)
                je.Lines.Add(new JournalLine
                {
                    AccountId   = advAccId,
                    Debit       = 0,
                    Credit      = run.TotalAdvancesDeducted,
                    Description = $"خصم سلف موظفين — {run.PeriodMonth}/{run.PeriodYear}"
                });

            // ربط كل سطر بالموظف المعني (للكشف)
            // ملاحظة: السطر الإجمالي لا يُربط بموظف واحد — يبقى عاماً
            // لكن نضيف سطوراً تفصيلية مرتبطة بكل موظف على حساب الرواتب المستحقة
            foreach (var item in run.Items)
            {
                if (item.Employee.AccountId.HasValue)
                {
                    je.Lines.Add(new JournalLine
                    {
                        AccountId   = accrualAccId,
                        Debit       = item.NetPayable,
                        Credit      = 0,
                        Description = $"راتب {item.Employee.Name} — {run.PeriodMonth}/{run.PeriodYear}",
                        EmployeeId  = item.EmployeeId
                    });
                    je.Lines.Add(new JournalLine
                    {
                        AccountId   = item.Employee.AccountId.Value,
                        Debit       = 0,
                        Credit      = item.NetPayable,
                        Description = $"راتب {item.Employee.Name} — {run.PeriodMonth}/{run.PeriodYear}",
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
        var run = await _db.PayrollRuns.FindAsync(id);
        if (run == null) return NotFound();
        if (run.Status == PayrollStatus.Posted)
            return BadRequest("لا يمكن حذف مسير مرحّل.");

        _db.PayrollRuns.Remove(run);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static PayrollRunDto ToDto(PayrollRun run) => new(
        run.Id, run.PayrollNumber, run.PeriodYear, run.PeriodMonth,
        run.TotalBasicSalary, run.TotalBonuses, run.TotalDeductions,
        run.TotalAdvancesDeducted, run.TotalNetPayable, (int)run.Status, run.Notes,
        run.JournalEntryId, run.CreatedAt,
        run.Items.Select(i => new PayrollItemDto(
            i.Id, i.EmployeeId, i.Employee.Name, i.Employee.EmployeeNumber,
            i.Employee.JobTitle, i.BasicSalary, i.BonusAmount,
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

    public EmployeeAdvancesController(AppDbContext db, SequenceService seq)
        => (_db, _seq) = (db, seq);

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

    // POST — صرف سلفة (مع قيد محاسبي: مدين سلف موظفين، دائن خزينة)
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAdvanceDto dto)
    {
        var emp = await _db.Employees.FindAsync(dto.EmployeeId);
        if (emp == null) return NotFound("الموظف غير موجود.");

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
            AdvanceNumber   = advNo,
            EmployeeId      = dto.EmployeeId,
            AdvanceDate     = dto.AdvanceDate,
            Amount          = dto.Amount,
            Reason          = dto.Reason?.Trim(),
            Notes           = dto.Notes?.Trim(),
            CashAccountId   = dto.CashAccountId,
            Status          = AdvanceStatus.Pending,
            CreatedAt       = TimeHelper.GetEgyptTime(),
            CreatedByUserId = UserId
        };

        _db.EmployeeAdvances.Add(advance);

        // قيد: مدين سلف الموظفين، دائن الخزينة
        JournalEntry? je = null;
        if (emp.AccountId.HasValue && dto.CashAccountId.HasValue)
        {
            var jeNo = await _seq.NextAsync("JE", async (db, pattern) =>
            {
                var max = await db.JournalEntries
                    .Where(e => EF.Functions.Like(e.EntryNumber, pattern))
                    .Select(e => e.EntryNumber).ToListAsync();
                return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                          .DefaultIfEmpty(0).Max();
            });

            je = new JournalEntry
            {
                EntryNumber     = jeNo,
                EntryDate       = dto.AdvanceDate,
                Type            = JournalEntryType.AdvancePayment,
                Status          = JournalEntryStatus.Posted,
                Description     = $"سلفة — {emp.Name}",
                Reference       = advNo,
                CreatedByUserId = UserId,
                CreatedAt       = TimeHelper.GetEgyptTime(),
                Lines = new List<JournalLine>
                {
                    new() { AccountId = emp.AccountId.Value,     Debit = dto.Amount, Credit = 0,           Description = $"سلفة {emp.Name}", EmployeeId = emp.Id },
                    new() { AccountId = dto.CashAccountId.Value, Debit = 0,          Credit = dto.Amount,  Description = $"صرف سلفة {emp.Name}" }
                }
            };
            _db.JournalEntries.Add(je);
        }

        await _db.SaveChangesAsync();
        if (je != null) { advance.JournalEntryId = je.Id; await _db.SaveChangesAsync(); }

        return Ok(new { id = advance.Id, advanceNumber = advance.AdvanceNumber, journalEntryId = je?.Id });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var adv = await _db.EmployeeAdvances.FindAsync(id);
        if (adv == null) return NotFound();
        if (adv.Status != AdvanceStatus.Pending)
            return BadRequest("لا يمكن حذف سلفة بدأ خصمها.");

        _db.EmployeeAdvances.Remove(adv);
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

    public EmployeeBonusesController(AppDbContext db, SequenceService seq)
        => (_db, _seq) = (db, seq);

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
    public async Task<IActionResult> Create([FromBody] CreateBonusDto dto)
    {
        var emp = await _db.Employees.FindAsync(dto.EmployeeId);
        if (emp == null) return NotFound("الموظف غير موجود.");

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
            BonusNumber     = bonNo,
            EmployeeId      = dto.EmployeeId,
            BonusDate       = dto.BonusDate,
            Amount          = dto.Amount,
            BonusType       = dto.BonusType,
            Reason          = dto.Reason?.Trim(),
            Notes           = dto.Notes?.Trim(),
            CashAccountId   = dto.CashAccountId,
            CreatedAt       = TimeHelper.GetEgyptTime(),
            CreatedByUserId = UserId
        };

        _db.EmployeeBonuses.Add(bonus);

        // قيد: مدين مصروف رواتب (مكافآت)، دائن الخزينة
        JournalEntry? je = null;
        if (dto.CashAccountId.HasValue)
        {
            var jeNo = await _seq.NextAsync("JE", async (db, pattern) =>
            {
                var max = await db.JournalEntries
                    .Where(e => EF.Functions.Like(e.EntryNumber, pattern))
                    .Select(e => e.EntryNumber).ToListAsync();
                return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                          .DefaultIfEmpty(0).Max();
            });

            // الحصول على حساب مصروف المكافآت من الربط
            var core = (AccountingCoreService)HttpContext.RequestServices.GetService(typeof(AccountingCoreService))!;
            var mapDict = await core.GetSafeSystemMappingsAsync();
            var bonusExpenseAccId = await core.GetRequiredMappedAccountAsync(MappingKeys.SalaryExpense, mapDict);

            je = new JournalEntry
            {
                EntryNumber     = jeNo,
                EntryDate       = dto.BonusDate,
                Type            = JournalEntryType.Payroll,
                Status          = JournalEntryStatus.Posted,
                Description     = $"مكافأة فورية — {emp.Name}",
                Reference       = bonNo,
                CreatedByUserId = UserId,
                CreatedAt       = TimeHelper.GetEgyptTime(),
                Lines = new List<JournalLine>
                {
                    new() { AccountId = bonusExpenseAccId,       Debit = dto.Amount, Credit = 0,           Description = $"مكافأة {emp.Name}", EmployeeId = emp.Id },
                    new() { AccountId = dto.CashAccountId.Value, Debit = 0,          Credit = dto.Amount,  Description = $"صرف مكافأة {emp.Name}" }
                }
            };
            _db.JournalEntries.Add(je);
        }

        await _db.SaveChangesAsync();
        if (je != null) { bonus.JournalEntryId = je.Id; await _db.SaveChangesAsync(); }

        return Ok(new { id = bonus.Id, bonusNumber = bonus.BonusNumber, journalEntryId = je?.Id });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var bon = await _db.EmployeeBonuses.FindAsync(id);
        if (bon == null) return NotFound();
        if (bon.PayrollRunId.HasValue)
            return BadRequest("لا يمكن حذف مكافأة مرتبطة بمسير رواتب.");

        _db.EmployeeBonuses.Remove(bon);
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

    public EmployeeDeductionsController(AppDbContext db, SequenceService seq)
        => (_db, _seq) = (db, seq);

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

        // قيد: مدين الخزينة، دائن إيراد خصومات موظفين (إذا تم دفعه نقداً فوراً)
        JournalEntry? je = null;
        if (dto.CashAccountId.HasValue)
        {
            var jeNo = await _seq.NextAsync("JE", async (db, pattern) =>
            {
                var max = await db.JournalEntries
                    .Where(e => EF.Functions.Like(e.EntryNumber, pattern))
                    .Select(e => e.EntryNumber).ToListAsync();
                return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                          .DefaultIfEmpty(0).Max();
            });

            var core = (AccountingCoreService)HttpContext.RequestServices.GetService(typeof(AccountingCoreService))!;
            var mapDict = await core.GetSafeSystemMappingsAsync();
            var deductionRevenueAccId = await core.GetRequiredMappedAccountAsync(MappingKeys.EmployeeDeductions, mapDict);

            je = new JournalEntry
            {
                EntryNumber     = jeNo,
                EntryDate       = dto.DeductionDate,
                Type            = JournalEntryType.Payroll,
                Status          = JournalEntryStatus.Posted,
                Description     = $"تحصيل خصم فوري — {emp.Name}",
                Reference       = dedNo,
                CreatedByUserId = UserId,
                CreatedAt       = TimeHelper.GetEgyptTime(),
                Lines = new List<JournalLine>
                {
                    new() { AccountId = dto.CashAccountId.Value,  Debit = dto.Amount, Credit = 0,           Description = $"تحصيل خصم {emp.Name}" },
                    new() { AccountId = deductionRevenueAccId,   Debit = 0,          Credit = dto.Amount,  Description = $"خصم {emp.Name}", EmployeeId = emp.Id }
                }
            };
            _db.JournalEntries.Add(je);
        }

        await _db.SaveChangesAsync();
        if (je != null) { ded.JournalEntryId = je.Id; await _db.SaveChangesAsync(); }

        return Ok(new { id = ded.Id, deductionNumber = ded.DeductionNumber, journalEntryId = je?.Id });
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
