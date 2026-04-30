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

    public CouponsController(ICouponService coupons, ITranslator t)
    {
        _coupons = coupons;
        _t = t;
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
        try { return Ok(await _coupons.CreateAsync(dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Ã˜ÂªÃ˜Â¹Ã˜Â¯Ã™Å Ã™â€ž Ã™Æ’Ã™Ë†Ã˜Â¨Ã™Ë†Ã™â€  (Admin)</summary>
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

    /// <summary>Ã˜ÂªÃ™ÂÃ˜Â¹Ã™Å Ã™â€ž/Ã˜Â¥Ã™Å Ã™â€šÃ˜Â§Ã™Â Ã™Æ’Ã™Ë†Ã˜Â¨Ã™Ë†Ã™â€  (Admin)</summary>
    [HttpPatch("{id}/toggle")]
    public async Task<IActionResult> Toggle(int id) =>
        await _coupons.ToggleAsync(id) ? Ok() : NotFound();

    /// <summary>Ã˜ÂªÃ˜Â¹Ã˜Â·Ã™Å Ã™â€ž Ã™Æ’Ã™Ë†Ã˜Â¨Ã™Ë†Ã™â€  (Admin)</summary>
    [HttpPatch("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(int id) =>
        await _coupons.DeactivateAsync(id) ? Ok() : NotFound();

    /// <summary>Ã˜Â­Ã˜Â°Ã™Â Ã™Æ’Ã™Ë†Ã˜Â¨Ã™Ë†Ã™â€  (Admin)</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id) =>
        await _coupons.DeleteAsync(id) ? Ok() : NotFound();
}

