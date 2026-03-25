using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using System.Security.Claims;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly IPdfService _pdfService;

    public OrdersController(IOrderService orderService, IPdfService pdfService)
    {
        _orderService = orderService;
        _pdfService = pdfService;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Staff,Cashier")]
    public async Task<ActionResult<PaginatedResult<OrderSummaryDto>>> GetOrders(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 12, 
        [FromQuery] OrderStatus? status = null, [FromQuery] string? search = null,
        [FromQuery] int? customerId = null, [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null, [FromQuery] string? salesPersonId = null)
    {
        var result = await _orderService.GetOrdersAsync(page, pageSize, status, search, customerId, fromDate, toDate, salesPersonId);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OrderDetailDto>> GetOrder(int id)
    {
        var order = await _orderService.GetOrderByIdAsync(id);
        if (order == null) return NotFound();
        return Ok(order);
    }

    [HttpGet("my")]
    public async Task<ActionResult<PaginatedResult<OrderSummaryDto>>> GetMyOrders([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var customerIdStr = User.FindFirstValue("CustomerId");
        if (string.IsNullOrEmpty(customerIdStr)) return BadRequest("User has no customer profile");
        
        var result = await _orderService.GetCustomerOrdersAsync(int.Parse(customerIdStr), page, pageSize);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<OrderDetailDto>> CreateOrder([FromBody] CreateOrderDto dto)
    {
        var customerIdStr = User.FindFirstValue("CustomerId");
        int? customerId = string.IsNullOrEmpty(customerIdStr) ? null : int.Parse(customerIdStr);

        var order = await _orderService.CreateOrderAsync(customerId, dto);
        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }

    [HttpPatch("{id}/status")]
    [Authorize(Roles = "Admin,Staff,Cashier")]
    public async Task<ActionResult<OrderDetailDto>> UpdateStatus(int id, [FromBody] UpdateOrderStatusDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var order = await _orderService.UpdateOrderStatusAsync(id, dto, userId);
        return Ok(order);
    }

    [HttpGet("{id}/pdf")]
    [AllowAnonymous] // Allow customers to download without force login if they have link
    public async Task<IActionResult> GetOrderPdf(int id)
    {
        var order = await _orderService.GetOrderByIdAsync(id);
        if (order == null) return NotFound();

        var pdfBytes = await _pdfService.GenerateOrderPdfAsync(order);
        return File(pdfBytes, "application/pdf", $"Order-{order.OrderNumber}.pdf");
    }
}
