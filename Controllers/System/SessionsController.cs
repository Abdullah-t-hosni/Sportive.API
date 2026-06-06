using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Utils;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SessionsController : ControllerBase
{
    private readonly AppDbContext _db;

    public SessionsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActiveSessions()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var currentSessionIdClaim = User.FindFirst("SessionId")?.Value;
        Guid.TryParse(currentSessionIdClaim, out var currentSessionId);

        var sessions = await _db.UserSessions
            .Where(s => s.UserId == userId && !s.IsRevoked && (s.ExpiresAt == null || s.ExpiresAt > DateTime.UtcNow))
            .OrderByDescending(s => s.LastSeen)
            .Select(s => new
            {
                s.Id,
                s.DeviceName,
                s.IpAddress,
                s.CreatedAt,
                s.LastSeen,
                s.ExpiresAt,
                IsCurrent = s.Id == currentSessionId
            })
            .ToListAsync();

        return Ok(sessions);
    }

    [HttpPost("logout/{id}")]
    public async Task<IActionResult> LogoutSession(Guid id)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var session = await _db.UserSessions
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

        if (session == null)
            return NotFound(new { message = "Session not found." });

        if (!session.IsRevoked)
        {
            session.IsRevoked = true;
            session.RevokedAt = TimeHelper.GetEgyptTime();
            await _db.SaveChangesAsync();
        }

        return Ok(new { message = "Session revoked successfully." });
    }

    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAllSessions()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var currentSessionIdClaim = User.FindFirst("SessionId")?.Value;
        Guid.TryParse(currentSessionIdClaim, out var currentSessionId);

        var otherSessions = await _db.UserSessions
            .Where(s => s.UserId == userId && !s.IsRevoked && s.Id != currentSessionId)
            .ToListAsync();

        foreach (var session in otherSessions)
        {
            session.IsRevoked = true;
            session.RevokedAt = TimeHelper.GetEgyptTime();
        }

        if (otherSessions.Count > 0)
        {
            await _db.SaveChangesAsync();
        }

        return Ok(new { message = "All other sessions revoked successfully." });
    }
}
