using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.Settings)]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
public class WaMeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWaMeService _wa;

    public WaMeController(AppDbContext db, IWaMeService wa)
    {
        _db = db;
        _wa = wa;
    }

    // GET /api/wame/order/{id}
    [HttpGet("order/{orderId}")]
    public async Task<IActionResult> GetOrderLinks(int orderId, [FromQuery] string? tracking = null)
    {
        var order = await LoadOrder(orderId);
        if (order == null) return NotFound();

        return Ok(new
        {
            phone        = order.Customer?.Phone,
            customerName = order.Customer?.FullName,
            orderNumber  = order.OrderNumber,
            links = new
            {
                confirmation = _wa.OrderConfirmation(order),
                shipping     = _wa.ShippingUpdate(order, tracking),
                @return      = _wa.ReturnConfirmation(order),
                ready        = _wa.OrderReady(order),
                reminder     = _wa.PaymentReminder(order),
            }
        });
    }

    // GET /api/wame/order/{id}/confirmation
    [HttpGet("order/{orderId}/confirmation")]
    public async Task<IActionResult> Confirmation(int orderId)
        => await SingleLink(orderId, o => _wa.OrderConfirmation(o));

    // GET /api/wame/order/{id}/shipping?tracking=TRK123
    [HttpGet("order/{orderId}/shipping")]
    public async Task<IActionResult> Shipping(int orderId, [FromQuery] string? tracking = null)
        => await SingleLink(orderId, o => _wa.ShippingUpdate(o, tracking));

    // GET /api/wame/order/{id}/return
    [HttpGet("order/{orderId}/return")]
    public async Task<IActionResult> Return(int orderId)
        => await SingleLink(orderId, o => _wa.ReturnConfirmation(o));

    // GET /api/wame/order/{id}/ready
    [HttpGet("order/{orderId}/ready")]
    public async Task<IActionResult> Ready(int orderId)
        => await SingleLink(orderId, o => _wa.OrderReady(o));

    // GET /api/wame/order/{id}/reminder
    [HttpGet("order/{orderId}/reminder")]
    public async Task<IActionResult> Reminder(int orderId)
        => await SingleLink(orderId, o => _wa.PaymentReminder(o));

    // POST /api/wame/custom
    [HttpPost("custom")]
    public IActionResult Custom([FromBody] CustomWaDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Phone) || string.IsNullOrWhiteSpace(dto.Message))
            return BadRequest(new { message = "Ø§Ù„ØªÙ„ÙŠÙÙˆÙ† ÙˆØ§Ù„Ø±Ø³Ø§Ù„Ø© Ù…Ø·Ù„ÙˆØ¨Ø§Ù†" });

        var result = _wa.CustomMessage(dto.Phone, dto.Message);
        return Ok(result);
    }

    private async Task<IActionResult> SingleLink(int orderId, Func<Order, WaMeResult> fn)
    {
        var order = await LoadOrder(orderId);
        if (order == null) return NotFound();
        return Ok(fn(order));
    }

    private async Task<Order?> LoadOrder(int id) =>
        await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);
}

public record CustomWaDto(string Phone, string Message);

