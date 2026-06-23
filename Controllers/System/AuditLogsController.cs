using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Sportive.API.Controllers;

[Route("api/system/audit-logs")]
[ApiController]
[Authorize(Roles = "SuperAdmin,Admin")]
public class AuditLogsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly Sportive.API.Interfaces.ITenantContext _tenantContext;

    public AuditLogsController(AppDbContext db, IWebHostEnvironment env, Sportive.API.Interfaces.ITenantContext tenantContext)
    {
        _db = db;
        _env = env;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] string? userId,
        [FromQuery] string? fromDate,
        [FromQuery] string? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(action))
            query = query.Where(x => x.Action.Contains(action));

        if (!string.IsNullOrEmpty(entityType))
            query = query.Where(x => x.EntityType.Contains(entityType));

        if (!string.IsNullOrEmpty(entityId))
            query = query.Where(x => x.EntityId == entityId);

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(x => x.UserId == userId);

        if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var fromDt))
            query = query.Where(x => x.CreatedAt >= fromDt);

        if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var toDt))
        {
            toDt = toDt.Date.AddDays(1).AddTicks(-1);
            query = query.Where(x => x.CreatedAt <= toDt);
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = items
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetLogById(int id)
    {
        var log = await _db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (log == null) return NotFound("Audit log not found");
        return Ok(log);
    }

    [HttpPost("archive")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> ArchiveOldLogs()
    {
        var oneMonthAgo = DateTime.UtcNow.AddMonths(-1);
        var oldLogs = await _db.AuditLogs
            .Where(x => x.CreatedAt < oneMonthAgo)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        if (!oldLogs.Any())
        {
            return Ok(new { message = "No logs to archive.", count = 0 });
        }

        var prefix = _tenantContext.CurrentTenant?.Slug?.ToLowerInvariant() ?? "global";
        var backupDir = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), prefix, "backups", "audit_logs");
        if (!Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        var fileName = $"audit_logs_archive_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        var filePath = Path.Combine(backupDir, fileName);

        var json = JsonSerializer.Serialize(oldLogs, new JsonSerializerOptions { WriteIndented = true });
        await System.IO.File.WriteAllTextAsync(filePath, json);

        _db.AuditLogs.RemoveRange(oldLogs);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Archive successful", count = oldLogs.Count, file = fileName });
    }
}
