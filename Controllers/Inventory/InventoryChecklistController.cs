using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;

namespace Sportive.API.Controllers.Inventory;

public class NotifyInventoryNoteDto
{
    public int ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? ProductSku { get; set; }
    public string NoteText { get; set; } = string.Empty;
    public string? ReplyToUserId { get; set; }
    public string? ReplyToUserName { get; set; }
    public List<string>? ParticipantUserIds { get; set; }
    public List<string>? ParticipantUserNames { get; set; }
    public bool IsResolution { get; set; }
}

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
    private readonly INotificationService _notificationService;

    public InventoryChecklistController(
        AppDbContext db, 
        ILogger<InventoryChecklistController> logger,
        INotificationService notificationService)
    {
        _db = db;
        _logger = logger;
        _notificationService = notificationService;
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
            query = query.Where(s => s.WarehouseId == warehouseId.Value || s.WarehouseId == null);
        }
        else if (branchId.HasValue && branchId.Value > 0)
        {
            query = query.Where(s => s.BranchId == branchId.Value || s.BranchId == null);
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

        // إذا لم نجدها بالـ ID، نبحث عن الجلسة النشطة
        if (session == null)
        {
            var query = _db.InventoryChecklistSessions.Where(s => s.IsActive);
            if (dto.WarehouseId.HasValue && dto.WarehouseId.Value > 0)
                query = query.Where(s => s.WarehouseId == dto.WarehouseId.Value || s.WarehouseId == null);
            else if (dto.BranchId.HasValue && dto.BranchId.Value > 0)
                query = query.Where(s => s.BranchId == dto.BranchId.Value || s.BranchId == null);

            session = await query.OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt).FirstOrDefaultAsync();
        }

        if (session == null)
        {
            return BadRequest(new { message = "لا توجد جلسة تدقيق محددة أو نشطة للحفظ. يرجى اختيار جلسة من القائمة أو بدء جلسة جديدة." });
        }

        var now = TimeHelper.GetEgyptTime();

        // تحديث الجلسة الموجودة
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
            query = query.Where(s => s.WarehouseId == warehouseId.Value || s.WarehouseId == null);
        else if (branchId.HasValue && branchId.Value > 0)
            query = query.Where(s => s.BranchId == branchId.Value || s.BranchId == null);

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

    /// <summary>
    /// إرسال إشعار لحظي للمستخدمين عند الرد على ملاحظة أو إضافة ملاحظة في صنف به ملاحظات سابقة
    /// </summary>
    [HttpPost("notify-note")]
    public async Task<IActionResult> NotifyAuditNote([FromBody] NotifyInventoryNoteDto dto)
    {
        if (dto == null || dto.ProductId <= 0 || string.IsNullOrWhiteSpace(dto.NoteText))
        {
            return BadRequest(new { message = "بيانات غير مكتملة للإشعار" });
        }

        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var currentUserName = User.Identity?.Name ?? User.FindFirst("name")?.Value ?? "مستخدم";

        var productName = !string.IsNullOrWhiteSpace(dto.ProductName) 
            ? dto.ProductName 
            : (!string.IsNullOrWhiteSpace(dto.ProductSku) ? dto.ProductSku : $"صنف #{dto.ProductId}");
        var auditLink = $"/admin/inventory-audit?productId={dto.ProductId}&search={dto.ProductSku ?? dto.ProductId.ToString()}";

        var notifiedUserIds = new HashSet<string>();

        bool isDirectReply = !string.IsNullOrWhiteSpace(dto.ReplyToUserId) || !string.IsNullOrWhiteSpace(dto.ReplyToUserName);

        // الحالة الأولى: إذا كان ردًا مباشرًا على ملاحظة مستخدم محدد (يصل للشخص المعني بالرد فقط ولا يصل لأي شخص آخر)
        if (isDirectReply)
        {
            string? targetUserId = dto.ReplyToUserId;

            if (string.IsNullOrWhiteSpace(targetUserId) && !string.IsNullOrWhiteSpace(dto.ReplyToUserName))
            {
                var targetUser = await _db.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.FullName == dto.ReplyToUserName || u.UserName == dto.ReplyToUserName);
                if (targetUser != null)
                {
                    targetUserId = targetUser.Id;
                }
            }

            // التأكد من أن المستلم ليس هو نفسه كاتب الرد الحالي، وأنه يوزر حقيقي ومحدد
            if (!string.IsNullOrWhiteSpace(targetUserId) && targetUserId != currentUserId)
            {
                var replyTitleAr = "رد جديد على ملاحظتك في تدقيق المخزون 💬";
                var replyTitleEn = "New Reply on Your Audit Note";
                var replyMsgAr = dto.IsResolution
                    ? $"قام {currentUserName} بحل المشكلة والرد على ملاحظتك في صنف ({productName}): {dto.NoteText}"
                    : $"قام {currentUserName} بالرد على ملاحظتك في صنف ({productName}): {dto.NoteText}";
                var replyMsgEn = dto.IsResolution
                    ? $"{currentUserName} resolved the issue and replied on ({productName}): {dto.NoteText}"
                    : $"{currentUserName} replied on your note for ({productName}): {dto.NoteText}";

                try
                {
                    await _notificationService.SendAsync(
                        userId: targetUserId,
                        titleAr: replyTitleAr,
                        titleEn: replyTitleEn,
                        msgAr: replyMsgAr,
                        msgEn: replyMsgEn,
                        type: "AuditNote",
                        orderId: null,
                        link: auditLink
                    );
                    notifiedUserIds.Add(targetUserId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send audit reply notification to user {UserId}", targetUserId);
                }
            }

            // في حالة الرد المباشر: ننتهي هنا ولا نرسل لباقي المشاركين!
            return Ok(new
            {
                success = true,
                isDirectReply = true,
                notifiedCount = notifiedUserIds.Count,
                notifiedUserIds = notifiedUserIds.ToList()
            });
        }

        // الحالة الثانية: إذا كانت ملاحظة جديدة أضافها مستخدم على صنف كان قد دوّن فيه مستخدمون آخرون ملاحظات سابقة
        // نرسل فقط للمستخدمين السابقين أصحاب الملاحظات على نفس الصنف (باستثناء كاتب الملاحظة الحالي)
        if (dto.ParticipantUserIds != null && dto.ParticipantUserIds.Any())
        {
            foreach (var participantId in dto.ParticipantUserIds.Distinct())
            {
                if (string.IsNullOrWhiteSpace(participantId) || 
                    participantId == currentUserId || 
                    notifiedUserIds.Contains(participantId))
                {
                    continue;
                }

                var noteTitleAr = "ملاحظة جديدة في تدقيق صنف شاركت به 📝";
                var noteTitleEn = "New Note on Product You Audited";
                var noteMsgAr = dto.IsResolution
                    ? $"قام {currentUserName} بحل المشكلة وتدوين توضيح في صنف ({productName}): {dto.NoteText}"
                    : $"أضاف {currentUserName} ملاحظة جديدة في صنف ({productName}): {dto.NoteText}";
                var noteMsgEn = $"{currentUserName} added a note on ({productName}): {dto.NoteText}";

                try
                {
                    await _notificationService.SendAsync(
                        userId: participantId,
                        titleAr: noteTitleAr,
                        titleEn: noteTitleEn,
                        msgAr: noteMsgAr,
                        msgEn: noteMsgEn,
                        type: "AuditNote",
                        orderId: null,
                        link: auditLink
                    );
                    notifiedUserIds.Add(participantId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send audit participant notification to user {UserId}", participantId);
                }
            }
        }

        // 3. التحقق من الأسماء لو كانت بعض الملاحظات القديمة لا تحتوي على UserIds
        if (dto.ParticipantUserNames != null && dto.ParticipantUserNames.Any())
        {
            foreach (var participantName in dto.ParticipantUserNames.Distinct())
            {
                if (string.IsNullOrWhiteSpace(participantName) || participantName == currentUserName)
                    continue;

                try
                {
                    var user = await _db.Users
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.FullName == participantName || u.UserName == participantName);

                    if (user != null && user.Id != currentUserId && !notifiedUserIds.Contains(user.Id))
                    {
                        var noteTitleAr = "ملاحظة جديدة في تدقيق صنف شاركت به 📝";
                        var noteTitleEn = "New Note on Product You Audited";
                        var noteMsgAr = dto.IsResolution
                            ? $"قام {currentUserName} بحل المشكلة وتدوين توضيح في صنف ({productName}): {dto.NoteText}"
                            : $"أضاف {currentUserName} ملاحظة جديدة في صنف ({productName}): {dto.NoteText}";
                        var noteMsgEn = $"{currentUserName} added a note on ({productName}): {dto.NoteText}";

                        await _notificationService.SendAsync(
                            userId: user.Id,
                            titleAr: noteTitleAr,
                            titleEn: noteTitleEn,
                            msgAr: noteMsgAr,
                            msgEn: noteMsgEn,
                            type: "AuditNote",
                            orderId: null,
                            link: auditLink
                        );
                        notifiedUserIds.Add(user.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to lookup participant {UserName} for audit notification", participantName);
                }
            }
        }

        return Ok(new
        {
            success = true,
            isDirectReply = false,
            notifiedCount = notifiedUserIds.Count,
            notifiedUserIds = notifiedUserIds.ToList()
        });
    }
}
