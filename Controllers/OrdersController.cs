using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Services;
using System.Security.Claims;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly IPdfService _pdfService;
    private readonly AppDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;

    public OrdersController(IOrderService orderService, IPdfService pdfService, AppDbContext db, IServiceScopeFactory scopeFactory)
    {
        _orderService = orderService;
        _pdfService   = pdfService;
        _db           = db;
        _scopeFactory = scopeFactory;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Staff,Cashier")]
    public async Task<ActionResult<PaginatedResult<OrderSummaryDto>>> GetOrders(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 12, 
        [FromQuery] OrderStatus? status = null, [FromQuery] string? search = null,
        [FromQuery] int? customerId = null, [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null, [FromQuery] string? salesPersonId = null,
        [FromQuery] OrderSource? source = null)
    {
        var result = await _orderService.GetOrdersAsync(page, pageSize, status, search, customerId, fromDate, toDate, salesPersonId, source);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OrderDetailDto>> GetOrder(int id)
    {
        var order = await _orderService.GetOrderByIdAsync(id);
        if (order == null) return NotFound();

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
            posDto.CustomerName,
            posDto.Note
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

        if (!IsOwnerOrStaff(order.Customer.Id))
            return Forbid();

        var pdfBytes = await _pdfService.GenerateOrderPdfAsync(order);
        return File(pdfBytes, "application/pdf", $"Order-{order.OrderNumber}.pdf");
    }

    [Authorize(Roles = "Admin,Manager,Staff")]
    [HttpPatch("{id}/payment-status")]
    public async Task<IActionResult> UpdatePaymentStatus(int id, [FromBody] UpdatePaymentStatusDto dto)
    {
        var order = await _db.Orders.Include(o => o.Customer).FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted);
        if (order == null) return NotFound();

        var oldStatus = order.PaymentStatus;
        order.PaymentStatus = dto.PaymentStatus;
        order.UpdatedAt     = DateTime.UtcNow;

        if (dto.PaymentStatus == PaymentStatus.Paid && oldStatus != PaymentStatus.Paid)
        {
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                try
                {
                    var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var fullOrder = await db.Orders.Include(o => o.Customer).FirstAsync(o => o.Id == id);
                    await accounting.PostOrderPaymentAsync(fullOrder);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Accounting] Auto-collection from UpdatePaymentStatus failed: {ex.Message}");
                }
            });
        }

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

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Delete(int id)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted);

        if (order == null) return NotFound();

        using var scope = _scopeFactory.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();

        foreach (var item in order.Items)
        {
            // ProductId هو int (ليس Nullable) لذا لا يحتاج HasValue
            if (item.ProductId > 0)
            {
                await inventory.LogMovementAsync(
                    InventoryMovementType.Adjustment,
                    item.Quantity, // الإضافة للمخزن بدلاً من الخصم
                    item.ProductId,
                    item.ProductVariantId,
                    order.OrderNumber,
                    "Order Deleted (Cascade Cleanup)",
                    User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                );
            }
        }

        // 3. حذف القيد المحاسبي المرتبط بالطلب نهائياً (Sales Invoice)
        var salesEntry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Type == JournalEntryType.SalesInvoice && e.Reference == order.OrderNumber);
        if (salesEntry != null) _db.JournalEntries.Remove(salesEntry);

        // 4. حذف قيد التحصيل (ReceiptVoucher) أو قيد الرد (PaymentVoucher) لو موجود نهائياً
        var paymentEntry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Type == JournalEntryType.ReceiptVoucher && e.Reference == order.OrderNumber);
        if (paymentEntry != null) _db.JournalEntries.Remove(paymentEntry);

        var refundEntry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Type == JournalEntryType.PaymentVoucher && e.Reference == order.OrderNumber + "-RFD");
        if (refundEntry != null) _db.JournalEntries.Remove(refundEntry);

        // 5. حذف الطلب وسجل حالته نهائياً (Hard-Delete)
        _db.Orders.Remove(order);

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
