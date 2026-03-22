using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CouponsController : ControllerBase
{
    private readonly ICouponService _coupons;
    public CouponsController(ICouponService coupons) => _coupons = coupons;

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] ApplyCouponRequest req)
    {
        var (valid, discount, error) = await _coupons.ValidateAsync(req.Code, req.OrderTotal);
        if (!valid) return BadRequest(new { message = error });
        return Ok(new { discount, message = $"تم تطبيق خصم {discount:N2} ج.م" });
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _coupons.GetAllAsync());

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCouponDto dto)
    {
        try { return Ok(await _coupons.CreateAsync(dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Deactivate(int id) =>
        await _coupons.DeactivateAsync(id) ? Ok() : NotFound();
}
