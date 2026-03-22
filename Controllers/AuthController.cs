using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    public AuthController(IAuthService auth) => _auth = auth;

    /// <summary>Use this customerId for /api/cart/{customerId} after login (also returned in login/register JSON as customerId).</summary>
    [Authorize]
    [HttpGet("customer-id")]
    public async Task<IActionResult> GetMyCustomerId()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        try
        {
            var id = await _auth.GetMyCustomerIdAsync(userId);
            return id.HasValue
                ? Ok(new { customerId = id.Value })
                : NotFound(new { message = "No customer profile (account is not in Customer role)." });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
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
}
