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
