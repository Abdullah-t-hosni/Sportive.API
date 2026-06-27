using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/system/[controller]")]
public class BillingAnalyticsController : ControllerBase
{
    private readonly MasterDbContext _context;

    public BillingAnalyticsController(MasterDbContext context)
    {
        _context = context;
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> GetBillingMetrics()
    {
        // Monthly Recurring Revenue (MRR)
        // Assume active subscriptions with monthly plan price
        var activeSubscriptions = await _context.TenantSubscriptions
            .Include(ts => ts.Plan)
            .Where(ts => ts.IsActive)
            .ToListAsync();

        var mrr = activeSubscriptions.Sum(ts => ts.Plan?.MonthlyPrice ?? 0);
        var arr = mrr * 12; // Annual Recurring Revenue

        // Basic Churn Calculation (just mock logic for now, e.g. cancelled within last 30 days)
        var totalSubscriptions = await _context.TenantSubscriptions.CountAsync();
        var cancelledSubscriptions = await _context.TenantSubscriptions
            .Where(ts => !ts.IsActive && ts.ExpiresAt > DateTime.UtcNow.AddDays(-30))
            .CountAsync();

        var churnRate = totalSubscriptions == 0 ? 0 : Math.Round((decimal)cancelledSubscriptions / totalSubscriptions * 100, 2);

        return Ok(new
        {
            mrr = mrr,
            arr = arr,
            churnRate = churnRate,
            activeSubscriptionsCount = activeSubscriptions.Count,
            lifetimeValue = mrr * 24 // Example: average 2 years retention
        });
    }
}
