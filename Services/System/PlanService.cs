using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.DTOs.System;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public class PlanService : IPlanService
{
    private readonly MasterDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PlanService> _logger;

    private const string CacheKeyAllPlans = "plans_all";
    private const string CacheKeyAnalyticsDashboard = "analytics_dashboard";

    public PlanService(MasterDbContext context, IMemoryCache cache, ILogger<PlanService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IEnumerable<PlanDto>> GetAllPlansAsync(bool includeInactive = false)
    {
        if (!includeInactive && _cache.TryGetValue(CacheKeyAllPlans, out IEnumerable<PlanDto>? cachedPlans))
        {
            return cachedPlans!;
        }

        var query = _context.Plans.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        var plans = await query
            .OrderBy(p => p.DisplayOrder)
            .Select(p => new PlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                MaxUsers = p.MaxUsers,
                MaxBranches = p.MaxBranches,
                MaxStorageGB = p.MaxStorageGB,
                MonthlyPrice = p.MonthlyPrice,
                YearlyPrice = p.YearlyPrice,
                IsActive = p.IsActive,
                DisplayOrder = p.DisplayOrder,
                IsFeatured = p.IsFeatured
            })
            .ToListAsync();

        if (!includeInactive)
        {
            _cache.Set(CacheKeyAllPlans, plans, TimeSpan.FromMinutes(5));
        }

        return plans;
    }

    public async Task<PlanDto?> GetPlanByIdAsync(int id)
    {
        var plan = await _context.Plans.FindAsync(id);
        if (plan == null) return null;

        return new PlanDto
        {
            Id = plan.Id,
            Name = plan.Name,
            Description = plan.Description,
            MaxUsers = plan.MaxUsers,
            MaxBranches = plan.MaxBranches,
            MaxStorageGB = plan.MaxStorageGB,
            MonthlyPrice = plan.MonthlyPrice,
            YearlyPrice = plan.YearlyPrice,
            IsActive = plan.IsActive,
            DisplayOrder = plan.DisplayOrder,
            IsFeatured = plan.IsFeatured
        };
    }

    public async Task<(bool Success, string Message, PlanDto? Data)> CreatePlanAsync(CreatePlanDto request)
    {
        var exists = await _context.Plans.AnyAsync(p => p.Name == request.Name);
        if (exists)
        {
            return (false, "A plan with this name already exists.", null);
        }

        var plan = new Plan
        {
            Name = request.Name,
            Description = request.Description,
            MaxUsers = request.MaxUsers,
            MaxBranches = request.MaxBranches,
            MaxStorageGB = request.MaxStorageGB,
            MonthlyPrice = request.MonthlyPrice,
            YearlyPrice = request.YearlyPrice,
            DisplayOrder = request.DisplayOrder,
            IsFeatured = request.IsFeatured,
            IsActive = true
        };

        _context.Plans.Add(plan);
        await _context.SaveChangesAsync();

        InvalidateCaches();

        var dto = await GetPlanByIdAsync(plan.Id);
        return (true, "Plan created successfully.", dto);
    }

    public async Task<(bool Success, string Message, PlanDto? Data)> UpdatePlanAsync(int id, UpdatePlanDto request)
    {
        var plan = await _context.Plans.FindAsync(id);
        if (plan == null)
        {
            return (false, "Plan not found.", null);
        }

        // Validate: Prevent editing 'Name' if there are active subscriptions
        if (!string.IsNullOrWhiteSpace(request.Name) && request.Name != plan.Name)
        {
            var hasSubscriptions = await _context.TenantSubscriptions.AnyAsync(ts => ts.PlanId == id);
            if (hasSubscriptions)
            {
                return (false, "Cannot change plan name because it has active subscriptions linked to it. This would break historical reporting.", null);
            }
            
            var nameExists = await _context.Plans.AnyAsync(p => p.Name == request.Name && p.Id != id);
            if (nameExists)
            {
                return (false, "A plan with this name already exists.", null);
            }
            plan.Name = request.Name;
        }

        if (request.Description != null) plan.Description = request.Description;
        if (request.MaxUsers.HasValue) plan.MaxUsers = request.MaxUsers.Value;
        if (request.MaxBranches.HasValue) plan.MaxBranches = request.MaxBranches.Value;
        if (request.MaxStorageGB.HasValue) plan.MaxStorageGB = request.MaxStorageGB.Value;
        if (request.MonthlyPrice.HasValue) plan.MonthlyPrice = request.MonthlyPrice.Value;
        if (request.YearlyPrice.HasValue) plan.YearlyPrice = request.YearlyPrice.Value;
        if (request.DisplayOrder.HasValue) plan.DisplayOrder = request.DisplayOrder.Value;
        if (request.IsFeatured.HasValue) plan.IsFeatured = request.IsFeatured.Value;

        await _context.SaveChangesAsync();

        InvalidateCaches();

        var dto = await GetPlanByIdAsync(plan.Id);
        return (true, "Plan updated successfully.", dto);
    }

    public async Task<(bool Success, string Message)> DeactivatePlanAsync(int id)
    {
        var plan = await _context.Plans.FindAsync(id);
        if (plan == null)
        {
            return (false, "Plan not found.");
        }

        plan.IsActive = false;
        await _context.SaveChangesAsync();

        InvalidateCaches();

        return (true, "Plan deactivated successfully.");
    }

    private void InvalidateCaches()
    {
        _cache.Remove(CacheKeyAllPlans);
        _cache.Remove(CacheKeyAnalyticsDashboard);
    }
}
