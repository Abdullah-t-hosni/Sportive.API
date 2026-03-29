using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using System.Security.Claims;
using System;
using System.Threading.Tasks;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly IPdfService _pdfService;
    private readonly AppDbContext _db;

    public OrdersController(IOrderService orderService, IPdfService pdfService, AppDbContext db)
    {
        _orderService = orderService;
        _pdfService = pdfService;
        _db = db;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Staff,Cashier")]
    public async Task<ActionResult<PaginatedResult<OrderSummaryDto>>> GetOrders(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 12, 
        [FromQuery] OrderStatus? status = null, [FromQuery] string? search = null,
        [FromQuery] int? customerId = null, [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null, [FromQuery] string? salesPersonId = null,
        [FromQuery] int? source = null)
    {
        var result = await _orderService.GetOrdersAsync(page, pageSize, status, search, customerId, fromDate, toDate, salesPersonId, source);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OrderDetailDto>> GetOrder(int id)
    {
        var order = await _orderService.GetOrderByIdAsync(id);
        if (order == null) return NotFound();

        // 🛡️ Security Check: Owner or Admin/Staff?
        if (!IsOwnerOrStaff(order.Customer.Id))
            return Forbid();

        return Ok(order);
    }

    [HttpGet("my")]
    public async Task<ActionResult<PaginatedResult<OrderSummaryDto>>> GetMyOrders([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var customerIdStr = User.FindFirst("CustomerId")?.Value;
        if (string.IsNullOrEmpty(customerIdStr)) return BadRequest("User has no customer profile");
        
        var result = await _orderService.GetCustomerOrdersAsync(int.Parse(customerIdStr), page, pageSize);
        return Ok(result);
    }
    
    // Helper to check ownership
    private bool IsOwnerOrStaff(int customerId)
    {
        if (User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Staff") || User.IsInRole("Cashier"))
            return true;

        var currentCustomerId = User.FindFirst("CustomerId")?.Value;
        return currentCustomerId != null && int.Parse(currentCustomerId) == customerId;
    }

    [HttpPost]
    public async Task<ActionResult<OrderDetailDto>> CreateOrder([FromBody] CreateOrderDto dto, [FromQuery] int? customerId = null)
    {
        var claimCustomerIdStr = User.FindFirst("CustomerId")?.Value;
        int? finalCustomerId = null;

        if (!string.IsNullOrEmpty(claimCustomerIdStr) && int.TryParse(claimCustomerIdStr, out var parsedId))
        {
            finalCustomerId = parsedId;
        }
        else
        {
            // If no customer claim (e.g. Staff/Admin), use the provided customerId from query
            finalCustomerId = customerId;
        }

        var order = await _orderService.CreateOrderAsync(finalCustomerId, dto);
        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }

    [HttpPost("pos")]
    [Authorize(Roles = "Admin,Staff,Cashier")]
    public async Task<ActionResult<OrderDetailDto>> CreatePosOrder([FromBody] CreatePOSOrderDto posDto)
    {
        var dto = new CreateOrderDto(
            FulfillmentType.Pickup,
            (PaymentMethod)posDto.PaymentMethod,
            null,
            null,
            "POS Sale",
            null,
            posDto.PosEmployeeId ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            (OrderSource)posDto.OrderSource,
            posDto.Items.Select(i => new CreateOrderItemDto(i.ProductId, i.ProductVariantId, i.Quantity)).ToList(),
            posDto.CustomerPhone,
            posDto.CustomerName
        );

        var order = await _orderService.CreateOrderAsync(posDto.CustomerId, dto);
        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }

    [HttpPatch("{id}/status")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    public async Task<ActionResult<OrderDetailDto>> UpdateStatus(int id, [FromBody] UpdateOrderStatusDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        var order = await _orderService.UpdateOrderStatusAsync(id, dto, userId);
        return Ok(order);
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetOrderPdf(int id)
    {
        var order = await _orderService.GetOrderByIdAsync(id);
        if (order == null) return NotFound();

        // 🛡️ Security Check: Owner or Admin/Staff?
        if (!IsOwnerOrStaff(order.Customer.Id))
            return Forbid();

        var pdfBytes = await _pdfService.GenerateOrderPdfAsync(order);
        return File(pdfBytes, "application/pdf", $"Order-{order.OrderNumber}.pdf");
    }

    [Authorize(Roles = "Admin,Manager,Staff")]
    [HttpPatch("{id}/payment-status")]
    public async Task<IActionResult> UpdatePaymentStatus(
        int id,
        [FromBody] UpdatePaymentStatusDto dto)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted);
        if (order == null) return NotFound();

        order.PaymentStatus = dto.PaymentStatus;
        order.UpdatedAt     = DateTime.UtcNow;

        // لو الحالة دي "مدفوع" وكان آجل → يولّد قيد تحصيل
        if (dto.PaymentStatus == PaymentStatus.Paid &&
            order.PaymentStatus != PaymentStatus.Paid)
        {
            // optional: trigger accounting cash entry
        }

        // سجّل في StatusHistory لو عندك Note
        if (!string.IsNullOrEmpty(dto.Note))
        {
            _db.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId          = id,
                Status           = order.Status,
                Note             = $"[حالة الدفع → {dto.PaymentStatus}] {dto.Note}",
                ChangedByUserId  = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                CreatedAt        = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { paymentStatus = order.PaymentStatus.ToString() });
    }
}
