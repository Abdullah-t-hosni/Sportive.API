using Sportive.API.Models;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Services;
using Sportive.API.DTOs;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.Coupons, requireEdit: true)]
public class CouponsController : ControllerBase
{
    private readonly ICouponService _coupons;
    public CouponsController(ICouponService coupons) => _coupons = coupons;

    /// <summary>Ø§Ù„ØªØ­Ù‚Ù‚ Ù…Ù† ÙƒÙˆØ¨ÙˆÙ† Ø®ØµÙ… (public)</summary>
    [HttpPost("validate")]
    [AllowAnonymous]
    public async Task<IActionResult> Validate([FromBody] ApplyCouponRequest req)
    {
        var (valid, discount, error) = await _coupons.ValidateAsync(req.Code, req.OrderTotal);
        if (!valid) return BadRequest(new { message = error });
        return Ok(new { discount, message = $"ØªÙ… ØªØ·Ø¨ÙŠÙ‚ Ø®ØµÙ… {discount:N2} Ø¬.Ù…" });
    }

    /// <summary>ÙƒÙ„ Ø§Ù„ÙƒÙˆØ¨ÙˆÙ†Ø§Øª (Admin)</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _coupons.GetAllAsync());

    /// <summary>Ø¥Ø¶Ø§ÙØ© ÙƒÙˆØ¨ÙˆÙ† Ø¬Ø¯ÙŠØ¯ (Admin)</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCouponDto dto)
    {
        try { return Ok(await _coupons.CreateAsync(dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>ØªØ¹Ø¯ÙŠÙ„ ÙƒÙˆØ¨ÙˆÙ† (Admin)</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateCouponDto dto)
    {
        try
        {
            var result = await _coupons.UpdateAsync(id, dto);
            return result == null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>ØªÙØ¹ÙŠÙ„/Ø¥ÙŠÙ‚Ø§Ù ÙƒÙˆØ¨ÙˆÙ† (Admin)</summary>
    [HttpPatch("{id}/toggle")]
    public async Task<IActionResult> Toggle(int id) =>
        await _coupons.ToggleAsync(id) ? Ok() : NotFound();

    /// <summary>ØªØ¹Ø·ÙŠÙ„ ÙƒÙˆØ¨ÙˆÙ† (Admin)</summary>
    [HttpPatch("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(int id) =>
        await _coupons.DeactivateAsync(id) ? Ok() : NotFound();

    /// <summary>Ø­Ø°Ù ÙƒÙˆØ¨ÙˆÙ† (Admin)</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id) =>
        await _coupons.DeleteAsync(id) ? Ok() : NotFound();
}

