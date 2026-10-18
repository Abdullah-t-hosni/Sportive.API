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
    public int? VerifiedCount { get; set; }
    public int? IssueCount { get; set; }
    public int? TotalItemsCount { get; set; }
}

public class CloseInventoryChecklistDto
{
    public int SessionId { get; set; }
    public string? Notes { get; set; }
    public int? VerifiedCount { get; set; }
    public int? IssueCount { get; set; }
    public int? TotalItemsCount { get; set; }
}

public class StartNewChecklistSessionDto
{
    public string Title { get; set; } = string.Empty;
    public int? BranchId { get; set; }
    public int? WarehouseId { get; set; }
    public string? Notes { get; set; }
}

public class ResetInventoryChecklistDto
{
    public int? SessionId { get; set; }
    public int? BranchId { get; set; }
    public int? WarehouseId { get; set; }
    public string? Reason { get; set; }
}

public class UpdateInventoryChecklistSessionDto
{
    public string? Title { get; set; }
    public string? Notes { get; set; }
    public int? WarehouseId { get; set; }
    public int? BranchId { get; set; }
    public bool? IsActive { get; set; }
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
                activeSession.StartedAt,
                activeSession.ClosedAt,
                activeSession.ClosedByUserId,
                activeSession.ClosedByName,
                activeSession.VerifiedCount,
                activeSession.IssueCount,
                activeSession.TotalItemsCount,
                activeSession.IsActive,
                activeSession.Notes,
                activeSession.ChecklistJson,
                activeSession.CreatedAt,
                activeSession.UpdatedAt
            }
        });
    }

    /// <summary>
    /// بدء جلسة تدقيق جديدة رسميًا مع توثيق وقت الفتح واليوزر
    /// </summary>
    [HttpPost("start")]
    public async Task<IActionResult> StartSession([FromBody] StartNewChecklistSessionDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userName = User.Identity?.Name ?? User.FindFirst("name")?.Value ?? "المدير";
        var now = TimeHelper.GetEgyptTime();

        // إغلاق أي جلسات مفتوحة حالياً لنفس المخزن/الفرع لضمان عدم التداخل
        var activeQuery = _db.InventoryChecklistSessions.Where(s => s.IsActive);
        if (dto.WarehouseId.HasValue && dto.WarehouseId.Value > 0)
            activeQuery = activeQuery.Where(s => s.WarehouseId == dto.WarehouseId.Value);
        else if (dto.BranchId.HasValue && dto.BranchId.Value > 0)
            activeQuery = activeQuery.Where(s => s.BranchId == dto.BranchId.Value);

        var openSessions = await activeQuery.ToListAsync();
        foreach (var old in openSessions)
        {
            old.IsActive = false;
            old.ClosedAt = now;
            old.ClosedByUserId = userId;
            old.ClosedByName = userName;
            old.Notes = string.IsNullOrWhiteSpace(old.Notes) ? "[تم الإغلاق لبدء جلسة جديدة]" : $"{old.Notes} | [تم الإغلاق لبدء جلسة جديدة]";
            old.UpdatedAt = now;
        }

        var defaultTitle = dto.WarehouseId.HasValue
            ? $"جلسة تدقيق مخزن {dto.WarehouseId} - {now:yyyy-MM-dd HH:mm}"
            : $"جلسة تدقيق جرد - {now:yyyy-MM-dd HH:mm}";

        var session = new InventoryChecklistSession
        {
            Title = string.IsNullOrWhiteSpace(dto.Title) ? defaultTitle : dto.Title,
            BranchId = dto.BranchId,
            WarehouseId = dto.WarehouseId,
            CreatedByUserId = userId,
            CreatedByName = userName,
            StartedAt = now,
            IsActive = true,
            Notes = dto.Notes,
            ChecklistJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.InventoryChecklistSessions.Add(session);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "تم بدء جلسة تدقيق جديدة بنجاح",
            session = new
            {
                session.Id,
                session.Title,
                session.BranchId,
                session.WarehouseId,
                session.CreatedByUserId,
                session.CreatedByName,
                session.StartedAt,
                session.ClosedAt,
                session.ClosedByUserId,
                session.ClosedByName,
                session.VerifiedCount,
                session.IssueCount,
                session.TotalItemsCount,
                session.IsActive,
                session.Notes,
                session.ChecklistJson,
                session.CreatedAt,
                session.UpdatedAt
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
            if (dto.VerifiedCount.HasValue) session.VerifiedCount = dto.VerifiedCount.Value;
            if (dto.IssueCount.HasValue) session.IssueCount = dto.IssueCount.Value;
            if (dto.TotalItemsCount.HasValue) session.TotalItemsCount = dto.TotalItemsCount.Value;
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
                StartedAt = now,
                IsActive = true,
                Notes = dto.Notes,
                ChecklistJson = dto.ChecklistJson ?? "{}",
                VerifiedCount = dto.VerifiedCount ?? 0,
                IssueCount = dto.IssueCount ?? 0,
                TotalItemsCount = dto.TotalItemsCount ?? 0,
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
                session.StartedAt,
                session.ClosedAt,
                session.ClosedByUserId,
                session.ClosedByName,
                session.VerifiedCount,
                session.IssueCount,
                session.TotalItemsCount,
                session.IsActive,
                session.Notes,
                session.ChecklistJson,
                session.CreatedAt,
                session.UpdatedAt
            }
        });
    }

    /// <summary>
    /// إنهاء وإغلاق جلسة التدقيق مع تسجيل من أغلقها وتاريخ ووقت الإغلاق
    /// </summary>
    [HttpPost("close")]
    public async Task<IActionResult> CloseSession([FromBody] CloseInventoryChecklistDto dto)
    {
        var session = await _db.InventoryChecklistSessions.FirstOrDefaultAsync(s => s.Id == dto.SessionId);
        if (session == null)
        {
            return NotFound(new { message = "جلسة التدقيق غير موجودة" });
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userName = User.Identity?.Name ?? User.FindFirst("name")?.Value ?? "المدير";
        var now = TimeHelper.GetEgyptTime();

        session.IsActive = false;
        session.ClosedAt = now;
        session.ClosedByUserId = userId;
        session.ClosedByName = userName;
        session.UpdatedAt = now;

        if (dto.VerifiedCount.HasValue) session.VerifiedCount = dto.VerifiedCount.Value;
        if (dto.IssueCount.HasValue) session.IssueCount = dto.IssueCount.Value;
        if (dto.TotalItemsCount.HasValue) session.TotalItemsCount = dto.TotalItemsCount.Value;

        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            session.Notes = string.IsNullOrWhiteSpace(session.Notes) 
                ? $"[ملاحظة الإغلاق: {dto.Notes}]" 
                : $"{session.Notes} | [ملاحظة الإغلاق: {dto.Notes}]";
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "تم إغلاق وأرشفة جلسة التدقيق بنجاح",
            session = new
            {
                session.Id,
                session.Title,
                session.BranchId,
                session.WarehouseId,
                session.CreatedByName,
                session.StartedAt,
                session.ClosedAt,
                session.ClosedByName,
                session.VerifiedCount,
                session.IssueCount,
                session.TotalItemsCount,
                session.IsActive,
                session.Notes,
                session.UpdatedAt
            }
        });
    }

    /// <summary>
    /// إعادة فتح جلسة مغلقة لمتابعة العمل عليها
    /// </summary>
    [HttpPost("reopen/{id}")]
    public async Task<IActionResult> ReopenSession(int id)
    {
        var session = await _db.InventoryChecklistSessions.FirstOrDefaultAsync(s => s.Id == id);
        if (session == null) return NotFound(new { message = "الجلسة غير موجودة" });

        var now = TimeHelper.GetEgyptTime();
        session.IsActive = true;
        session.ClosedAt = null;
        session.ClosedByUserId = null;
        session.ClosedByName = null;
        session.UpdatedAt = now;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "تم إعادة فتح الجلسة بنجاح",
            session = new
            {
                session.Id,
                session.Title,
                session.BranchId,
                session.WarehouseId,
                session.CreatedByName,
                session.StartedAt,
                session.ClosedAt,
                session.ClosedByName,
                session.VerifiedCount,
                session.IssueCount,
                session.TotalItemsCount,
                session.IsActive,
                session.Notes,
                session.ChecklistJson,
                session.CreatedAt,
                session.UpdatedAt
            }
        });
    }

    /// <summary>
    /// جلب تفاصيل جلسة محددة بما فيها الـ ChecklistJson للاستعراض التاريخي
    /// </summary>
    [HttpGet("session/{id}")]
    public async Task<IActionResult> GetSessionById(int id)
    {
        var session = await _db.InventoryChecklistSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);

        if (session == null) return NotFound(new { message = "الجلسة غير موجودة" });

        return Ok(new
        {
            session.Id,
            session.Title,
            session.BranchId,
            session.WarehouseId,
            session.CreatedByUserId,
            session.CreatedByName,
            session.StartedAt,
            session.ClosedAt,
            session.ClosedByUserId,
            session.ClosedByName,
            session.VerifiedCount,
            session.IssueCount,
            session.TotalItemsCount,
            session.IsActive,
            session.Notes,
            session.ChecklistJson,
            session.CreatedAt,
            session.UpdatedAt
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
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userName = User.Identity?.Name ?? User.FindFirst("name")?.Value ?? "المدير";

        foreach (var s in sessionsToClose)
        {
            s.IsActive = false;
            s.ClosedAt = now;
            s.ClosedByUserId = userId;
            s.ClosedByName = userName;
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
    /// سجل جلسات تدقيق الجرد السابقة والحالية
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
            .Take(100)
            .Select(s => new
            {
                s.Id,
                s.Title,
                s.BranchId,
                s.WarehouseId,
                s.CreatedByUserId,
                s.CreatedByName,
                s.StartedAt,
                s.ClosedAt,
                s.ClosedByUserId,
                s.ClosedByName,
                s.VerifiedCount,
                s.IssueCount,
                s.TotalItemsCount,
                s.IsActive,
                s.Notes,
                s.CreatedAt,
                s.UpdatedAt
            })
            .ToListAsync();

        return Ok(list);
    }

    /// <summary>
    /// تعديل بيانات جلسة الجرد (المسمى، الملاحظات، المخزن، الحالة)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateSession(int id, [FromBody] UpdateInventoryChecklistSessionDto dto)
    {
        var session = await _db.InventoryChecklistSessions.FirstOrDefaultAsync(s => s.Id == id);
        if (session == null) return NotFound(new { message = "الجلسة غير موجودة" });

        if (!string.IsNullOrWhiteSpace(dto.Title))
            session.Title = dto.Title.Trim();

        if (dto.Notes != null)
            session.Notes = dto.Notes.Trim();

        if (dto.WarehouseId.HasValue)
            session.WarehouseId = dto.WarehouseId.Value > 0 ? dto.WarehouseId.Value : null;

        if (dto.BranchId.HasValue)
            session.BranchId = dto.BranchId.Value > 0 ? dto.BranchId.Value : null;

        if (dto.IsActive.HasValue)
        {
            session.IsActive = dto.IsActive.Value;
            if (!session.IsActive && !session.ClosedAt.HasValue)
            {
                session.ClosedAt = TimeHelper.GetEgyptTime();
                session.ClosedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                session.ClosedByName = User.Identity?.Name ?? User.FindFirst("name")?.Value ?? "المدير";
            }
            else if (session.IsActive)
            {
                session.ClosedAt = null;
                session.ClosedByUserId = null;
                session.ClosedByName = null;
            }
        }

        session.UpdatedAt = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "تم تعديل بيانات الجلسة بنجاح",
            session = new
            {
                session.Id,
                session.Title,
                session.BranchId,
                session.WarehouseId,
                session.CreatedByName,
                session.StartedAt,
                session.ClosedAt,
                session.ClosedByName,
                session.VerifiedCount,
                session.IssueCount,
                session.TotalItemsCount,
                session.IsActive,
                session.Notes,
                session.CreatedAt,
                session.UpdatedAt
            }
        });
    }

    /// <summary>
    /// حذف جلسة تدقيق الجرد نهائيًا
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSession(int id)
    {
        var session = await _db.InventoryChecklistSessions.FirstOrDefaultAsync(s => s.Id == id);
        if (session == null) return NotFound(new { message = "الجلسة غير موجودة" });

        _db.InventoryChecklistSessions.Remove(session);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Checklist session {SessionId} ('{Title}') deleted by user {User}", 
            id, session.Title, User.Identity?.Name);

        return Ok(new
        {
            success = true,
            message = $"تم حذف الجلسة '{session.Title}' بنجاح"
        });
    }
}
