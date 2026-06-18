using System.Security.Claims;
using Sportive.API.Models;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Services;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.Coupons, requireEdit: true)]
public class CouponsController : ControllerBase
{
    private readonly ICouponService _coupons;
    private readonly ITranslator _t;
    private readonly IAuditService _audit;

    public CouponsController(ICouponService coupons, ITranslator t, IAuditService audit)
    {
        _coupons = coupons;
        _t = t;
        _audit = audit;
    }

    /// <summary>التحقق من كوبون خصم (public)</summary>
    [HttpPost("validate")]
    [AllowAnonymous]
    public async Task<IActionResult> Validate([FromBody] ApplyCouponRequest req)
    {
        var (valid, discount, error) = await _coupons.ValidateAsync(req.Code, req.OrderTotal);
        if (!valid) return BadRequest(new { message = error });
        return Ok(new { discount, message = string.Format(_t.Get("Coupons.DiscountApplied"), discount.ToString("N2")) });
    }

    /// <summary>كل الكوبونات (Admin)</summary>
    [HttpGet]
    [AllowPosAccess]
    public async Task<IActionResult> GetAll() =>
        Ok(await _coupons.GetAllAsync());

    /// <summary>Ã˜Â¥Ã˜Â¶Ã˜Â§Ã™ÂÃ˜Â© Ã™Æ’Ã™Ë†Ã˜Â¨Ã™Ë†Ã™â€  Ã˜Â¬Ã˜Â¯Ã™Å Ã˜Â¯ (Admin)</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCouponDto dto)
    {
        try { 
            var result = await _coupons.CreateAsync(dto); 
            try { await _audit.LogAsync("CreateCoupon", "Coupon", "", $"Created coupon {dto.Code}", User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), User.FindFirstValue(System.Security.Claims.ClaimTypes.Name)); } catch { }
            return Ok(result); 
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Ã˜ÂªÃ˜Â¹Ã˜Â¯Ã™Å Ã™â€ž Ã™Æ’Ã™Ë†Ã˜Â¨Ã™Ë†Ã™â€  (Admin)</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateCouponDto dto)
    {
        try
        {
            var result = await _coupons.UpdateAsync(id, dto);
            if (result != null) { try { await _audit.LogAsync("UpdateCoupon", "Coupon", id.ToString(), $"Updated coupon {dto.Code}", User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), User.FindFirstValue(System.Security.Claims.ClaimTypes.Name)); } catch { } }
            return result == null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Ã˜ÂªÃ™ÂÃ˜Â¹Ã™Å Ã™â€ž/Ã˜Â¥Ã™Å Ã™â€šÃ˜Â§Ã™Â Ã™Æ’Ã™Ë†Ã˜Â¨Ã™Ë†Ã™â€  (Admin)</summary>
    [HttpPatch("{id}/toggle")]
    public async Task<IActionResult> Toggle(int id)
    {
        var result = await _coupons.ToggleAsync(id);
        if (result) { try { await _audit.LogAsync("ToggleCoupon", "Coupon", id.ToString(), $"Toggled coupon", User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), User.FindFirstValue(System.Security.Claims.ClaimTypes.Name)); } catch { } }
        return result ? Ok() : NotFound();
    }

    /// <summary>Ã˜ÂªÃ˜Â¹Ã˜Â·Ã™Å Ã™â€ž Ã™Æ’Ã™Ë†Ã˜Â¨Ã™Ë†Ã™â€  (Admin)</summary>
    [HttpPatch("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var result = await _coupons.DeactivateAsync(id);
        if (result) { try { await _audit.LogAsync("DeactivateCoupon", "Coupon", id.ToString(), $"Deactivated coupon", User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), User.FindFirstValue(System.Security.Claims.ClaimTypes.Name)); } catch { } }
        return result ? Ok() : NotFound();
    }

    /// <summary>Ã˜Â­Ã˜Â°Ã™Â Ã™Æ’Ã™Ë†Ã˜Â¨Ã™Ë†Ã™â€  (Admin)</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _coupons.DeleteAsync(id);
        if (result) { try { await _audit.LogAsync("DeleteCoupon", "Coupon", id.ToString(), $"Deleted coupon", User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), User.FindFirstValue(System.Security.Claims.ClaimTypes.Name)); } catch { } }
        return result ? Ok() : NotFound();
    }
}

