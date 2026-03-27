using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager")]
public class CouponsController : ControllerBase
{
    private readonly ICouponService _coupons;
    public CouponsController(ICouponService coupons) => _coupons = coupons;

    /// <summary>التحقق من كوبون خصم (public)</summary>
    [HttpPost("validate")]
    [AllowAnonymous]
    public async Task<IActionResult> Validate([FromBody] ApplyCouponRequest req)
    {
        var (valid, discount, error) = await _coupons.ValidateAsync(req.Code, req.OrderTotal);
        if (!valid) return BadRequest(new { message = error });
        return Ok(new { discount, message = $"تم تطبيق خصم {discount:N2} ج.م" });
    }

    /// <summary>كل الكوبونات (Admin)</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _coupons.GetAllAsync());

    /// <summary>إضافة كوبون جديد (Admin)</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCouponDto dto)
    {
        try { return Ok(await _coupons.CreateAsync(dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>تعديل كوبون (Admin)</summary>
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

    /// <summary>تفعيل/إيقاف كوبون (Admin)</summary>
    [HttpPatch("{id}/toggle")]
    public async Task<IActionResult> Toggle(int id) =>
        await _coupons.ToggleAsync(id) ? Ok() : NotFound();

    /// <summary>تعطيل كوبون (Admin)</summary>
    [HttpPatch("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(int id) =>
        await _coupons.DeactivateAsync(id) ? Ok() : NotFound();

    /// <summary>حذف كوبون (Admin)</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id) =>
        await _coupons.DeleteAsync(id) ? Ok() : NotFound();
}
