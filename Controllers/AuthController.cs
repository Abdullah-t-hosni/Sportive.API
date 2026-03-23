using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly AppDbContext _db;

    public AuthController(IAuthService auth, AppDbContext db)
    {
        _auth = auth;
        _db = db;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        try { return Ok(await _auth.RegisterAsync(dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        try { return Ok(await _auth.LoginAsync(dto)); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _auth.ChangePasswordAsync(userId, dto);
        return result ? Ok(new { message = "Password changed successfully" }) : BadRequest(new { message = "Failed to change password" });
    }

    /// <summary>يرجع customerId للمستخدم المسجل حالياً</summary>
    [Authorize]
    [HttpGet("customer-id")]
    public async Task<IActionResult> GetCustomerId()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var customer = await _db.Customers
            .Where(c => c.AppUserId == userId && !c.IsDeleted)
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync();

        if (customer == null)
            return NotFound(new { message = "Customer profile not found" });

        return Ok(new { customerId = customer.Id });
    }

    /// <summary>بيانات المستخدم الحالي</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var userId   = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var email    = User.FindFirstValue(ClaimTypes.Email)!;
        var roles    = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        var fullName = $"{User.FindFirstValue(ClaimTypes.GivenName)} {User.FindFirstValue(ClaimTypes.Surname)}".Trim();

        var customer = await _db.Customers
            .Where(c => c.AppUserId == userId && !c.IsDeleted)
            .Select(c => new { c.Id, c.Phone })
            .FirstOrDefaultAsync();

        return Ok(new {
            userId,
            email,
            fullName,
            roles,
            customerId = customer?.Id,
            phone      = customer?.Phone
        });
    }
}
