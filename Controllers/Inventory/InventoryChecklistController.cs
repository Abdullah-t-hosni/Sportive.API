using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Controllers.Inventory;

public class SaveInventoryChecklistDto
{
    public int? Id { get; set; }
    public string Title { get; set; } = "جلسة تدقيق الجرد";
    public int? BranchId { get; set; }
    public int? WarehouseId { get; set; }
    public string? Notes { get; set; }
    public string ChecklistJson { get; set; } = "{}";
}

public class ResetInventoryChecklistDto
{
    public int? SessionId { get; set; }
    public int? BranchId { get; set; }
    public int? WarehouseId { get; set; }
    public string? Reason { get; set; }
}

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InventoryChecklistController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<InventoryChecklistController> _logger;

    public InventoryChecklistController(AppDbContext db, ILogger<InventoryChecklistController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// جلب الجلسة النشطة الحالية للمخزن/الفرع المحدد
    /// </summary>
    [HttpGet("active")]
    public async Task<IActionResult> GetActiveSession([FromQuery] int? warehouseId, [FromQuery] int? branchId)
    {
        var query = _db.InventoryChecklistSessions
            .AsNoTracking()
            .Where(s => s.IsActive);

        if (warehouseId.HasValue && warehouseId.Value > 0)
        {
            query = query.Where(s => s.WarehouseId == warehouseId.Value);
        }
        else if (branchId.HasValue && branchId.Value > 0)
        {
            query = query.Where(s => s.BranchId == branchId.Value);
        }

        var activeSession = await query
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .FirstOrDefaultAsync();

        if (activeSession == null)
        {
            return Ok(new
            {
                exists = false,
                session = (object?)null
            });
        }

        return Ok(new
        {
            exists = true,
            session = new
            {
                activeSession.Id,
                activeSession.Title,
                activeSession.BranchId,
                activeSession.WarehouseId,
                activeSession.CreatedByUserId,
                activeSession.CreatedByName,
                activeSession.IsActive,
                activeSession.Notes,
                activeSession.ChecklistJson,
                activeSession.CreatedAt,
                activeSession.UpdatedAt
            }
        });
    }

    /// <summary>
    /// حفظ أو تحديث حالة جلسة الجرد والمنتجات المدققة (Checklist)
    /// </summary>
    [HttpPost("save")]
    public async Task<IActionResult> SaveSession([FromBody] SaveInventoryChecklistDto dto)
    {
        if (dto == null) return BadRequest(new { message = "بيانات غير صالحة" });

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userName = User.Identity?.Name ?? User.FindFirst("name")?.Value ?? "المدير";

        InventoryChecklistSession? session = null;

        if (dto.Id.HasValue && dto.Id.Value > 0)
        {
            session = await _db.InventoryChecklistSessions
                .FirstOrDefaultAsync(s => s.Id == dto.Id.Value);
        }

        // إذا لم نجدها بالـ ID، نبحث عن جلسة نشطة لنفس المخزن/الفرع
        if (session == null && (dto.WarehouseId.HasValue || dto.BranchId.HasValue))
        {
            var query = _db.InventoryChecklistSessions.Where(s => s.IsActive);
            if (dto.WarehouseId.HasValue && dto.WarehouseId.Value > 0)
                query = query.Where(s => s.WarehouseId == dto.WarehouseId.Value);
            else if (dto.BranchId.HasValue && dto.BranchId.Value > 0)
                query = query.Where(s => s.BranchId == dto.BranchId.Value);

            session = await query.OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt).FirstOrDefaultAsync();
        }

        var now = TimeHelper.GetEgyptTime();

        if (session != null)
        {
            // تحديث جلسة موجودة
            if (!string.IsNullOrWhiteSpace(dto.Title)) session.Title = dto.Title;
            if (dto.BranchId.HasValue) session.BranchId = dto.BranchId;
            if (dto.WarehouseId.HasValue) session.WarehouseId = dto.WarehouseId;
            session.Notes = dto.Notes;
            session.ChecklistJson = dto.ChecklistJson ?? "{}";
            session.UpdatedAt = now;
            session.IsActive = true;
        }
        else
        {
            // إنشاء جلسة جديدة
            session = new InventoryChecklistSession
            {
                Title = string.IsNullOrWhiteSpace(dto.Title) ? $"جرد مخزن {dto.WarehouseId} - {now:yyyy-MM-dd}" : dto.Title,
                BranchId = dto.BranchId,
                WarehouseId = dto.WarehouseId,
                CreatedByUserId = userId,
                CreatedByName = userName,
                IsActive = true,
                Notes = dto.Notes,
                ChecklistJson = dto.ChecklistJson ?? "{}",
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.InventoryChecklistSessions.Add(session);
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "تم حفظ جلسة تدقيق الجرد بنجاح",
            session = new
            {
                session.Id,
                session.Title,
                session.BranchId,
                session.WarehouseId,
                session.CreatedByUserId,
                session.CreatedByName,
                session.IsActive,
                session.Notes,
                session.ChecklistJson,
                session.CreatedAt,
                session.UpdatedAt
            }
        });
    }

    /// <summary>
    /// إعادة ضبط / أرشفة الجلسة الحالية للبدء في جرد جديد
    /// </summary>
    [HttpPost("reset")]
    public async Task<IActionResult> ResetSession([FromBody] ResetInventoryChecklistDto dto)
    {
        var query = _db.InventoryChecklistSessions.Where(s => s.IsActive);

        if (dto.SessionId.HasValue && dto.SessionId.Value > 0)
        {
            query = query.Where(s => s.Id == dto.SessionId.Value);
        }
        else
        {
            if (dto.WarehouseId.HasValue && dto.WarehouseId.Value > 0)
                query = query.Where(s => s.WarehouseId == dto.WarehouseId.Value);
            else if (dto.BranchId.HasValue && dto.BranchId.Value > 0)
                query = query.Where(s => s.BranchId == dto.BranchId.Value);
        }

        var sessionsToClose = await query.ToListAsync();
        var now = TimeHelper.GetEgyptTime();

        foreach (var s in sessionsToClose)
        {
            s.IsActive = false;
            s.UpdatedAt = now;
            if (!string.IsNullOrWhiteSpace(dto.Reason))
            {
                s.Notes = string.IsNullOrWhiteSpace(s.Notes) ? $"[تم الإنهاء: {dto.Reason}]" : $"{s.Notes} | [تم الإنهاء: {dto.Reason}]";
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = $"تم أرشفة {sessionsToClose.Count} جلسة والبدء من جديد."
        });
    }

    /// <summary>
    /// سجل جلسات تدقيق الجرد السابقة
    /// </summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions([FromQuery] int? warehouseId, [FromQuery] int? branchId)
    {
        var query = _db.InventoryChecklistSessions
            .AsNoTracking();

        if (warehouseId.HasValue && warehouseId.Value > 0)
            query = query.Where(s => s.WarehouseId == warehouseId.Value);
        else if (branchId.HasValue && branchId.Value > 0)
            query = query.Where(s => s.BranchId == branchId.Value);

        var list = await query
            .OrderByDescending(s => s.CreatedAt)
            .Take(50)
            .Select(s => new
            {
                s.Id,
                s.Title,
                s.BranchId,
                s.WarehouseId,
                s.CreatedByName,
                s.IsActive,
                s.Notes,
                s.CreatedAt,
                s.UpdatedAt
            })
            .ToListAsync();

        return Ok(list);
    }
}
