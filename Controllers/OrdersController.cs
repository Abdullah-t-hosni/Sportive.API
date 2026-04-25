using Sportive.API.Utils;
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
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(IOrderService orderService, IPdfService pdfService, AppDbContext db, IServiceScopeFactory scopeFactory, ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _pdfService   = pdfService;
        _db           = db;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Staff,Cashier")]
    public async Task<ActionResult<PaginatedResult<OrderSummaryDto>>> GetOrders(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 12,
        [FromQuery] OrderStatus? status = null, [FromQuery] string? search = null,
        [FromQuery] int? customerId = null, [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null, [FromQuery] string? salesPersonId = null,
        [FromQuery] OrderSource? source = null, [FromQuery] PaymentMethod? paymentMethod = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var result = await _orderService.GetOrdersAsync(page, pageSize, status, search, customerId, fromDate, toDate, salesPersonId, source, paymentMethod);
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
        if (posDto == null || posDto.Items == null || !posDto.Items.Any())
            return BadRequest("Order must have at least one item.");

        var dto = new CreateOrderDto(
            FulfillmentType.Pickup,
            (PaymentMethod)posDto.PaymentMethod,
            null,
            null,
            "POS Sale",
            posDto.CouponCode,
            posDto.PosEmployeeId ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            (OrderSource)posDto.OrderSource,
            posDto.Items.Select(i => new CreateOrderItemDto(i.ProductId, i.ProductVariantId, i.Quantity, i.UnitPrice, i.TotalPrice)).ToList(),
            posDto.CustomerPhone,
            posDto.CustomerName,
            posDto.Note,
            posDto.DiscountAmount,
            posDto.TemporalDiscount,
            posDto.Subtotal,
            posDto.Payments,
            posDto.PaidAmount,
            posDto.AttachmentUrl,
            posDto.AttachmentPublicId
        );

        var order = await _orderService.CreateOrderAsync(posDto.CustomerId, dto);
        if (order == null) return StatusCode(500, "Failed to create order.");
        
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

    [HttpPatch("{id}/note")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    public async Task<IActionResult> UpdateAdminNote(int id, [FromBody] UpdateOrderAdminNoteDto dto)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        order.AdminNotes = dto.Note;
        order.UpdatedAt  = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();

        return Ok(new { note = order.AdminNotes });
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

    [HttpGet("public-invoice/{orderNumber}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicOrderPdf(string orderNumber)
    {
        var order = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);

        if (order == null) return NotFound("Invoice not found.");

        var dto = await _orderService.GetOrderByIdAsync(order.Id);
        if (dto == null) return NotFound("Invoice details not found.");

        var pdfBytes = await _pdfService.GenerateOrderPdfAsync(dto);
        return File(pdfBytes, "application/pdf", $"Invoice-{orderNumber}.pdf");
    }

    [Authorize(Roles = "Admin,Manager,Staff")]
    [HttpPatch("{id}/payment-status")]
    public async Task<IActionResult> UpdatePaymentStatus(int id, [FromBody] UpdatePaymentStatusDto dto)
    {
        var order = await _db.Orders.Include(o => o.Customer).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        var oldStatus = order.PaymentStatus;
        order.PaymentStatus = dto.PaymentStatus;
        order.UpdatedAt     = TimeHelper.GetEgyptTime();

        // ✅ IMPORTANT: Sync the numerical PaidAmount when status moves to Paid
        // ✅ IMPORTANT: Sync the numerical PaidAmount when status moves to Paid
        if (dto.PaymentStatus == PaymentStatus.Paid)
        {
            // Only force total amount if no vouchers/payments exist yet to avoid double counting
            var currentVouchersSum = await _db.JournalLines
                .Where(l => l.OrderId == id && l.Credit > 0 && l.Account.Code.StartsWith("1103"))
                .SumAsync(l => l.Credit);

            if (currentVouchersSum < order.TotalAmount)
            {
                order.PaidAmount = order.TotalAmount;
            }
        }

        if (dto.PaymentStatus == PaymentStatus.Paid && oldStatus != PaymentStatus.Paid)
        {
            _ = PostOrderPaymentWithRetryAsync(id);
        }

        if (!string.IsNullOrEmpty(dto.Note))
        {
            _db.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId          = id,
                Status           = order.Status,
                Note             = $"[حالة الدفع → {dto.PaymentStatus}] {dto.Note}",
                ChangedByUserId  = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                CreatedAt        = TimeHelper.GetEgyptTime()
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
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) return NotFound();

        using var scope = _scopeFactory.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();

        foreach (var item in order.Items)
        {
            if (item.ProductId > 0)
            {
                await inventory.LogMovementAsync(
                    InventoryMovementType.Adjustment,
                    item.Quantity, 
                    item.ProductId,
                    item.ProductVariantId,
                    order.OrderNumber,
                    "Order Deleted (Cascade Cleanup)",
                    User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                );
            }
        }

        var salesEntry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Type == JournalEntryType.SalesInvoice && e.Reference == order.OrderNumber);
        if (salesEntry != null) _db.JournalEntries.Remove(salesEntry);

        var paymentEntry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Type == JournalEntryType.ReceiptVoucher && e.Reference == order.OrderNumber);
        if (paymentEntry != null) _db.JournalEntries.Remove(paymentEntry);

        var refundEntry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Type == JournalEntryType.PaymentVoucher && e.Reference == order.OrderNumber + "-RFD");
        if (refundEntry != null) _db.JournalEntries.Remove(refundEntry);

        // ✅ RESTORE COUPON USAGE IF DELETED
        if (!string.IsNullOrEmpty(order.CouponCode))
        {
            var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == order.CouponCode.ToUpper());
            if (coupon != null && coupon.CurrentUsageCount > 0)
            {
                coupon.CurrentUsageCount--;
            }
        }

        _db.Orders.Remove(order);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id}/partial-return")]
    [Authorize(Roles = "Admin,Manager,Staff,Cashier")]
    public async Task<ActionResult<OrderDetailDto>> PostPartialReturn(int id, [FromBody] PartialReturnDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        var order = await _orderService.ProcessPartialReturnAsync(id, dto, userId);
        return Ok(order);
    }

    // ══════════════════════════════════════════════════
    // POST /api/orders/{id}/archive — أرشفة أمر واحد
    // POST /api/orders/{id}/unarchive — استرجاع من الأرشيف
    // POST /api/orders/archive-batch — أرشفة مجموعة
    // GET  /api/orders/archived — عرض الأوامر المؤرشفة
    // ══════════════════════════════════════════════════
    [HttpPost("{id}/archive")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Archive(int id)
    {
        var order = await _db.Orders.FindAsync(id);
        if (order == null) return NotFound();
        order.IsArchived = true;
        order.ArchivedAt = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();
        return Ok(new { message = "Archived" });
    }

    [HttpPost("{id}/unarchive")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Unarchive(int id)
    {
        var order = await _db.Orders.FindAsync(id);
        if (order == null) return NotFound();
        order.IsArchived = false;
        order.ArchivedAt = null;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Unarchived" });
    }

    [HttpPost("archive-batch")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> ArchiveBatch([FromBody] ArchiveBatchDto dto)
    {
        if (dto.Ids == null || dto.Ids.Length == 0)
            return BadRequest(new { message = "No order IDs provided" });

        var shouldArchive = dto.Archive ?? true;
        var orders = await _db.Orders
            .Where(o => dto.Ids.Contains(o.Id) && o.IsArchived != shouldArchive)
            .ToListAsync();

        var now = TimeHelper.GetEgyptTime();
        foreach (var o in orders)
        {
            o.IsArchived = shouldArchive;
            o.ArchivedAt = shouldArchive ? now : null;
        }
        await _db.SaveChangesAsync();
        return Ok(new { processed = orders.Count, archived = shouldArchive });
    }

    [HttpGet("archived")]
    [Authorize(Roles = "Admin,Manager,Accountant")]
    public async Task<IActionResult> GetArchived(
        [FromQuery] string?   search   = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] int page           = 1,
        [FromQuery] int pageSize       = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var from = fromDate?.Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1);

        var q = _db.Orders.AsNoTracking().Where(o => o.IsArchived).Include(o => o.Customer).AsQueryable();
        if (from.HasValue) q = q.Where(o => o.ArchivedAt >= from);
        if (to.HasValue)   q = q.Where(o => o.ArchivedAt <= to);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(o => o.OrderNumber.Contains(search)
                           || (o.Customer != null && o.Customer.FullName.Contains(search))
                           || (o.Customer != null && o.Customer.Phone != null && o.Customer.Phone.Contains(search)));

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(o => o.ArchivedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new {
                o.Id, o.OrderNumber,
                Status = o.Status.ToString(),
                o.TotalAmount, o.CreatedAt, o.ArchivedAt,
                CustomerId = o.CustomerId,
                CustomerName  = o.Customer != null ? o.Customer.FullName : null,
                CustomerPhone = o.Customer != null ? o.Customer.Phone    : null,
            }).ToListAsync();

        return Ok(new { items, totalCount = total, totalPages = (int)Math.Ceiling((double)total / pageSize), page, pageSize });
    }

    private async Task PostOrderPaymentWithRetryAsync(int orderId)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var fullOrder = await db.Orders.Include(o => o.Customer).FirstAsync(o => o.Id == orderId);
                await accounting.PostOrderPaymentAsync(fullOrder);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex,
                    "[Accounting] Order payment journal attempt {Attempt}/{Max} failed for order {OrderId}. Retrying...",
                    attempt, maxAttempts, orderId);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Accounting] Order payment journal permanently failed for order {OrderId} after {Max} attempts.",
                    orderId, maxAttempts);
            }
        }
    }
}

public record ArchiveBatchDto(int[] Ids, bool? Archive = true);
