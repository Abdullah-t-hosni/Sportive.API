using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Hubs;
using Sportive.API.Models;
using Sportive.API.Utils;

using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class ReturnExchangeRequestsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<ReturnExchangeRequestsController> _logger;
    private readonly IAccountingService _accounting;

    public ReturnExchangeRequestsController(
        AppDbContext db,
        IHubContext<NotificationHub> hubContext,
        ILogger<ReturnExchangeRequestsController> logger,
        IAccountingService accounting)
    {
        _db = db;
        _hubContext = hubContext;
        _logger = logger;
        _accounting = accounting;
    }

    /// <summary>
    /// تقديم طلب استبدال أو استرجاع ذاتي بواسطة العميل
    /// </summary>
    /// <summary>
    /// تقديم طلب استبدال أو استرجاع أو حذف صنف بواسطة العميل (مسجل)
    /// </summary>
    [HttpPost("{orderId}/return-exchange-request")]
    public async Task<IActionResult> SubmitRequest(string orderId, [FromBody] CreateReturnExchangeRequestDto dto)
    {
        int.TryParse(orderId, out var idInt);

        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == idInt || o.OrderNumber == orderId);

        if (order == null) return NotFound("الطلب غير موجود.");

        var customer = await GetCurrentCustomerAsync() ?? await _db.Customers.FirstOrDefaultAsync(c => c.Id == order.CustomerId);
        int customerId = customer?.Id ?? order.CustomerId;

        return await ProcessItemDeletionOrRequestAsync(order, customerId, dto);
    }

    /// <summary>
    /// تقديم طلب استبدال أو حذف صنف عبر رابط الفاتورة العامة (زائر)
    /// </summary>
    [HttpPost("public-return-exchange-request")]
    [AllowAnonymous]
    public async Task<IActionResult> PublicSubmitRequest([FromQuery] string orderNumber, [FromBody] CreateReturnExchangeRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(orderNumber)) return BadRequest("رقم الطلب مطلوب.");

        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber || o.Id.ToString() == orderNumber);

        if (order == null) return NotFound("الطلب غير موجود.");

        return await ProcessItemDeletionOrRequestAsync(order, order.CustomerId, dto);
    }

    /// <summary>
    /// الاستعلام العام عن طلبات التعديل/الاستبدال النشطة بالفاتورة
    /// </summary>
    [HttpGet("public-status/{orderNumber}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicOrderStatus(string orderNumber)
    {
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderNumber == orderNumber || o.Id.ToString() == orderNumber);
        if (order == null) return NotFound();

        var requests = await _db.ReturnExchangeRequests
            .AsNoTracking()
            .Include(r => r.Items)
                .ThenInclude(i => i.OrderItem)
            .Where(r => r.OrderId == order.Id)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var response = requests.Select(MapToResponseDto).ToList();
        return Ok(response);
    }

    private async Task<IActionResult> ProcessItemDeletionOrRequestAsync(Order order, int customerId, CreateReturnExchangeRequestDto dto)
    {
        bool isDelete = string.Equals(dto.Type, "Delete", StringComparison.OrdinalIgnoreCase);
        bool isExchange = string.Equals(dto.Type, "Exchange", StringComparison.OrdinalIgnoreCase);

        // 1. Validation for Delete / Exchange
        if (isDelete || isExchange)
        {
            var disallowedStatuses = new[] { OrderStatus.OutForDelivery, OrderStatus.Delivered, OrderStatus.Cancelled, OrderStatus.Returned };
            if (disallowedStatuses.Contains(order.Status))
            {
                string actionName = isDelete ? "حذف أصناف" : "تقديم طلب استبدال";
                return BadRequest($"لا يمكن {actionName} بعد شحن الطلب أو تسليمه أو إلغائه.");
            }
        }
        else
        {
            // Return Rule (post delivery <= 14 days)
            if (order.Status != OrderStatus.Delivered)
            {
                return BadRequest("طلب الاسترجاع متاح فقط بعد استلام وتوصيل الفاتورة.");
            }
            var diffDays = (TimeHelper.GetEgyptTime() - order.CreatedAt).TotalDays;
            if (diffDays > 14)
            {
                return BadRequest("تجاوزت الفترة المسموحة لطلب الاسترجاع (14 يوماً من تاريخ الطلب).");
            }
        }

        if (dto.Items == null || !dto.Items.Any())
        {
            return BadRequest("يرجى اختيار صنف واحد على الأقل.");
        }

        // 2. DIRECT DELETION LOGIC (بدون موافقة آدمن - فورياً لطلب العميل)
        if (isDelete)
        {
            var deletedItemsNotes = new List<string>();

            foreach (var itemDto in dto.Items)
            {
                var orderItem = order.Items.FirstOrDefault(i => i.Id == itemDto.OrderItemId || i.ProductId == itemDto.OrderItemId);
                if (orderItem != null)
                {
                    int qtyToRemove = itemDto.Quantity > 0 ? Math.Min(itemDto.Quantity, orderItem.Quantity) : orderItem.Quantity;

                    // Restock inventory directly
                    if (orderItem.ProductVariantId.HasValue)
                    {
                        var variant = await _db.ProductVariants.FindAsync(orderItem.ProductVariantId.Value);
                        if (variant != null)
                        {
                            variant.StockQuantity += qtyToRemove;
                            variant.UpdatedAt = TimeHelper.GetEgyptTime();
                        }
                    }
                    else
                    {
                        var product = await _db.Products.FindAsync(orderItem.ProductId);
                        if (product != null)
                        {
                            product.TotalStock += qtyToRemove;
                            product.UpdatedAt = TimeHelper.GetEgyptTime();
                        }
                    }

                    orderItem.Quantity -= qtyToRemove;
                    string itemTitle = !string.IsNullOrWhiteSpace(orderItem.ProductNameAr) ? orderItem.ProductNameAr : orderItem.ProductNameEn;
                    deletedItemsNotes.Add($"{itemTitle} (كمية: {qtyToRemove})");

                    if (orderItem.Quantity <= 0)
                    {
                        _db.OrderItems.Remove(orderItem);
                    }
                }
            }

            // Recalculate financial totals
            var remainingItems = order.Items.Where(i => _db.Entry(i).State != EntityState.Deleted && i.Quantity > 0).ToList();
            decimal subTotal = remainingItems.Sum(i => i.UnitPrice * i.Quantity);
            order.SubTotal = subTotal;
            order.TotalAmount = Math.Max(0, subTotal + order.DeliveryFee - order.DiscountAmount + order.TotalVatAmount);
            order.UpdatedAt = TimeHelper.GetEgyptTime();

            if (!remainingItems.Any())
            {
                order.Status = OrderStatus.Cancelled;
                order.AdminNotes = (order.AdminNotes ?? "") + $" | [إلغاء الطلب بحذف جميع الأصناف بواسطة العميل بتاريخ {TimeHelper.GetEgyptTime():yyyy-MM-dd HH:mm}]";
            }
            else
            {
                order.AdminNotes = (order.AdminNotes ?? "") + $" | [حذف أصناف بواسطة العميل: {string.Join(", ", deletedItemsNotes)} بتاريخ {TimeHelper.GetEgyptTime():yyyy-MM-dd HH:mm}]";
            }

            await _db.SaveChangesAsync();

            // 🔄 SMART UPDATE ACCOUNTING JOURNAL ENTRY IN-PLACE (يحافظ على رقم القيد و ID القيد الحالي)
            try
            {
                if (remainingItems.Any())
                {
                    await _accounting.PostSalesOrderAsync(order);
                }
                else
                {
                    // إذا أصبحت الفاتورة ملغية (0 أصناف)، يتم عكس القيد بدلاً من حذفه
                    var existingEntry = await _db.JournalEntries
                        .FirstOrDefaultAsync(e => (e.Type == JournalEntryType.SalesInvoice || e.Type == JournalEntryType.Sales) && e.Reference == order.OrderNumber);
                    if (existingEntry != null && existingEntry.Status != JournalEntryStatus.Reversed)
                    {
                        await _accounting.ReverseEntryAsync(existingEntry.Id, $"إلغاء الفاتورة رقم {order.OrderNumber} بحذف جميع الأصناف");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update accounting journal entry for order deletion {OrderNo}", order.OrderNumber);
            }

            try
            {
                await _hubContext.Clients.All.SendAsync("DashboardUpdate", new { type = "OrderUpdated", id = order.Id });
            }
            catch { }

            return Ok(new { 
                message = remainingItems.Any() ? "تم حذف الصنف من الفاتورة وتحديث الإجمالي والمخزن فوراً 🌟" : "تم حذف جميع الأصناف وإلغاء الفاتورة بنجاح.", 
                isDeletedDirectly = true,
                newTotal = order.TotalAmount,
                orderStatus = order.Status.ToString()
            });
        }

        // 3. EXCHANGE / RETURN REQUEST (يحتاج مراجعة الإدارة)
        var reqType = isExchange ? ReturnExchangeType.Exchange : ReturnExchangeType.Return;
        var request = new ReturnExchangeRequest
        {
            OrderId = order.Id,
            CustomerId = customerId,
            Type = reqType,
            Status = ReturnExchangeStatus.Pending,
            Reason = dto.Reason ?? "طلب تعديل / استبدال صنف",
            CustomerNotes = dto.CustomerNotes,
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        foreach (var itemDto in dto.Items)
        {
            var orderItem = order.Items.FirstOrDefault(i => 
                i.Id == itemDto.OrderItemId || 
                (i.ProductId.HasValue && i.ProductId.Value == itemDto.OrderItemId) ||
                (i.ProductVariantId.HasValue && i.ProductVariantId.Value == itemDto.OrderItemId)
            );

            if (orderItem != null)
            {
                int maxAvailable = Math.Max(1, orderItem.Quantity - orderItem.ReturnedQuantity);
                int validQty = itemDto.Quantity > 0 ? Math.Min(itemDto.Quantity, maxAvailable) : 1;

                request.Items.Add(new ReturnExchangeRequestItem
                {
                    OrderItemId = orderItem.Id,
                    Quantity = validQty,
                    ReplacementNote = !string.IsNullOrWhiteSpace(itemDto.ReplacementNote) ? itemDto.ReplacementNote : dto.CustomerNotes,
                    CreatedAt = TimeHelper.GetEgyptTime()
                });
            }
        }

        // Fallback: If no items matched specific IDs, add first order item so request is never empty
        if (!request.Items.Any() && order.Items.Any())
        {
            var firstItem = order.Items.First();
            request.Items.Add(new ReturnExchangeRequestItem
            {
                OrderItemId = firstItem.Id,
                Quantity = 1,
                ReplacementNote = dto.CustomerNotes,
                CreatedAt = TimeHelper.GetEgyptTime()
            });
        }

        _db.ReturnExchangeRequests.Add(request);
        await _db.SaveChangesAsync();

        try
        {
            await _hubContext.Clients.All.SendAsync("DashboardUpdate", new { type = "ReturnExchangeRequest", id = request.Id });
            await _hubContext.Clients.All.SendAsync("DashboardUpdated", new { type = "ReturnExchangeRequest", id = request.Id });
        }
        catch { }

        return Ok(new { 
            message = "تم تقديم طلب الاستبدال بنجاح وسيتم مراجعته والتواصل معكم من الإدارة. ⏳", 
            requestId = request.Id,
            isDeletedDirectly = false 
        });
    }

    /// <summary>
    /// استعراض الطلبات الخاصة بالعميل الحالي
    /// </summary>
    [HttpGet("my-return-exchange-requests")]
    public async Task<IActionResult> GetMyRequests()
    {
        var customer = await GetCurrentCustomerAsync();
        int customerId = customer?.Id ?? 0;

        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        if (int.TryParse(customerIdClaim, out var cClaimId) && cClaimId > 0)
        {
            if (customerId == 0) customerId = cClaimId;
        }

        var requests = await _db.ReturnExchangeRequests
            .AsNoTracking()
            .Include(r => r.Order)
            .Include(r => r.Items)
                .ThenInclude(i => i.OrderItem)
            .Where(r => r.CustomerId == customerId || (r.Order != null && r.Order.CustomerId == customerId))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var response = requests.Select(MapToResponseDto).ToList();
        return Ok(response);
    }

    private async Task<Customer?> GetCurrentCustomerAsync()
    {
        // 1. Direct CustomerId Claim from JWT Token
        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        if (!string.IsNullOrEmpty(customerIdClaim) && int.TryParse(customerIdClaim, out var cId))
        {
            var cust = await _db.Customers.FirstOrDefaultAsync(c => c.Id == cId);
            if (cust != null) return cust;
        }

        // 2. Fallback lookup by NameIdentifier / Email Claim
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userEmail = User.FindFirstValue(ClaimTypes.Email);

        if (!string.IsNullOrEmpty(userEmail))
        {
            var custByEmail = await _db.Customers.FirstOrDefaultAsync(c => c.Email == userEmail);
            if (custByEmail != null) return custByEmail;
        }

        if (!string.IsNullOrEmpty(userIdStr))
        {
            var custByUserId = await _db.Customers.FirstOrDefaultAsync(c => c.Email == userIdStr);
            if (custByUserId != null) return custByUserId;
        }

        return null;
    }

    /// <summary>
    /// استعراض جميع الطلبات للإدارة
    /// </summary>
    [HttpGet("admin-return-exchange-requests")]
    public async Task<IActionResult> GetAdminRequests([FromQuery] ReturnExchangeRequestListFilterDto filter)
    {
        var query = _db.ReturnExchangeRequests
            .AsNoTracking()
            .Include(r => r.Order)
                .ThenInclude(o => o.Customer)
            .Include(r => r.Customer)
            .Include(r => r.Items)
                .ThenInclude(i => i.OrderItem)
            .AsQueryable();

        if (!string.IsNullOrEmpty(filter.Type) && filter.Type != "all")
        {
            if (Enum.TryParse<ReturnExchangeType>(filter.Type, true, out var parsedType))
            {
                query = query.Where(r => r.Type == parsedType);
            }
        }

        if (!string.IsNullOrEmpty(filter.Status) && filter.Status != "all")
        {
            if (Enum.TryParse<ReturnExchangeStatus>(filter.Status, true, out var parsedStatus))
            {
                query = query.Where(r => r.Status == parsedStatus);
            }
        }

        if (!string.IsNullOrEmpty(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            query = query.Where(r =>
                (r.Order != null && r.Order.OrderNumber.ToLower().Contains(s)) ||
                (r.Customer != null && r.Customer.FullName != null && r.Customer.FullName.ToLower().Contains(s)) ||
                (r.Customer != null && r.Customer.Phone != null && r.Customer.Phone.Contains(s)) ||
                (r.Reason != null && r.Reason.ToLower().Contains(s)) ||
                (r.CustomerNotes != null && r.CustomerNotes.ToLower().Contains(s)) ||
                r.OrderId.ToString() == s ||
                r.Id.ToString() == s);
        }

        var allList = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();

        // Calculate summary stats
        var summary = new ReturnExchangeRequestSummaryDto
        {
            Total = allList.Count,
            Pending = allList.Count(r => r.Status == ReturnExchangeStatus.Pending),
            Exchanges = allList.Count(r => r.Type == ReturnExchangeType.Exchange),
            Returns = allList.Count(r => r.Type == ReturnExchangeType.Return)
        };

        var responseItems = allList.Select(MapToResponseDto).ToList();

        return Ok(new ReturnExchangeRequestsPagedResultDto
        {
            Items = responseItems,
            Summary = summary
        });
    }

    /// <summary>
    /// موافقة الإدارة على طلب الاستبدال
    /// </summary>
    [HttpPost("return-exchange-requests/{requestId}/approve-exchange")]
    public async Task<IActionResult> ApproveExchange(int requestId)
    {
        var req = await _db.ReturnExchangeRequests
            .Include(r => r.Order)
                .ThenInclude(o => o.Items)
            .Include(r => r.Items)
                .ThenInclude(i => i.OrderItem)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (req == null) return NotFound("الطلب غير موجود.");
        if (req.Type != ReturnExchangeType.Exchange) return BadRequest("هذا الطلب ليس طلب استبدال.");
        if (req.Status != ReturnExchangeStatus.Pending) return BadRequest("الطلب تم البت فيه مسبقاً.");

        req.Status = ReturnExchangeStatus.Completed;
        req.AdminNotes = $"[تمت الموافقة على الاستبدال بتاريخ {TimeHelper.GetEgyptTime():yyyy-MM-dd HH:mm}]";

        // Update Order note
        req.Order.AdminNotes = (req.Order.AdminNotes ?? "") + $" | [طلب استبدال محقق #{req.Id}]";

        await _db.SaveChangesAsync();
        return Ok(new { message = "تمت الموافقة على الاستبدال وتحديث الطلب بنجاح." });
    }

    /// <summary>
    /// موافقة تمهيدية من الإدارة على طلب الاسترجاع (في انتظار الشحن للمخزن)
    /// </summary>
    [HttpPost("return-exchange-requests/{requestId}/approve-return")]
    public async Task<IActionResult> ApproveReturn(int requestId)
    {
        var req = await _db.ReturnExchangeRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null) return NotFound("الطلب غير موجود.");
        if (req.Type != ReturnExchangeType.Return) return BadRequest("هذا الطلب ليس طلب استرجاع.");
        if (req.Status != ReturnExchangeStatus.Pending) return BadRequest("الطلب ليس في حالة قيد الانتظار.");

        req.Status = ReturnExchangeStatus.Approved;
        req.AdminNotes = $"[موافقة تمهيدية - بانتظار استلام المرتجع بالمخزن {TimeHelper.GetEgyptTime():yyyy-MM-dd HH:mm}]";

        await _db.SaveChangesAsync();
        return Ok(new { message = "تمت الموافقة المبدئية. في انتظار وصول المنتجات للمخزن." });
    }

    /// <summary>
    /// تأكيد وصول الشحنة والمرتجع إلى المخزن (التأثير المخزني والمحاسبي المباشر)
    /// </summary>
    [HttpPost("return-exchange-requests/{requestId}/confirm-warehouse-receipt")]
    public async Task<IActionResult> ConfirmWarehouseReceipt(int requestId, [FromBody] ConfirmWarehouseReceiptDto dto)
    {
        var req = await _db.ReturnExchangeRequests
            .Include(r => r.Order)
                .ThenInclude(o => o.Items)
            .Include(r => r.Items)
                .ThenInclude(i => i.OrderItem)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (req == null) return NotFound("الطلب غير موجود.");
        if (req.Status == ReturnExchangeStatus.ReceivedAtWarehouse || req.Status == ReturnExchangeStatus.Completed)
        {
            return BadRequest("تم تأكيد استلام هذا الطلب بالمخزن مسبقاً.");
        }

        req.Status = ReturnExchangeStatus.ReceivedAtWarehouse;
        req.ReceivedAtWarehouseAt = TimeHelper.GetEgyptTime();
        req.RefundAccountId = dto.RefundAccountId;
        req.RefundShipping = dto.RefundShipping ?? false;
        if (!string.IsNullOrEmpty(dto.AdminNotes))
        {
            req.AdminNotes = (req.AdminNotes ?? "") + $" | {dto.AdminNotes}";
        }

        decimal totalRefundValue = 0;

        // 1. Restock items in inventory & update ReturnedQuantity
        foreach (var reqItem in req.Items)
        {
            var orderItem = reqItem.OrderItem;
            if (orderItem != null)
            {
                orderItem.ReturnedQuantity += reqItem.Quantity;
                totalRefundValue += (orderItem.UnitPrice * reqItem.Quantity);

                // Increment ProductVariant Stock if exists
                if (orderItem.ProductVariantId.HasValue)
                {
                    var variant = await _db.ProductVariants.FirstOrDefaultAsync(v => v.Id == orderItem.ProductVariantId.Value);
                    if (variant != null)
                    {
                        variant.StockQuantity += reqItem.Quantity;
                    }
                }
                // Or Product Stock
                else if (orderItem.ProductId.HasValue)
                {
                    var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == orderItem.ProductId.Value);
                    if (product != null)
                    {
                        product.TotalStock += reqItem.Quantity;
                    }
                }
            }
        }

        // 2. Check Order Return Status (Full vs Partial)
        bool isFullReturn = req.Order.Items.All(i => i.ReturnedQuantity >= i.Quantity);
        req.Order.Status = isFullReturn ? OrderStatus.Returned : OrderStatus.PartiallyReturned;

        // 3. Generate Accounting Entry for Refund
        if (dto.RefundAccountId.HasValue && totalRefundValue > 0)
        {
            var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == dto.RefundAccountId.Value);
            if (account != null)
            {
                var entry = new JournalEntry
                {
                    EntryNumber = $"JE-RTN-{req.Id}-{TimeHelper.GetEgyptTime():yyyyMMddHHmmss}",
                    EntryDate = TimeHelper.GetEgyptTime(),
                    Type = JournalEntryType.SalesReturn,
                    Status = JournalEntryStatus.Posted,
                    Reference = req.Order.OrderNumber,
                    Description = $"مرتجع فاتورة #{req.Order.OrderNumber} - طلب استرجاع ذاتي #{req.Id}",
                    OrderId = req.OrderId,
                    CreatedAt = TimeHelper.GetEgyptTime()
                };

                // Credit Refund Treasury / Bank account
                entry.Lines.Add(new JournalLine
                {
                    AccountId = account.Id,
                    Credit = totalRefundValue,
                    Debit = 0,
                    Description = $"خصم مرتجع فاتورة #{req.Order.OrderNumber}",
                    CustomerId = req.CustomerId,
                    OrderId = req.OrderId
                });

                _db.JournalEntries.Add(entry);
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "تم تأكيد وصول المرتجع للمخزن وتحديث المخزون والقيد المحاسبي بنجاح.",
            refundAmount = totalRefundValue,
            orderStatus = req.Order.Status.ToString()
        });
    }

    /// <summary>
    /// رفض طلب الاستبدال أو الاسترجاع
    /// </summary>
    [HttpPost("return-exchange-requests/{requestId}/reject")]
    public async Task<IActionResult> RejectRequest(int requestId, [FromBody] RejectReturnExchangeRequestDto dto)
    {
        var req = await _db.ReturnExchangeRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null) return NotFound("الطلب غير موجود.");

        req.Status = ReturnExchangeStatus.Rejected;
        req.RejectionReason = dto?.Reason ?? "مرفوض من قبل الإدارة";

        await _db.SaveChangesAsync();
        return Ok(new { message = "تم رفض الطلب." });
    }

    private static ReturnExchangeRequestResponseDto MapToResponseDto(ReturnExchangeRequest r)
    {
        var itemSummaries = r.Items.Select(i =>
        {
            var pName = i.OrderItem != null ? i.OrderItem.ProductNameAr : "منتج";
            var sizeStr = i.OrderItem != null && !string.IsNullOrEmpty(i.OrderItem.Size) ? $" ({i.OrderItem.Size})" : "";
            return $"{pName}{sizeStr} × {i.Quantity}";
        });

        return new ReturnExchangeRequestResponseDto
        {
            Id = r.Id,
            OrderId = r.OrderId,
            OrderNumber = r.Order != null ? r.Order.OrderNumber : "",
            CustomerId = r.CustomerId ?? r.Order?.CustomerId ?? 0,
            CustomerName = r.Customer != null && !string.IsNullOrWhiteSpace(r.Customer.FullName) 
                ? r.Customer.FullName 
                : (r.Order?.Customer != null && !string.IsNullOrWhiteSpace(r.Order.Customer.FullName) 
                    ? r.Order.Customer.FullName 
                    : "عميل"),
            CustomerPhone = r.Customer != null && !string.IsNullOrWhiteSpace(r.Customer.Phone) 
                ? r.Customer.Phone 
                : (r.Order?.Customer != null && !string.IsNullOrWhiteSpace(r.Order.Customer.Phone) 
                    ? r.Order.Customer.Phone 
                    : ""),
            Type = r.Type.ToString(),
            Status = r.Status.ToString(),
            Reason = r.Reason,
            CustomerNotes = r.CustomerNotes,
            AdminNotes = r.AdminNotes,
            RejectionReason = r.RejectionReason,
            ItemSummary = string.Join(" | ", itemSummaries),
            CreatedAt = r.CreatedAt,
            ReceivedAtWarehouseAt = r.ReceivedAtWarehouseAt,
            Items = r.Items.Select(i => new ReturnExchangeRequestItemResponseDto
            {
                Id = i.Id,
                OrderItemId = i.OrderItemId,
                ProductName = i.OrderItem != null ? i.OrderItem.ProductNameAr : "",
                Size = i.OrderItem?.Size,
                Color = i.OrderItem?.Color,
                Quantity = i.Quantity,
                UnitPrice = i.OrderItem?.UnitPrice ?? 0,
                TotalPrice = (i.OrderItem?.UnitPrice ?? 0) * i.Quantity,
                ReplacementNote = i.ReplacementNote
            }).ToList()
        };
    }
}
