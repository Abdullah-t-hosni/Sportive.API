using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.DTOs.System;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[Route("api/system/tenants")]
[ApiController]
[Authorize(Roles = "SuperAdmin")]
public class TenantsController : ControllerBase
{
    private readonly ITenantService _tenantService;
    private readonly ITenantContext _tenantContext;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly MasterDbContext _masterContext;
    private readonly ILogger<TenantsController> _logger;

    public TenantsController(
        ITenantService tenantService,
        ITenantContext tenantContext,
        IDbContextFactory<AppDbContext> dbContextFactory,
        MasterDbContext masterContext,
        ILogger<TenantsController> logger)
    {
        _tenantService = tenantService;
        _tenantContext = tenantContext;
        _dbContextFactory = dbContextFactory;
        _masterContext = masterContext;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllTenants([FromQuery] TenantQueryDto query)
    {
        var response = await _tenantService.GetAllTenantsAsync(query);
        return Ok(new { success = true, data = response });
    }

    [HttpGet("{tenantGuid}")]
    public async Task<IActionResult> GetTenantById(Guid tenantGuid)
    {
        var tenant = await _tenantService.GetTenantByIdAsync(tenantGuid);
        if (tenant == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        return Ok(new { success = true, data = tenant });
    }

    [HttpPut("{tenantGuid}")]
    public async Task<IActionResult> UpdateTenant(Guid tenantGuid, [FromBody] UpdateTenantDto request)
    {
        var (success, message) = await _tenantService.UpdateTenantAsync(tenantGuid, request);
        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message });
    }

    [HttpGet("{tenantGuid}/usage")]
    public async Task<IActionResult> GetTenantUsage(Guid tenantGuid)
    {
        var usage = await _tenantService.GetTenantUsageAsync(tenantGuid);
        if (usage == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        return Ok(new { success = true, data = usage });
    }

    [HttpPut("{tenantGuid}/lock")]
    public async Task<IActionResult> LockTenant(Guid tenantGuid, [FromBody] LockTenantRequest request)
    {
        var (success, message) = await _tenantService.LockTenantAsync(tenantGuid, request.Reason);
        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message });
    }

    [HttpPut("{tenantGuid}/unlock")]
    public async Task<IActionResult> UnlockTenant(Guid tenantGuid)
    {
        var (success, message) = await _tenantService.UnlockTenantAsync(tenantGuid);
        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message });
    }

    [HttpPost("{tenantGuid}/migrate")]
    public async Task<IActionResult> MigrateTenant(Guid tenantGuid)
    {
        var tenant = await _masterContext.Tenants.FirstOrDefaultAsync(t => t.TenantGuid == tenantGuid);
        if (tenant == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        if (tenant.Status != Sportive.API.Models.TenantStatus.Active)
            return BadRequest(new { success = false, message = "Cannot migrate a non-active tenant." });

        _tenantContext.SetTenant(tenant);

        try
        {
            _logger.LogInformation("Running migration for tenant {Tenant}", tenant.Slug);
            using var context = await _dbContextFactory.CreateDbContextAsync();
            if (context.Database.IsRelational())
            {
                var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
                int count = pendingMigrations.Count();

                await context.Database.MigrateAsync();
                
                _logger.LogInformation("Migration completed for tenant {Tenant}", tenant.Slug);
                return Ok(new
                {
                    success = true,
                    tenantGuid = tenantGuid,
                    appliedMigrations = count,
                    message = "Tenant database migrated successfully."
                });
            }
            else
            {
                return BadRequest(new { success = false, message = "Database provider is not relational, cannot migrate." });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run migrations for tenant {Tenant}", tenant.Slug);
            return StatusCode(500, new { success = false, message = "Failed to run migrations." });
        }
    }
}

public class LockTenantRequest
{
    public string? Reason { get; set; }
}
