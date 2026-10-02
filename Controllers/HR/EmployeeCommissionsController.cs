using System.Security.Claims;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/commissions")]
[RequirePermission(ModuleKeys.HrCommissions)]
public class EmployeeCommissionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITranslator _t;
    private readonly IAuditService _audit;
    public EmployeeCommissionsController(AppDbContext db, ITranslator t, IAuditService audit) { _db = db; _t = t; _audit = audit; }

    private static bool IsStoreEmployee(Employee? emp)
        => emp != null && (emp.BranchId == 5 || emp.CostCenter == OrderSource.Website);

    private static bool IsStoreGroup(CommissionGroup g)
        => g.Members.Any(m => m.BranchId == 5 || m.CostCenter == OrderSource.Website);

    private static decimal CalculateStoreNetSales(IEnumerable<Order> orders)
    {
        return orders
            .Where(o => (o.Source == OrderSource.Website || o.BranchId == 5) &&
                        (o.Status == OrderStatus.Delivered || o.Status == OrderStatus.PartiallyReturned))
            .Sum(o => {
                var ret = o.Items.Sum(i => i.Quantity > 0 ? (i.TotalPrice / i.Quantity) * i.ReturnedQuantity : 0m);
                return Math.Max(0m, (o.TotalAmount - o.DeliveryFee) - ret);
            });
    }

    private static int CalculateStoreDeliveredItemsCount(IEnumerable<Order> orders)
    {
        return orders
            .Where(o => (o.Source == OrderSource.Website || o.BranchId == 5) &&
                        (o.Status == OrderStatus.Delivered || o.Status == OrderStatus.PartiallyReturned))
            .Sum(o => o.Items.Sum(i => Math.Max(0, i.Quantity - i.ReturnedQuantity)));
    }

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
        try { await _audit.LogAsync("UpdateCommissionSetting", "EmployeeCommissionSetting", employeeId.ToString(), $"Updated commission setting for employee ID {employeeId}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return NoContent();
    }

    [HttpGet("summary")]
    public async Task<ActionResult<IEnumerable<EmployeeCommissionSummaryDto>>> GetCommissionsSummary([FromQuery] int? year = null, [FromQuery] int? month = null)
    {
        var egyptTime = TimeHelper.GetEgyptTime();
        var startOfMonth = year.HasValue && month.HasValue
            ? new DateTime(year.Value, month.Value, 1)
            : new DateTime(egyptTime.Year, egyptTime.Month, 1);
        var endOfMonth = startOfMonth.AddMonths(1);
        
        var orders = await _db.Orders
            .Include(o => o.Items)
            .Where(o => o.Status != OrderStatus.Cancelled &&
                (
                    (o.CreatedAt >= startOfMonth && o.CreatedAt < endOfMonth)
                    ||
                    ((o.Source == OrderSource.Website || o.BranchId == 5) &&
                     (o.Status == OrderStatus.Delivered || o.Status == OrderStatus.PartiallyReturned) &&
                     (o.ActualDeliveryDate ?? o.CreatedAt) >= startOfMonth &&
                     (o.ActualDeliveryDate ?? o.CreatedAt) < endOfMonth)
                )
            )
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
            bool isStoreGrp = IsStoreGroup(g);
            List<Order> groupOrders;
            if (isStoreGrp)
            {
                groupOrders = orders.Where(o => 
                    (o.Source == OrderSource.Website || o.BranchId == 5) &&
                    (o.Status == OrderStatus.Delivered || o.Status == OrderStatus.PartiallyReturned) &&
                    ((o.ActualDeliveryDate ?? o.CreatedAt) >= startOfMonth && (o.ActualDeliveryDate ?? o.CreatedAt) < endOfMonth)
                ).ToList();
            }
            else
            {
                var memberUserIds = g.Members.Select(m => m.AppUserId).Where(id => id != null).OfType<string>().ToList();
                var memberIds = g.Members.Select(m => m.Id.ToString()).ToList();
                
                groupOrders = orders.Where(o => 
                    (o.SalesPersonId != null && memberUserIds.Contains(o.SalesPersonId)) || 
                    (o.SalesPersonId != null && memberIds.Contains(o.SalesPersonId))
                ).ToList();
            }
            
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

            bool isStoreGrpComm = isStoreGrp || basis == CommissionBasis.OnlineStoreDeliveredNetSales;
            if (isStoreGrpComm)
            {
                groupOrders = orders.Where(o => 
                    (o.Source == OrderSource.Website || o.BranchId == 5) &&
                    (o.Status == OrderStatus.Delivered || o.Status == OrderStatus.PartiallyReturned) &&
                    ((o.ActualDeliveryDate ?? o.CreatedAt) >= startOfMonth && (o.ActualDeliveryDate ?? o.CreatedAt) < endOfMonth)
                ).ToList();
            }

            decimal relevantSales = 0;
            if (isStoreGrpComm)
            {
                relevantSales = CalculateStoreNetSales(groupOrders);
            }
            else
            {
                var returnsAmount = groupOrders.Sum(o => o.Status == OrderStatus.Returned ? o.TotalAmount : o.Items.Sum(i => i.Quantity > 0 ? (i.TotalPrice / i.Quantity) * i.ReturnedQuantity : 0));
                relevantSales = basis == CommissionBasis.NetSales 
                    ? groupOrders.Sum(o => o.TotalAmount) - returnsAmount
                    : groupOrders.Sum(o => o.SubTotal) - returnsAmount;
            }

            decimal earnedCommission = 0;

            if (type == CommissionType.TargetAchievementTiers || relevantSales >= targetAmount)
            {
                if (type == CommissionType.PercentageOfSales)
                {
                    earnedCommission = relevantSales * (defaultRate / 100);
                }
                else if (type == CommissionType.FixedAmountPerItem)
                {
                    int itemsCount;
                    if (isStoreGrpComm)
                    {
                        itemsCount = CalculateStoreDeliveredItemsCount(groupOrders);
                    }
                    else
                    {
                        var orderIds = groupOrders.Select(o => o.Id).ToList();
                        itemsCount = await _db.OrderItems
                            .Where(oi => orderIds.Contains(oi.OrderId))
                            .SumAsync(oi => oi.Quantity);
                    }
                    
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
                bool isStoreEmp = IsStoreEmployee(e);
                List<Order> empOrders;
                if (isStoreEmp)
                {
                    empOrders = orders.Where(o => 
                        (o.Source == OrderSource.Website || o.BranchId == 5) &&
                        (o.Status == OrderStatus.Delivered || o.Status == OrderStatus.PartiallyReturned) &&
                        ((o.ActualDeliveryDate ?? o.CreatedAt) >= startOfMonth && (o.ActualDeliveryDate ?? o.CreatedAt) < endOfMonth)
                    ).ToList();
                }
                else
                {
                    empOrders = orders.Where(o => 
                        o.SalesPersonId == e.AppUserId || 
                        o.SalesPersonId == e.Id.ToString()
                    ).ToList();
                }

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

                bool isStoreComm = isStoreEmp || basis == CommissionBasis.OnlineStoreDeliveredNetSales;
                if (isStoreComm)
                {
                    empOrders = orders.Where(o => 
                        (o.Source == OrderSource.Website || o.BranchId == 5) &&
                        (o.Status == OrderStatus.Delivered || o.Status == OrderStatus.PartiallyReturned) &&
                        ((o.ActualDeliveryDate ?? o.CreatedAt) >= startOfMonth && (o.ActualDeliveryDate ?? o.CreatedAt) < endOfMonth)
                    ).ToList();
                }

                if (isStoreComm)
                {
                    relevantSales = CalculateStoreNetSales(empOrders);
                }
                else
                {
                    var returnsAmount = empOrders.Sum(o => o.Status == OrderStatus.Returned ? o.TotalAmount : o.Items.Sum(i => i.Quantity > 0 ? (i.TotalPrice / i.Quantity) * i.ReturnedQuantity : 0));
                    relevantSales = basis == CommissionBasis.NetSales 
                        ? empOrders.Sum(o => o.TotalAmount) - returnsAmount
                        : empOrders.Sum(o => o.SubTotal) - returnsAmount;
                }

                if (type == CommissionType.TargetAchievementTiers || relevantSales >= targetAmount)
                {
                    if (type == CommissionType.PercentageOfSales)
                    {
                        earnedCommission = relevantSales * (defaultRate / 100);
                    }
                    else if (type == CommissionType.FixedAmountPerItem)
                    {
                        int itemsCount;
                        if (isStoreComm)
                        {
                            itemsCount = CalculateStoreDeliveredItemsCount(empOrders);
                        }
                        else
                        {
                            var orderIds = empOrders.Select(o => o.Id).ToList();
                            itemsCount = await _db.OrderItems
                                .Where(oi => orderIds.Contains(oi.OrderId))
                                .SumAsync(oi => oi.Quantity);
                        }
                        
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
