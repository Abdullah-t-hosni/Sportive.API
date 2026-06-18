using Sportive.API.Attributes;
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
using Hangfire;
using Microsoft.Extensions.Caching.Memory;
using Sportive.API.Extensions;

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
    private readonly IAuditService _audit;
    private readonly ITranslator _translator;
    private readonly IMemoryCache _cache;

    public OrdersController(IOrderService orderService, IPdfService pdfService, AppDbContext db, IServiceScopeFactory scopeFactory, ILogger<OrdersController> logger, IAuditService audit, ITranslator translator, IMemoryCache cache)
    {
        _orderService = orderService;
        _pdfService   = pdfService;
        _db           = db;
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _audit        = audit;
        _translator   = translator;
        _cache        = cache;
    }

    [HttpGet]
    [RequirePermission(ModuleKeys.Orders)]
    [AllowPosAccess]
    public async Task<ActionResult<PaginatedResult<OrderSummaryDto>>> GetOrders(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 12,
        [FromQuery] OrderStatus? status = null, [FromQuery] string? search = null,
        [FromQuery] int? customerId = null, [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null, [FromQuery] string? salesPersonId = null,
        [FromQuery] OrderSource? source = null, [FromQuery] PaymentMethod? paymentMethod = null,
        [FromQuery] string? orderBy = null, [FromQuery] bool descending = false,
        [FromQuery] int? branchId = null, [FromQuery] int? warehouseId = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 2000);

        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? isolatedBranchId = canViewAll ? branchId : User.GetBranchId();

        var result = await _orderService.GetOrdersAsync(page, pageSize, status, search, customerId, fromDate, toDate, salesPersonId, source, paymentMethod, orderBy, descending, isolatedBranchId, warehouseId);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OrderDetailDto>> GetOrder(int id)
    {
        var order = await _orderService.GetOrderByIdAsync(id);
        if (order == null) return NotFound();

        if (!await IsOwnerOrStaffAsync(order.Customer.Id))
            return Forbid();

        return Ok(order);
    }

    [HttpGet("my")]
    public async Task<ActionResult<PaginatedResult<OrderSummaryDto>>> GetMyOrders([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var customerIdStr = User.FindFirst("CustomerId")?.Value;
        if (string.IsNullOrEmpty(customerIdStr)) return BadRequest(_translator.Get("Orders.NoCustomerProfile"));
        
        var result = await _orderService.GetCustomerOrdersAsync(int.Parse(customerIdStr), page, pageSize);
        return Ok(result);
    }
    
    private async Task<bool> IsOwnerOrStaffAsync(int customerId)
    {
        if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Staff") || User.IsInRole("Cashier"))
            return true;

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            var permissionService = HttpContext.RequestServices.GetRequiredService<IPermissionService>();
            if (await permissionService.HasPosAccessAsync(userId))
                return true;
        }

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
    [RequirePermission(ModuleKeys.Orders)]
    [AllowPosAccess]
    public async Task<ActionResult<OrderDetailDto>> CreatePosOrder([FromBody] CreatePOSOrderDto posDto)
    {
        if (posDto == null || posDto.Items == null || !posDto.Items.Any())
            return BadRequest(_translator.Get("Orders.MinOneItem"));

        // ✅ Strong Idempotency: التحقق من وجود الطلب بمفتاح الأوفلاين الفريد لمنع تكرار المزامنة نهائياً
        if (!string.IsNullOrEmpty(posDto.OfflineRef))
        {
            var existingOrder = await _db.Orders
                .FirstOrDefaultAsync(o => o.AdminNotes != null && o.AdminNotes.Contains($"[OfflineRef: {posDto.OfflineRef}]"));
            if (existingOrder != null)
            {
                _logger.LogWarning("[Idempotency] Order with OfflineRef {OfflineRef} already exists. Returning existing order {OrderId}", posDto.OfflineRef, existingOrder.Id);
                var orderDetail = await _orderService.GetOrderByIdAsync(existingOrder.Id);
                return Ok(orderDetail);
            }
        }

        // ✅ Idempotency Guard (Short-term cache): منع تكرار الطلب خلال 10 ثواني من نفس الكاشير بنفس الإجمالي ونفس الأصناف
        var cashierId = posDto.PosEmployeeId ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        var itemsKey  = string.Join("|", posDto.Items.Select(i => $"{i.ProductId}:{i.ProductVariantId}:{i.Quantity}").OrderBy(x => x));
        var idempotencyKey = $"pos_order:{cashierId}:{posDto.TotalAmount}:{posDto.CustomerId}:{itemsKey}";

        if (_cache.TryGetValue(idempotencyKey, out _))
        {
            _logger.LogWarning("[Idempotency] Duplicate POS order blocked for cashier={Cashier} total={Total}", cashierId, posDto.TotalAmount);
            return Conflict(_translator.Get("Orders.DuplicateOrderBlocked") ?? "تم حجب طلب مكرر. الفاتورة السابقة تمت بنجاح.");
        }

        // تسجيل المفتاح لمدة 10 ثواني لمنع التكرار
        _cache.Set(idempotencyKey, true, TimeSpan.FromSeconds(10));

        var finalNote = string.IsNullOrEmpty(posDto.OfflineRef)
            ? posDto.Note
            : $"[OfflineRef: {posDto.OfflineRef}] {posDto.Note}".Trim();

        var dto = new CreateOrderDto(
            FulfillmentType.Pickup,
            (PaymentMethod)posDto.PaymentMethod,
            null,
            null,
            "POS Sale",
            posDto.CouponCode,
            posDto.PosEmployeeId ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            (OrderSource)posDto.OrderSource,
            posDto.Items.Select(i => new CreateOrderItemDto(i.ProductId, i.ProductVariantId, i.Quantity, i.UnitPrice, i.TotalPrice, i.HasTax, i.VatRate, i.Size, i.Color)).ToList(),
            posDto.CustomerPhone,
            posDto.CustomerName,
            finalNote,
            posDto.DiscountAmount,
            posDto.TemporalDiscount,
            posDto.Subtotal,
            posDto.Payments,
            posDto.PaidAmount,
            posDto.AttachmentUrl,
            posDto.AttachmentPublicId,
            User.GetBranchId() ?? posDto.BranchId,
            User.GetWarehouseId() ?? posDto.WarehouseId
        );

        var order = await _orderService.CreateOrderAsync(posDto.CustomerId, dto);
        if (order == null) return StatusCode(500, _translator.Get("Orders.CreationFailed"));
        
        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }

    [HttpPatch("{id}/status")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<OrderDetailDto>> UpdateStatus(int id, [FromBody] UpdateOrderStatusDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        var order = await _orderService.UpdateOrderStatusAsync(id, dto, userId);
        return Ok(order);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<OrderDetailDto>> UpdateOrder(int id, [FromBody] UpdateOrderDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        var order = await _orderService.UpdateOrderAsync(id, dto, userId);
        
        await _audit.LogAsync("UpdateOrder", "Order", id.ToString(), $"Order {order.OrderNumber} updated by admin", userId, User.FindFirstValue(ClaimTypes.Name));

        return Ok(order);
    }

    [HttpPatch("{id}/note")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateAdminNote(int id, [FromBody] UpdateOrderAdminNoteDto dto)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        order.AdminNotes = dto.Note;
        order.UpdatedAt  = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();

        return Ok(new { note = order.AdminNotes });
    }

    [HttpPatch("{id}/date")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateDate(int id, [FromBody] UpdateOrderDateDto dto)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        var oldDate = order.CreatedAt;
        order.CreatedAt = dto.CreatedAt;
        order.UpdatedAt = TimeHelper.GetEgyptTime();

        // ✅ SYNC ACCOUNTING: Update all Journal Entries associated with this order
        var journalEntries = await _db.JournalEntries
            .Where(e => e.OrderId == id || e.Reference == order.OrderNumber)
            .ToListAsync();

        foreach (var entry in journalEntries)
        {
            entry.EntryDate = TimeHelper.GetEgyptBusinessDayDate(dto.CreatedAt);
            entry.CreatedAt = dto.CreatedAt;
        }

        // ✅ SYNC VOUCHERS: Update any Receipt/Payment vouchers linked to this order
        var vouchers = await _db.ReceiptVouchers.Where(v => v.OrderId == id).ToListAsync();
        foreach (var v in vouchers)
        {
            v.VoucherDate = TimeHelper.GetEgyptBusinessDayDate(dto.CreatedAt);
            v.CreatedAt   = dto.CreatedAt;
        }

        await _db.SaveChangesAsync();
        
        await _audit.LogAsync("UpdateOrderDate", "Order", id.ToString(), $"Order {order.OrderNumber} date changed from {oldDate} to {dto.CreatedAt}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name));

        return Ok(new { createdAt = order.CreatedAt });
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetOrderPdf(int id)
    {
        var order = await _orderService.GetOrderByIdAsync(id);
        if (order == null) return NotFound();

        if (!await IsOwnerOrStaffAsync(order.Customer.Id))
            return Forbid();

        var pdfBytes = await _pdfService.GenerateOrderPdfAsync(order);
        return File(pdfBytes, "application/pdf", $"Order-{order.OrderNumber}.pdf");
    }

    [HttpGet("public-invoice/{orderNumber}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicOrderPdf(string orderNumber, [FromQuery] string hash)
    {
        if (string.IsNullOrEmpty(hash))
            return BadRequest(new { message = "Hash is required." });

        var expectedHash = GenerateOrderHash(orderNumber);
        var clientHash = hash.ToLower();
        if (clientHash.Length > 10) clientHash = clientHash.Substring(0, 10);
        if (expectedHash != clientHash)
            return Unauthorized(new { message = "Invalid hash signature." });

        var order = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);

        if (order == null) return NotFound(_translator.Get("Orders.InvoiceNotFound"));

        var dto = await _orderService.GetOrderByIdAsync(order.Id);
        if (dto == null) return NotFound(_translator.Get("Orders.InvoiceDetailsNotFound"));

        var pdfBytes = await _pdfService.GenerateOrderPdfAsync(dto);
        return File(pdfBytes, "application/pdf", $"Invoice-{orderNumber}.pdf");
    }

    [AllowAnonymous]
    [HttpGet("public-data/{orderNumber}")]
    public async Task<IActionResult> GetPublicOrderData(string orderNumber, [FromQuery] string hash)
    {
        if (string.IsNullOrEmpty(hash))
            return BadRequest(new { message = "Hash is required." });

        var expectedHash = GenerateOrderHash(orderNumber);
        var clientHash = hash.ToLower();
        if (clientHash.Length > 10) clientHash = clientHash.Substring(0, 10);
        if (expectedHash != clientHash)
            return Unauthorized(new { message = "Invalid hash signature." });

        var order = await _db.Orders
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);

        if (order == null) return NotFound(_translator.Get("Orders.InvoiceNotFound"));

        var dto = await _orderService.GetOrderByIdAsync(order.Id);
        if (dto == null) return NotFound(_translator.Get("Orders.InvoiceDetailsNotFound"));

        return Ok(dto);
    }

    private static string GenerateOrderHash(string orderNumber)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes("SportiveSecretInvoiceSaltKey2026"));
        var hashBytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"invoice-{orderNumber}"));
        return Convert.ToHexString(hashBytes).ToLower().Substring(0, 10);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPatch("{id}/payment-status")]
    public async Task<IActionResult> UpdatePaymentStatus(int id, [FromBody] UpdatePaymentStatusDto dto)
    {
        var order = await _db.Orders.Include(o => o.Customer).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        var oldStatus = order.PaymentStatus;
        order.PaymentStatus = dto.PaymentStatus;
        order.UpdatedAt     = TimeHelper.GetEgyptTime();

        // âœ… IMPORTANT: Sync the numerical PaidAmount when status moves to Paid
        // âœ… IMPORTANT: Sync the numerical PaidAmount when status moves to Paid
        if (dto.PaymentStatus == PaymentStatus.Paid)
        {
            // Only force total amount if no vouchers/payments exist yet to avoid double counting
            var currentVouchersSum = await _db.JournalLines
                .Where(l => l.OrderId == id && l.Credit > 0 && l.JournalEntry.Type != JournalEntryType.SalesReturn)
                .Where(l => l.Account.Code != null && l.Account.Code.StartsWith("1107"))
                .SumAsync(l => l.Credit);

            if (currentVouchersSum < order.TotalAmount)
            {
                order.PaidAmount = order.TotalAmount;
            }
        }

        if (dto.PaymentStatus == PaymentStatus.Paid && oldStatus != PaymentStatus.Paid)
        {
            BackgroundJob.Enqueue<IAccountingService>(a => a.PostOrderPaymentByIdAsync(id));
        }

        if (!string.IsNullOrEmpty(dto.Note))
        {
            _db.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId          = id,
                Status           = order.Status,
                Note             = _translator.Get("Orders.PaymentStatusUpdateNote", dto.PaymentStatus, dto.Note),
                ChangedByUserId  = dto.PerformedByEmployeeId?.ToString() ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                CreatedAt        = TimeHelper.GetEgyptTime()
            });
        }

        // 💳 SYNC INSTALLMENT STATUS ON PAYMENT STATUS CHANGE
        if (dto.PaymentStatus == PaymentStatus.Paid)
        {
            var linkedInstallments = await _db.CustomerInstallments
                .Where(i => i.OrderId == id && i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Cancelled)
                .ToListAsync();
            foreach (var inst in linkedInstallments)
            {
                inst.PaidAmount = inst.TotalAmount;
                inst.Status = InstallmentStatus.Paid;
                inst.UpdatedAt = TimeHelper.GetEgyptTime();
            }
        }
        else if (oldStatus == PaymentStatus.Paid && dto.PaymentStatus != PaymentStatus.Paid)
        {
            var linkedInstallments = await _db.CustomerInstallments
                .Where(i => i.OrderId == id && i.Status == InstallmentStatus.Paid)
                .ToListAsync();
            foreach (var inst in linkedInstallments)
            {
                inst.PaidAmount = 0;
                inst.Status = inst.DueDate < DateTime.Today ? InstallmentStatus.Overdue : InstallmentStatus.Pending;
                inst.UpdatedAt = TimeHelper.GetEgyptTime();
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { paymentStatus = order.PaymentStatus.ToString() });
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(int id)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) return NotFound();

        // 1. Guard against deleting orders with returns (partial or full)
        if (order.Status == OrderStatus.Returned || order.Status == OrderStatus.PartiallyReturned)
        {
            return BadRequest("لا يمكن حذف فاتورة تحتوي على مرتجع جزئي أو كلي. يرجى إلغاء المرتجع أولاً.");
        }

        using var scope = _scopeFactory.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();

        // Remove original inventory movements instead of creating adjustments (so it looks like it never existed)
        var relatedMovements = await _db.InventoryMovements
            .Where(m => m.Reference == order.OrderNumber)
            .ToListAsync();

        if (relatedMovements.Any())
        {
            _db.InventoryMovements.RemoveRange(relatedMovements);
            
            // Adjust stock back to warehouse manually since we are not using LogMovementAsync
            foreach (var item in order.Items)
            {
                if (item.ProductId > 0)
                {
                    var product = await _db.Products.FindAsync(item.ProductId);
                    if (product != null)
                    {
                        product.TotalStock += item.Quantity;
                        product.UpdatedAt = TimeHelper.GetEgyptTime();
                        
                        if (product.Status == ProductStatus.OutOfStock && product.TotalStock > 0)
                        {
                            product.Status = ProductStatus.Active;
                        }
                    }

                    if (item.ProductVariantId > 0)
                    {
                        var variant = await _db.ProductVariants.FindAsync(item.ProductVariantId);
                        if (variant != null)
                        {
                            variant.StockQuantity += item.Quantity;
                            variant.UpdatedAt = TimeHelper.GetEgyptTime();
                        }
                    }
                }
            }
        }

        // Clean up linked receipt vouchers and their associated journal entries
        var linkedReceiptVouchers = await _db.ReceiptVouchers.Where(v => v.OrderId == id).ToListAsync();
        foreach (var voucher in linkedReceiptVouchers)
        {
            var voucherEntries = await _db.JournalEntries
                .Include(e => e.Lines)
                .Where(e => e.Type == JournalEntryType.ReceiptVoucher && e.Reference == voucher.VoucherNumber)
                .ToListAsync();

            foreach (var voucherEntry in voucherEntries)
            {
                var childReversals = await _db.JournalEntries
                    .Include(j => j.Lines)
                    .Where(j => j.ReversalOfId == voucherEntry.Id)
                    .ToListAsync();
                foreach (var child in childReversals)
                {
                    _db.JournalLines.RemoveRange(child.Lines);
                }
                _db.JournalEntries.RemoveRange(childReversals);

                _db.JournalLines.RemoveRange(voucherEntry.Lines);
                _db.JournalEntries.Remove(voucherEntry);
            }
            _db.ReceiptVouchers.Remove(voucher);
        }

        // Clean up direct journal entries of the order itself (Sales Invoice, direct Payment/Receipt Vouchers, Reversals, etc.)
        var relatedEntries = await _db.JournalEntries
            .Include(e => e.Lines)
            .Where(e => e.OrderId == id || e.Reference == order.OrderNumber || e.Reference == order.OrderNumber + "-RFD")
            .ToListAsync();

        foreach (var entry in relatedEntries)
        {
            if (_db.Entry(entry).State == EntityState.Deleted || _db.Entry(entry).State == EntityState.Detached)
                continue;

            var childReversals = await _db.JournalEntries
                .Include(j => j.Lines)
                .Where(j => j.ReversalOfId == entry.Id)
                .ToListAsync();
            foreach (var child in childReversals)
            {
                if (_db.Entry(child).State != EntityState.Deleted && _db.Entry(child).State != EntityState.Detached)
                {
                    _db.JournalLines.RemoveRange(child.Lines);
                }
            }
            _db.JournalEntries.RemoveRange(childReversals.Where(c => _db.Entry(c).State != EntityState.Deleted && _db.Entry(c).State != EntityState.Detached));

            _db.JournalLines.RemoveRange(entry.Lines);
            _db.JournalEntries.Remove(entry);
        }

        // Restore coupon usage count
        if (!string.IsNullOrEmpty(order.CouponCode))
        {
            var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == order.CouponCode.ToUpper());
            if (coupon != null && coupon.CurrentUsageCount > 0)
            {
                coupon.CurrentUsageCount--;
            }
        }

        // Clean up installments
        var linkedInstallments = await _db.CustomerInstallments
            .Include(i => i.Payments)
            .Where(i => i.OrderId == id)
            .ToListAsync();
        foreach (var inst in linkedInstallments)
        {
            _db.InstallmentPayments.RemoveRange(inst.Payments);
        }
        _db.CustomerInstallments.RemoveRange(linkedInstallments);

        // 3. Nullify OrderId on all remaining entities to prevent foreign key violations (absolute safety guard)
        var remainingJournalEntries = await _db.JournalEntries.Where(e => e.OrderId == id).ToListAsync();
        foreach (var entry in remainingJournalEntries)
        {
            if (_db.Entry(entry).State != EntityState.Deleted && _db.Entry(entry).State != EntityState.Detached)
            {
                entry.OrderId = null;
            }
        }

        var remainingReceiptVouchers = await _db.ReceiptVouchers.Where(v => v.OrderId == id).ToListAsync();
        foreach (var voucher in remainingReceiptVouchers)
        {
            if (_db.Entry(voucher).State != EntityState.Deleted && _db.Entry(voucher).State != EntityState.Detached)
            {
                voucher.OrderId = null;
            }
        }

        var remainingJournalLines = await _db.JournalLines.Where(l => l.OrderId == id).ToListAsync();
        foreach (var line in remainingJournalLines)
        {
            if (_db.Entry(line).State != EntityState.Deleted && _db.Entry(line).State != EntityState.Detached)
            {
                line.OrderId = null;
            }
        }

        var remainingNotifications = await _db.Notifications.Where(n => n.OrderId == id).ToListAsync();
        foreach (var notification in remainingNotifications)
        {
            if (_db.Entry(notification).State != EntityState.Deleted && _db.Entry(notification).State != EntityState.Detached)
            {
                notification.OrderId = null;
            }
        }

        var remainingInstallments = await _db.CustomerInstallments.Where(i => i.OrderId == id).ToListAsync();
        foreach (var inst in remainingInstallments)
        {
            if (_db.Entry(inst).State != EntityState.Deleted && _db.Entry(inst).State != EntityState.Detached)
            {
                inst.OrderId = null;
            }
        }

        // Remove order and save changes
        _db.Orders.Remove(order);
        await _db.SaveChangesAsync();

        // Log audit
        await _audit.LogAsync("DeleteOrder", "Order", id.ToString(), $"Order {order.OrderNumber} deleted with its journal entries", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name));

        // Recalculate and sync customer balances
        try
        {
            Hangfire.BackgroundJob.Enqueue<IAccountingService>(a => a.SyncEntityBalancesAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing entity balances after deleting order {OrderId}", id);
        }

        return NoContent();
    }

    [HttpPost("{id}/partial-return")]
    [RequirePermission(ModuleKeys.Orders)]
    [AllowPosAccess]
    public async Task<ActionResult<OrderDetailDto>> PostPartialReturn(int id, [FromBody] PartialReturnDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        var order = await _orderService.ProcessPartialReturnAsync(id, dto, userId);
        return Ok(order);
    }

    [HttpPost("{id}/convert-to-cost")]
    [RequirePermission(ModuleKeys.PosSellAtCost, requireEdit: true)]
    public async Task<ActionResult<OrderDetailDto>> ConvertToCost(int id, [FromBody] ConvertToCostDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        var order = await _orderService.ConvertToCostAsync(id, dto.RefundMethod, userId);
        return Ok(order);
    }


    [HttpPost("direct-return")]
    [RequirePermission(ModuleKeys.Orders)]
    [AllowPosAccess]
    public async Task<IActionResult> PostDirectReturn([FromBody] DirectReturnDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        var returnNumber = await _orderService.ProcessDirectReturnAsync(dto, userId);
        return Ok(new { message = _translator.Get("Orders.DirectReturnSuccess"), returnNumber });
    }

    [HttpPost("returns/{reference}/update")]
    [RequirePermission(ModuleKeys.Orders)]
    [AllowPosAccess]
    public async Task<IActionResult> UpdateSalesReturn(string reference, [FromBody] UpdateSalesReturnDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        await _orderService.UpdateSalesReturnAsync(reference, dto, userId);
        return Ok(new { message = "Sales return updated successfully." });
    }

    [HttpPatch("{id}/redistribute-payments")]
    [RequirePermission(ModuleKeys.Orders)]
    [AllowPosAccess]
    public async Task<IActionResult> RedistributePayments(int id, [FromBody] RedistributePaymentsDto dto)
    {
        if (dto == null || dto.Payments == null || !dto.Payments.Any())
            return BadRequest(_translator.Get("Orders.MinOneItem"));

        var order = await _db.Orders
            .Include(o => o.Payments)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        // ── 🔴 Fix 1: منع إعادة التوزيع على فواتير مُلغاة أو مُرتجعة ──
        if (order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Returned)
            return BadRequest(_translator.Get("Orders.CannotRedistributeOnStatus") ?? "لا يمكن إعادة توزيع المبالغ على فاتورة ملغاة أو مرتجعة");

        var newTotal = dto.Payments.Sum(p => p.Amount);
        if (Math.Abs(newTotal - order.TotalAmount) > 0.1m)
            return BadRequest(_translator.Get("Orders.TotalAmountMismatch") ?? "إجمالي المبالغ يجب أن يساوي إجمالي الفاتورة");

        using var scope = _scopeFactory.CreateScope();
        var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
        var accountingCore = scope.ServiceProvider.GetRequiredService<AccountingCoreService>();

        // ── تحديث OrderPayments ──
        _db.OrderPayments.RemoveRange(order.Payments);
        foreach (var p in dto.Payments)
        {
            order.Payments.Add(new OrderPayment
            {
                Method    = p.Method,
                Amount    = p.Amount,
                Reference = p.Reference,
                Notes     = p.Notes,
                IsPosted  = true
            });
        }

        // ── تحديث PaidAmount على الـ Order (المبالغ المسددة فعلاً تستبعد الآجل) ──
        order.PaidAmount = dto.Payments
            .Where(p => p.Method != PaymentMethod.Credit)
            .Sum(p => p.Amount);

        // ── تحديث PaymentStatus على الـ Order ──
        if (order.PaidAmount >= order.TotalAmount - 0.1m)
        {
            order.PaymentStatus = PaymentStatus.Paid;
        }
        else if (order.PaidAmount > 0)
        {
            order.PaymentStatus = PaymentStatus.PartiallyPaid;
        }
        else
        {
            order.PaymentStatus = PaymentStatus.Pending;
        }

        // ── تحديث PaymentMethod على الـ Order ──
        if (dto.Payments.Count == 1)
            order.PaymentMethod = dto.Payments.First().Method;
        else if (dto.Payments.Count > 1)
            order.PaymentMethod = PaymentMethod.Mixed;

        // ── ⚡ PERF: جلب الـ mappings مرة واحدة فقط ──
        var mapDict = await accountingCore.GetSafeSystemMappingsAsync();

        // ── جلب IDs الحسابات الحقيقية من الـ mappings (مرة واحدة بدل N queries) ──
        var mappedAccountIds = new HashSet<int>();
        var methodToAccountId = new Dictionary<PaymentMethod, int>();
        foreach (var pm in new[] { PaymentMethod.Cash, PaymentMethod.Bank, PaymentMethod.CreditCard, PaymentMethod.Vodafone, PaymentMethod.InstaPay })
        {
            try
            {
                var code = await accountingCore.GetMappedCashAccountAsync(pm, order.Source, mapDict);
                int? accId = null;
                if (code.StartsWith("ID:", StringComparison.OrdinalIgnoreCase) && int.TryParse(code.Substring(3), out var parsedId))
                    accId = parsedId;
                else
                {
                    var acc = await _db.Accounts.Where(a => a.Code == code).Select(a => (int?)a.Id).FirstOrDefaultAsync();
                    accId = acc;
                }
                if (accId.HasValue)
                {
                    mappedAccountIds.Add(accId.Value);
                    methodToAccountId[pm] = accId.Value;
                }
            }
            catch { /* method not mapped — skip */ }
        }

        // ── جلب حساب العملاء للطلب ──
        int receivablesAccountId;
        if (order.Customer?.MainAccountId != null)
        {
            receivablesAccountId = order.Customer.MainAccountId.Value;
        }
        else
        {
            receivablesAccountId = await accountingCore.GetRequiredMappedAccountAsync(MappingKeys.Customer, mapDict);
        }

        // ── بحث عن قيد المبيعات الخاص بالطلب تحديداً ──
        var journalEntry = await _db.JournalEntries
            .Include(e => e.Lines).ThenInclude(l => l.Account)
            .FirstOrDefaultAsync(e => (e.OrderId == id || e.Reference == order.OrderNumber) && e.Type == JournalEntryType.SalesInvoice);

        if (journalEntry != null)
        {
            // حذف سطور الدفع السابقة وسطور المديونية لتعديلها بشكل متوازن
            var paymentLines = journalEntry.Lines
                .Where(l => l.Debit > 0 && (mappedAccountIds.Contains(l.AccountId) || l.AccountId == receivablesAccountId))
                .ToList();

            if (!paymentLines.Any())
            {
                // Fallback: لو لم نجد السطور باستخدام الـ IDs الحقيقية، نبحث بكود "110" أو "1107" أو "1105"
                paymentLines = journalEntry.Lines
                    .Where(l => l.Debit > 0 && l.Account != null &&
                                (l.Account.Code!.StartsWith("110") || l.Account.Code!.StartsWith("1107") || l.Account.Code!.StartsWith("1105")))
                    .ToList();
            }

            foreach (var line in paymentLines)
                _db.JournalLines.Remove(line);

            // إضافة سطور الدفع الجديدة (بدون DB queries إضافية — كل شئ من الـ cache)
            foreach (var p in dto.Payments)
            {
                if (p.Method == PaymentMethod.Credit)
                    continue; // الآجل يُحسب من المديونية المتبقية تلقائياً بالأسفل

                if (p.Method == PaymentMethod.CustomerBalance)
                {
                    journalEntry.Lines.Add(new JournalLine
                    {
                        AccountId   = receivablesAccountId,
                        Debit       = p.Amount,
                        Credit      = 0,
                        Description = "تسديد باستخدام رصيد العميل المتاح",
                        OrderId     = order.Id,
                        CostCenter  = order.Source,
                        CreatedAt   = TimeHelper.GetEgyptTime(),
                        BranchId    = order.BranchId
                    });
                }
                else
                {
                    // ⚡ استخدام الـ cache بدل DB query جديدة
                    if (!methodToAccountId.TryGetValue(p.Method, out var accountId))
                        return BadRequest($"الحساب المرتبط بطريقة الدفع {p.Method} غير موجود في الإعدادات");

                    journalEntry.Lines.Add(new JournalLine
                    {
                        AccountId   = accountId,
                        Debit       = p.Amount,
                        Credit      = 0,
                        Description = $"تعديل طريقة دفع - {p.Method}",
                        OrderId     = order.Id,
                        CostCenter  = order.Source,
                        CreatedAt   = TimeHelper.GetEgyptTime(),
                        BranchId    = order.BranchId
                    });
                }
            }

            // إضافة سطر المديونية المتبقية (الآجل الفعلي) ليتوازن القيد تماماً
            var remainingDebt = Math.Round(order.TotalAmount - order.PaidAmount, 2);
            if (remainingDebt > 0.01m)
            {
                journalEntry.Lines.Add(new JournalLine
                {
                    AccountId   = receivablesAccountId,
                    Debit       = remainingDebt,
                    Credit      = 0,
                    Description = $"إثبات مديونية الطلب المتبقية - {order.OrderNumber}",
                    OrderId     = order.Id,
                    CostCenter  = order.Source,
                    CreatedAt   = TimeHelper.GetEgyptTime(),
                    BranchId    = order.BranchId
                });
            }
        }
        else
        {
            _logger.LogWarning("[RedistributePayments] No sales invoice journal entry found for OrderId={Id} OrderNumber={Num}. Payments updated in DB only.",
                id, order.OrderNumber);
        }

        await _db.SaveChangesAsync();

        try
        {
            Hangfire.BackgroundJob.Enqueue<IAccountingService>(a => a.SyncEntityBalancesAsync());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RedistributePayments] Failed to enqueue balance sync for order {OrderId} — operation still succeeded.", id);
        }

        await _audit.LogAsync("RedistributePayments", "Order", id.ToString(),
            $"Order {order.OrderNumber} payments redistributed by cashier", 
            User.FindFirstValue(ClaimTypes.NameIdentifier), 
            User.FindFirstValue(ClaimTypes.Name));

        return Ok(new { 
            message = _translator.Get("Orders.PaymentsRedistributed") ?? "تم إعادة توزيع المبالغ بنجاح",
            journalUpdated = journalEntry != null
        });
    }


    // POST /api/orders/{id}/archive 
    // POST /api/orders/{id}/unarchive 
    // POST /api/orders/archive-batch 
    // GET  /api/orders/archived 
    [HttpPost("{id}/archive")]
    [RequirePermission(ModuleKeys.Orders, requireEdit: true)]
    public async Task<IActionResult> Archive(int id)
    {
        var order = await _db.Orders.FindAsync(id);
        if (order == null) return NotFound();
        order.IsArchived = true;
        order.ArchivedAt = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();
        return Ok(new { message = _translator.Get("Orders.Archived") });
    }

    [HttpPost("{id}/unarchive")]
    [RequirePermission(ModuleKeys.Orders, requireEdit: true)]
    public async Task<IActionResult> Unarchive(int id)
    {
        var order = await _db.Orders.FindAsync(id);
        if (order == null) return NotFound();
        order.IsArchived = false;
        order.ArchivedAt = null;
        await _db.SaveChangesAsync();
        return Ok(new { message = _translator.Get("Orders.Unarchived") });
    }

    [HttpPost("archive-batch")]
    [RequirePermission(ModuleKeys.Orders, requireEdit: true)]
    public async Task<IActionResult> ArchiveBatch([FromBody] ArchiveBatchDto dto)
    {
        if (dto.Ids == null || dto.Ids.Length == 0)
            return BadRequest(new { message = _translator.Get("Orders.NoIdsProvided") });

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
    [RequirePermission(ModuleKeys.Orders)]
    [AllowPosAccess]
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
        {
            var searchHash = Customer.EncryptionHelper?.ComputeSearchHash(search);
            q = q.Where(o => o.OrderNumber.Contains(search)
                           || (o.Customer != null && o.Customer.FullName.Contains(search))
                           || (o.Customer != null && searchHash != null && o.Customer.PhoneHash == searchHash));
        }

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(o => o.ArchivedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new {
                o.Id, o.OrderNumber,
                Status = o.Status.ToString(),
                o.TotalAmount, o.PaidAmount, o.CreatedAt, o.ArchivedAt,
                CustomerId = o.CustomerId,
                CustomerName  = o.Customer != null ? o.Customer.FullName : null,
                CustomerPhone = o.Customer != null ? o.Customer.Phone    : null,
            }).ToListAsync();

        return Ok(new { items, totalCount = total, totalPages = (int)Math.Ceiling((double)total / pageSize), page, pageSize });
    }
}

public record ArchiveBatchDto(int[] Ids, bool? Archive = true);

public class RedistributePaymentsDto
{
    public List<PaymentItemDto> Payments { get; set; } = new List<PaymentItemDto>();
}

public class PaymentItemDto
{
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public class ConvertToCostDto
{
    public string RefundMethod { get; set; } = "credit"; // "cash" | "credit"
}

