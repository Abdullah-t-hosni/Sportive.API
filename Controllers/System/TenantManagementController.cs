using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

[Route("api/system/tenant-management")]
[ApiController]
[Authorize(Roles = "SuperAdmin")]
public class TenantManagementController : ControllerBase
{
    private readonly ITenantService _tenantService;
    private readonly ITenantRegistry _tenantRegistry;
    private readonly ITenantContext _tenantContext;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ILogger<TenantManagementController> _logger;

    public TenantManagementController(
        ITenantService tenantService,
        ITenantRegistry tenantRegistry, 
        ITenantContext tenantContext, 
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILogger<TenantManagementController> logger)
    {
        _tenantService = tenantService;
        _tenantRegistry = tenantRegistry;
        _tenantContext = tenantContext;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    [HttpPost("migrate-tenant")]
    public async Task<IActionResult> MigrateTenant([FromQuery] string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return BadRequest(new { success = false, message = "Slug is required." });

        var tenant = await _tenantRegistry.GetTenantBySlugAsync(slug);
        if (tenant == null)
            return NotFound(new { success = false, message = $"Tenant with slug '{slug}' not found." });

        if (tenant.Status != TenantStatus.Active)
            return BadRequest(new { success = false, message = "Cannot migrate a non-active tenant." });

        _tenantContext.SetTenant(tenant);

        try
        {
            _logger.LogInformation("Running migration for tenant {Tenant}", slug);
            using var context = await _dbContextFactory.CreateDbContextAsync();
            if (context.Database.IsRelational())
            {
                await context.Database.MigrateAsync();
                _logger.LogInformation("Migration completed for tenant {Tenant}", slug);
                return Ok(new { success = true, message = $"Successfully migrated database for tenant '{slug}'." });
            }
            else
            {
                return BadRequest(new { success = false, message = "Database provider is not relational, cannot migrate." });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run migrations for tenant {Tenant}", slug);
            return StatusCode(500, new { success = false, message = "Failed to run migrations." });
        }
    }

    [HttpPost("onboard")]
    public async Task<IActionResult> OnboardTenant([FromBody] OnboardTenantRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _tenantService.OnboardNewTenantAsync(request);
        
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }
}
