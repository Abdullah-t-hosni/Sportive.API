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

    [HttpGet("calculate-attendance")]
    public async Task<IActionResult> CalculateAttendance([FromQuery] int year, [FromQuery] int month)
    {
        var startOfPeriod = new DateTime(year, month, 1);
        var endOfPeriod = startOfPeriod.AddMonths(1).AddDays(-1);

        var settings = await _db.StoreInfo.FirstOrDefaultAsync(x => x.StoreConfigId == 1);
        var enableGradPolicy = settings?.EnableGraduatedDelayPolicy ?? true;
        var graceMins = settings?.DelayGraceMinutes ?? 15;
        var quarterLimit = settings?.DelayQuarterDayLimitMinutes ?? 30;
        var halfLimit = settings?.DelayHalfDayLimitMinutes ?? 60;

                var activeEmployees = await _db.Employees
            .Include(e => e.Advances)
            .Include(e => e.Bonuses)
            .Include(e => e.Deductions)
            .Include(e => e.CommissionSetting)
            .ThenInclude(s => s != null ? s.Tiers : null)
            .Where(e => e.Status == EmployeeStatus.Active)
            .ToListAsync();

        var attendances = await _db.EmployeeAttendances
            .Where(a => a.Date >= startOfPeriod.Date && a.Date <= endOfPeriod.Date)
            .ToListAsync();

        // Calculate commissions for the period
        var endOfPeriodExclusive = startOfPeriod.AddMonths(1);
        var orders = await _db.Orders
            .Include(o => o.Items)
            .Where(o => o.CreatedAt >= startOfPeriod && o.CreatedAt < endOfPeriodExclusive && o.Status != OrderStatus.Cancelled)
            .ToListAsync();

        var groups = await _db.CommissionGroups
            .Include(g => g.Members)
            .Include(g => g.Tiers)
            .Include(g => g.CommissionScheme)
            .ThenInclude(s => s != null ? s.Tiers : null)
            .ToListAsync();

        var employeeGroupCommissions = new Dictionary<int, decimal>();
        foreach (var g in groups)
        {
            var memberUserIds = g.Members.Select(m => m.AppUserId).Where(id => !string.IsNullOrEmpty(id)).ToList();
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

            decimal earnedGroupCommission = 0;

            if (type == CommissionType.TargetAchievementTiers || relevantSales >= targetAmount)
            {
                if (type == CommissionType.PercentageOfSales)
                {
                    earnedGroupCommission = relevantSales * (defaultRate / 100);
                }
                else if (type == CommissionType.FixedAmountPerItem)
                {
                    var orderIds = groupOrders.Select(o => o.Id).ToList();
                    var itemsCount = await _db.OrderItems
                        .Where(oi => orderIds.Contains(oi.OrderId))
                        .SumAsync(oi => oi.Quantity);
                    
                    earnedGroupCommission = itemsCount * defaultRate;
                }
                else if (type == CommissionType.TieredPercentage)
                {
                    var sortedTiers = tiersList.OrderBy(t => t.MinAmount).ToList();
                    var applicableTier = sortedTiers.LastOrDefault(t => relevantSales >= t.MinAmount && relevantSales <= t.MaxAmount);
                    
                    if (applicableTier != null)
                    {
                        earnedGroupCommission = relevantSales * (applicableTier.Rate / 100);
                    }
                    else
                    {
                        var lastTier = sortedTiers.LastOrDefault();
                        if (lastTier != null && relevantSales > lastTier.MaxAmount)
                        {
                            earnedGroupCommission = relevantSales * (lastTier.Rate / 100);
                        }
                        else
                        {
                            earnedGroupCommission = relevantSales * (defaultRate / 100);
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
                        earnedGroupCommission = relevantSales * (applicableTier.Rate / 100);
                    }
                    else
                    {
                        var lastTier = sortedTiers.LastOrDefault();
                        if (lastTier != null && achievementPercentage > lastTier.MaxAmount)
                        {
                            earnedGroupCommission = relevantSales * (lastTier.Rate / 100);
                        }
                        else
                        {
                            earnedGroupCommission = relevantSales * (defaultRate / 100);
                        }
                    }
                }
            }

            if (g.Members.Any())
            {
                var share = earnedGroupCommission / g.Members.Count;
                foreach (var m in g.Members)
                {
                    employeeGroupCommissions[m.Id] = share;
                }
            }
        }

        var result = new List<CreatePayrollItemDto>();

        foreach (var emp in activeEmployees)
        {
            var empAttendances = attendances.Where(a => a.EmployeeId == emp.Id).ToList();

            // Calculate Absence (explicitly logged IsAbsent, or missing records on workdays)
            var weekendDays = (emp.WeeklyDaysOff ?? "Friday")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(d => d.Trim().ToLower())
                .ToList();

            var today = TimeHelper.GetEgyptTime().Date;
            var daysInPeriod = DateTime.DaysInMonth(year, month);
            var missingDays = 0;

            for (int day = 1; day <= daysInPeriod; day++)
            {
                var date = new DateTime(year, month, day);
                if (date > today) continue; // Don't count future days as absent

                var dayNameAr = date.ToString("dddd", new System.Globalization.CultureInfo("ar-EG")).ToLower();
                var dayNameEn = date.ToString("dddd", new System.Globalization.CultureInfo("en-US")).ToLower();

                var isWeekend = weekendDays.Contains(dayNameEn) || weekendDays.Contains(dayNameAr);
                if (isWeekend) continue; // Weekend is not absent

                if (!empAttendances.Any(a => a.Date == date))
                {
                    missingDays++;
                }
            }

            var absenceDays = empAttendances.Count(a => a.IsAbsent) + missingDays;

            // Calculate Overtime
            var overtimeHours = empAttendances.Sum(a => a.OvertimeHours);

            // Calculate Delay
            var delayMinutes = empAttendances.Sum(a => a.DelayMinutes);

            // Financial Calculations
            var baseSalary = emp.BaseSalary;
            var daysPerMonth = emp.DaysPerMonth > 0 ? emp.DaysPerMonth : 26;
            var workHoursPerDay = emp.WorkHoursPerDay > 0 ? (decimal)emp.WorkHoursPerDay : 9m;

            var absenceDeduction = Math.Round(absenceDays * (baseSalary / daysPerMonth), 2);
            var overtimeAmount = Math.Round(overtimeHours * (baseSalary / daysPerMonth / workHoursPerDay) * emp.OvertimeMultiplier, 2);

            decimal delayDeduction = 0m;
            if (enableGradPolicy)
            {
                decimal totalDeductedDays = 0m;
                foreach (var att in empAttendances)
                {
                    if (att.DelayMinutes > 0 && !att.IsAbsent)
                    {
                        if (att.DelayMinutes > graceMins && att.DelayMinutes <= quarterLimit)
                        {
                            totalDeductedDays += 0.25m; // ربع يوم
                        }
                        else if (att.DelayMinutes > quarterLimit && att.DelayMinutes <= halfLimit)
                        {
                            totalDeductedDays += 0.50m; // نصف يوم
                        }
                        else if (att.DelayMinutes > halfLimit)
                        {
                            totalDeductedDays += 1.00m; // يوم كامل
                        }
                    }
                }
                delayDeduction = Math.Round(totalDeductedDays * (baseSalary / daysPerMonth), 2);
            }
            else
            {
                delayDeduction = Math.Round((delayMinutes / 60m) * (baseSalary / daysPerMonth / workHoursPerDay), 2);
            }

            // Advances
            var remainingAdvance = emp.Advances
                .Where(a => a.Status != AdvanceStatus.FullyDeducted && a.AdvanceDate <= endOfPeriod)
                .Sum(a => a.Amount - a.DeductedAmount);
            var proposedAdvanceDeduct = Math.Min(remainingAdvance, Math.Round(baseSalary * 0.25m, 2));

            // Bonuses (Pending)
            var pendingBonuses = emp.Bonuses
                .Where(b => b.PayrollRunId == null && b.CashAccountId == null && b.BonusDate <= endOfPeriod)
                .Sum(b => b.Amount);

            // Deductions (Pending)
            var pendingDeductions = emp.Deductions
                .Where(d => d.PayrollRunId == null && d.CashAccountId == null && d.DeductionDate <= endOfPeriod)
                .Sum(d => d.Amount);

            // Total Deduction = Fixed Deduction + Pending Deductions + Delay Deduction
            var totalDeduction = emp.FixedDeduction + pendingDeductions + delayDeduction;

            var notesList = new List<string>();
            if (delayMinutes > 0)
                notesList.Add($"خصم تأخير: {(int)delayMinutes} دقيقة بقيمة {delayDeduction} ج.م");
            if (absenceDays > 0)
                notesList.Add($"غياب: {absenceDays} أيام بقيمة {absenceDeduction} ج.م");
            if (overtimeHours > 0)
                notesList.Add($"إضافي: {overtimeHours:F1} ساعة بقيمة {overtimeAmount} ج.م");

            var notes = string.Join(" | ", notesList);

            // Calculate Commission Amount for Draft
            decimal earnedCommission = 0;
            if (employeeGroupCommissions.TryGetValue(emp.Id, out var groupComm))
            {
                earnedCommission = Math.Round(groupComm, 2);
            }
            else if (emp.CommissionSetting != null)
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
                earnedCommission = Math.Round(earnedCommission, 2);
            }

            result.Add(new CreatePayrollItemDto(
                emp.Id,
                null, // OverrideBasicSalary = null to use BaseSalary
                emp.TransportationAllowance,
                emp.CommunicationAllowance,
                emp.BonusAmount + pendingBonuses,
                emp.FixedAllowance,
                totalDeduction,
                proposedAdvanceDeduct,
                absenceDays,
                absenceDeduction,
                overtimeHours,
                overtimeAmount,
                notes,
                earnedCommission
            ));
        }

        return Ok(result);
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
            var endOfPeriod = startOfPeriod.AddMonths(1);
            
            var orders = await _db.Orders
                .Include(o => o.Items)
                .Where(o => o.CreatedAt >= startOfPeriod && o.CreatedAt < endOfPeriod && o.Status != OrderStatus.Cancelled)
                .ToListAsync();

            var groups = await _db.CommissionGroups
                .Include(g => g.Members)
                .Include(g => g.Tiers)
                .Include(g => g.CommissionScheme)
                .ThenInclude(s => s != null ? s.Tiers : null)
                .ToListAsync();

            var employeeGroupCommissions = new Dictionary<int, decimal>();
            foreach (var g in groups)
            {
                var memberUserIds = g.Members.Select(m => m.AppUserId).Where(id => !string.IsNullOrEmpty(id)).ToList();
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

                decimal earnedGroupCommission = 0;

                if (type == CommissionType.TargetAchievementTiers || relevantSales >= targetAmount)
                {
                    if (type == CommissionType.PercentageOfSales)
                    {
                        earnedGroupCommission = relevantSales * (defaultRate / 100);
                    }
                    else if (type == CommissionType.FixedAmountPerItem)
                    {
                        var orderIds = groupOrders.Select(o => o.Id).ToList();
                        var itemsCount = await _db.OrderItems
                            .Where(oi => orderIds.Contains(oi.OrderId))
                            .SumAsync(oi => oi.Quantity);
                        
                        earnedGroupCommission = itemsCount * defaultRate;
                    }
                    else if (type == CommissionType.TieredPercentage)
                    {
                        var sortedTiers = tiersList.OrderBy(t => t.MinAmount).ToList();
                        var applicableTier = sortedTiers.LastOrDefault(t => relevantSales >= t.MinAmount && relevantSales <= t.MaxAmount);
                        
                        if (applicableTier != null)
                        {
                            earnedGroupCommission = relevantSales * (applicableTier.Rate / 100);
                        }
                        else
                        {
                            var lastTier = sortedTiers.LastOrDefault();
                            if (lastTier != null && relevantSales > lastTier.MaxAmount)
                            {
                                earnedGroupCommission = relevantSales * (lastTier.Rate / 100);
                            }
                            else
                            {
                                earnedGroupCommission = relevantSales * (defaultRate / 100);
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
                            earnedGroupCommission = relevantSales * (applicableTier.Rate / 100);
                        }
                        else
                        {
                            var lastTier = sortedTiers.LastOrDefault();
                            if (lastTier != null && achievementPercentage > lastTier.MaxAmount)
                            {
                                earnedGroupCommission = relevantSales * (lastTier.Rate / 100);
                            }
                            else
                            {
                                earnedGroupCommission = relevantSales * (defaultRate / 100);
                            }
                        }
                    }
                }

                if (g.Members.Any())
                {
                    var share = earnedGroupCommission / g.Members.Count;
                    foreach (var m in g.Members)
                    {
                        employeeGroupCommissions[m.Id] = share;
                    }
                }
            }

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
                decimal earnedCommission = itemDto.CommissionAmount ?? 0;
                if (itemDto.CommissionAmount == null)
                {
                    if (employeeGroupCommissions.TryGetValue(emp.Id, out var groupComm))
                    {
                        earnedCommission = Math.Round(groupComm, 2);
                    }
                    else if (emp.CommissionSetting != null)
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
                        earnedCommission = Math.Round(earnedCommission, 2);
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

    // PUT /api/payroll/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreatePayrollRunDto dto)
    {
        try 
        {
            if (dto == null || dto.Items == null || !dto.Items.Any()) 
                return BadRequest(new { message = _t.Get("HR.PayrollMinOneEmployee") });

            var run = await _db.PayrollRuns
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (run == null) return NotFound();
            if (run.Status != PayrollStatus.Draft)
                return BadRequest(new { message = "Only draft payroll runs can be updated." });

            var lang = Request.Headers["Accept-Language"].ToString().StartsWith("en") ? "en" : "ar";

            // Update basic fields
            run.PeriodYear                 = dto.PeriodYear;
            run.PeriodMonth                = dto.PeriodMonth;
            run.Notes                      = dto.Notes?.Trim();
            if (dto.WagesExpenseAccountId.HasValue) run.WagesExpenseAccountId = dto.WagesExpenseAccountId;
            if (dto.AccruedSalariesAccountId.HasValue) run.AccruedSalariesAccountId = dto.AccruedSalariesAccountId;
            if (dto.DeductionRevenueAccountId.HasValue) run.DeductionRevenueAccountId = dto.DeductionRevenueAccountId;
            if (dto.AdvancesAccountId.HasValue) run.AdvancesAccountId = dto.AdvancesAccountId;
            run.UpdatedAt                  = TimeHelper.GetEgyptTime();

            // Clear old items
            _db.PayrollItems.RemoveRange(run.Items);
            run.Items.Clear();

            decimal totalBasic = 0, totalTrans = 0, totalComm = 0, totalBonus = 0, totalFixedAll = 0, totalDed = 0, totalAdv = 0, totalAbsence = 0, totalOvertime = 0;
            decimal totalCommission = 0;

            var startOfPeriod = new DateTime(dto.PeriodYear, dto.PeriodMonth, 1);
            var endOfPeriod = startOfPeriod.AddMonths(1);
            
            var orders = await _db.Orders
                .Include(o => o.Items)
                .Where(o => o.CreatedAt >= startOfPeriod && o.CreatedAt < endOfPeriod && o.Status != OrderStatus.Cancelled)
                .ToListAsync();

            var groups = await _db.CommissionGroups
                .Include(g => g.Members)
                .Include(g => g.Tiers)
                .Include(g => g.CommissionScheme)
                .ThenInclude(s => s != null ? s.Tiers : null)
                .ToListAsync();

            var employeeGroupCommissions = new Dictionary<int, decimal>();
            foreach (var g in groups)
            {
                var memberUserIds = g.Members.Select(m => m.AppUserId).Where(id => !string.IsNullOrEmpty(id)).ToList();
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

                decimal earnedGroupCommission = 0;

                if (type == CommissionType.TargetAchievementTiers || relevantSales >= targetAmount)
                {
                    if (type == CommissionType.PercentageOfSales)
                    {
                        earnedGroupCommission = relevantSales * (defaultRate / 100);
                    }
                    else if (type == CommissionType.FixedAmountPerItem)
                    {
                        var orderIds = groupOrders.Select(o => o.Id).ToList();
                        var itemsCount = await _db.OrderItems
                            .Where(oi => orderIds.Contains(oi.OrderId))
                            .SumAsync(oi => oi.Quantity);
                        
                        earnedGroupCommission = itemsCount * defaultRate;
                    }
                    else if (type == CommissionType.TieredPercentage)
                    {
                        var sortedTiers = tiersList.OrderBy(t => t.MinAmount).ToList();
                        var applicableTier = sortedTiers.LastOrDefault(t => relevantSales >= t.MinAmount && relevantSales <= t.MaxAmount);
                        
                        if (applicableTier != null)
                        {
                            earnedGroupCommission = relevantSales * (applicableTier.Rate / 100);
                        }
                        else
                        {
                            var lastTier = sortedTiers.LastOrDefault();
                            if (lastTier != null && relevantSales > lastTier.MaxAmount)
                            {
                                earnedGroupCommission = relevantSales * (lastTier.Rate / 100);
                            }
                            else
                            {
                                earnedGroupCommission = relevantSales * (defaultRate / 100);
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
                            earnedGroupCommission = relevantSales * (applicableTier.Rate / 100);
                        }
                        else
                        {
                            var lastTier = sortedTiers.LastOrDefault();
                            if (lastTier != null && achievementPercentage > lastTier.MaxAmount)
                            {
                                earnedGroupCommission = relevantSales * (lastTier.Rate / 100);
                            }
                            else
                            {
                                earnedGroupCommission = relevantSales * (defaultRate / 100);
                            }
                        }
                    }
                }

                if (g.Members.Any())
                {
                    var share = earnedGroupCommission / g.Members.Count;
                    foreach (var m in g.Members)
                    {
                        employeeGroupCommissions[m.Id] = share;
                    }
                }
            }

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
                decimal earnedCommission = itemDto.CommissionAmount ?? 0;
                if (itemDto.CommissionAmount == null)
                {
                    if (employeeGroupCommissions.TryGetValue(emp.Id, out var groupComm))
                    {
                        earnedCommission = Math.Round(groupComm, 2);
                    }
                    else if (emp.CommissionSetting != null)
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
                        earnedCommission = Math.Round(earnedCommission, 2);
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

            await _db.SaveChangesAsync();
            return Ok(new { id = run.Id, payrollNumber = run.PayrollNumber });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Error updating payroll run.", details = ex.Message });
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
                var je = await _db.JournalEntries.Include(j => j.Lines).FirstOrDefaultAsync(j => j.Id == run.JournalEntryId.Value);
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
