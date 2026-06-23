using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Hubs;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WelcomeMessageController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly Sportive.API.Interfaces.ITenantContext _tenantContext;

    public WelcomeMessageController(AppDbContext db, UserManager<AppUser> userManager, IHubContext<NotificationHub> hubContext, Sportive.API.Interfaces.ITenantContext tenantContext)
    {
        _db = db;
        _userManager = userManager;
        _hubContext = hubContext;
        _tenantContext = tenantContext;
    }

    // ── Admin Endpoints ─────────────────────────────────────

    [HttpGet]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetAll()
    {
        var result = await _db.WelcomeMessages
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new {
                x.Id,
                x.Message,
                x.TargetType,
                x.TargetUserId,
                TargetUser = x.TargetUser != null ? new { x.TargetUser.FullName } : null,
                x.TargetDepartmentId,
                TargetDepartment = x.TargetDepartment != null ? new { x.TargetDepartment.Name } : null,
                x.CreatedAt,
                x.StartDate,
                x.EndDate,
                x.IsActive,
                SeenCount = _db.WelcomeMessageSeens.Count(s => s.WelcomeMessageId == x.Id),
                IsSeen = x.TargetType == WelcomeMessageTargetType.User ? _db.WelcomeMessageSeens.Any(s => s.WelcomeMessageId == x.Id && s.UserId == x.TargetUserId) : false,
                SeenUsers = _db.WelcomeMessageSeens
                    .Where(s => s.WelcomeMessageId == x.Id)
                    .Select(s => new { s.User.FullName, s.SeenAt })
                    .ToList()
            })
            .ToListAsync();

        return Ok(result);
    }

    [HttpGet("departments")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetDepartments()
    {
        var departments = await _db.Departments
            .Select(d => new { d.Id, d.Name })
            .ToListAsync();
            
        return Ok(departments);
    }

    public record CreateWelcomeMessageDto(
        string Message,
        WelcomeMessageTargetType TargetType,
        string? TargetUserId,
        int? TargetDepartmentId,
        DateTime? StartDate,
        DateTime? EndDate,
        bool ShowImmediately // Added flag
    );

    [HttpPost]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Create([FromBody] CreateWelcomeMessageDto dto)
    {
        var currentUserId = _userManager.GetUserId(User);
        
        var message = new WelcomeMessage
        {
            Message = dto.Message,
            TargetType = dto.TargetType,
            TargetUserId = dto.TargetUserId,
            TargetDepartmentId = dto.TargetDepartmentId,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            CreatedByUserId = currentUserId,
            IsActive = true
        };

        _db.WelcomeMessages.Add(message);
        await _db.SaveChangesAsync();

        // Broadcast ONLY if requested
        if (dto.ShowImmediately)
        {
            var prefix = _tenantContext.CurrentTenant?.Slug?.ToLowerInvariant() ?? "global";
            await _hubContext.Clients.Group($"{prefix}_All").SendAsync("ReceiveNotification", new {
                type = "WelcomeMessage",
                id = message.Id,
                message = message.Message,
                targetType = message.TargetType.ToString(),
                targetUserId = message.TargetUserId,
                targetDepartmentId = message.TargetDepartmentId
            });
        }

        return Ok(message);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Delete(int id)
    {
        var message = await _db.WelcomeMessages.FindAsync(id);
        if (message == null) return NotFound();

        _db.WelcomeMessages.Remove(message);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Message deleted successfully" });
    }

    // ── User Endpoints ──────────────────────────────────────

    [HttpGet("my-message")]
    public async Task<IActionResult> GetMyMessage()
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return Unauthorized();

        var roles = await _userManager.GetRolesAsync(user);
        bool isStaff = roles.Any(r => r != "Customer");
        bool isCustomer = roles.Contains("Customer");

        // Find the user's department if they are an employee
        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.AppUserId == userId);
        int? departmentId = employee?.DepartmentId;

        var now = TimeHelper.GetEgyptTime();

        var allowedTypes = new List<WelcomeMessageTargetType> { WelcomeMessageTargetType.All };
        if (isStaff) allowedTypes.Add(WelcomeMessageTargetType.Staff);
        if (isCustomer) allowedTypes.Add(WelcomeMessageTargetType.Customers);

        // Find active messages that target this user, their department, or everyone
        // and that they haven't seen yet.
        var message = await _db.WelcomeMessages
            .Where(m => m.IsActive)
            .Where(m => m.StartDate == null || m.StartDate <= now)
            .Where(m => m.EndDate == null || m.EndDate >= now)
            .Where(m => 
                allowedTypes.Contains(m.TargetType) ||
                (m.TargetType == WelcomeMessageTargetType.User && m.TargetUserId == userId) ||
                (m.TargetType == WelcomeMessageTargetType.Department && m.TargetDepartmentId == departmentId)
            )
            .Where(m => !_db.WelcomeMessageSeens.Any(s => s.WelcomeMessageId == m.Id && s.UserId == userId))
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync();

        if (message == null) return NoContent();

        return Ok(new {
            id = message.Id,
            message = message.Message
        });
    }

    [HttpPost("{id}/seen")]
    public async Task<IActionResult> MarkAsSeen(int id)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        
        var alreadySeen = await _db.WelcomeMessageSeens
            .AnyAsync(s => s.WelcomeMessageId == id && s.UserId == userId);

        if (alreadySeen) return Ok();

        var seen = new WelcomeMessageSeen
        {
            WelcomeMessageId = id,
            UserId = userId,
            SeenAt = TimeHelper.GetEgyptTime()
        };

        _db.WelcomeMessageSeens.Add(seen);
        await _db.SaveChangesAsync();

        return Ok();
    }
}
