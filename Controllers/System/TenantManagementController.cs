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
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

[Route("api/system/tenant-management")]
[ApiController]
[Authorize(Roles = "SuperAdmin")]
public class TenantManagementController : ControllerBase
{
    private readonly ITenantRegistry _tenantRegistry;
    private readonly ITenantContext _tenantContext;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ILogger<TenantManagementController> _logger;
    private readonly MasterDbContext _masterContext;

    public TenantManagementController(
        ITenantRegistry tenantRegistry, 
        ITenantContext tenantContext, 
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILogger<TenantManagementController> logger,
        MasterDbContext masterContext)
    {
        _tenantRegistry = tenantRegistry;
        _tenantContext = tenantContext;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _masterContext = masterContext;
    }

    [HttpPost("migrate-tenant")]
    public async Task<IActionResult> MigrateTenant([FromQuery] string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return BadRequest(new { success = false, message = "Slug is required." });

        // 1. Fetch tenant from master registry
        var tenant = await _tenantRegistry.GetTenantBySlugAsync(slug);
        if (tenant == null)
            return NotFound(new { success = false, message = $"Tenant with slug '{slug}' not found." });

        if (tenant.Status != TenantStatus.Active)
            return BadRequest(new { success = false, message = "Cannot migrate a non-active tenant." });

        // 2. Set the current request's tenant context so the DB Factory builds the correct connection string
        _tenantContext.SetTenant(tenant);

        try
        {
            _logger.LogInformation("Running migration for tenant {Tenant}", slug);

            // 3. Create context and run migrations
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
    public async Task<IActionResult> OnboardTenant([FromBody] TenantDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.DatabasePassword))
            return BadRequest(new { success = false, message = "Database password is required." });

        if (await _masterContext.Tenants.AnyAsync(t => t.Slug == dto.Slug))
            return BadRequest(new { success = false, message = "Tenant slug already exists." });

        if (await _masterContext.Tenants.AnyAsync(t => t.Subdomain == dto.Subdomain))
            return BadRequest(new { success = false, message = "Tenant subdomain already exists." });

        if (await _masterContext.Tenants.AnyAsync(t => t.DatabaseName == dto.DatabaseName))
            return BadRequest(new { success = false, message = "Database name already exists." });

        if (await _masterContext.Tenants.AnyAsync(t => t.DatabaseUser == dto.DatabaseUser))
            return BadRequest(new { success = false, message = "Database user already exists." });

        var newTenant = new Tenant
        {
            TenantGuid = Guid.NewGuid(),
            Slug = dto.Slug,
            Name = dto.Name,
            Subdomain = dto.Subdomain,
            DatabaseName = dto.DatabaseName,
            DatabaseUser = dto.DatabaseUser,
            DatabasePassword = dto.DatabasePassword,
            Status = TenantStatus.Active,
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        _masterContext.Tenants.Add(newTenant);
        await _masterContext.SaveChangesAsync();

        return Ok(new { success = true, message = $"Tenant '{dto.Slug}' onboarded successfully. You can now run migrations for it." });
    }

    public class TenantDto
    {
        [Required]
        public string Slug { get; set; } = string.Empty;
        
        [Required]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        public string Subdomain { get; set; } = string.Empty;
        
        [Required]
        public string DatabaseName { get; set; } = string.Empty;
        
        [Required]
        public string DatabaseUser { get; set; } = string.Empty;
        
        [Required]
        public string DatabasePassword { get; set; } = string.Empty;
    }
}
