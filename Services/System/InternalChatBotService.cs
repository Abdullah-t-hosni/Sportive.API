using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Hubs;
using Sportive.API.Models;

namespace Sportive.API.Services;

public interface IInternalChatBotService
{
    Task EnsureSystemChannelsExistAsync();
    Task PostOrderAlertAsync(int orderId, string trigger, string? note = null);
    Task PostStockAlertAsync(int productId, int? variantId, int remainingStock, int reorderLevel);
    Task PostShiftAlertAsync(int shiftId, string cashierName, decimal expectedAmount, decimal actualAmount, decimal difference);
    Task<object> ExecuteSlashCommandAsync(int channelId, string userId, string userName, string command, string query);
    Task<object> ClaimOrResolveMessageAsync(int messageId, string userId, string userName, string action, string? note);
}

public class InternalChatBotService : IInternalChatBotService
{
    public const string BotUserId = "sportive-bot-ops";
    public const string BotUserName = "سبورتيف بوت 🤖";
    public const string BotAvatarUrl = "/favicon.svg";

    public const string ChannelKeyOrders = "sys_bot_orders";      // طلبات المتجر الأونلاين (Website + General)
    public const string ChannelKeyPos = "sys_bot_pos";            // فواتير الكاشير (POS)
    public const string ChannelKeyInventory = "sys_bot_inventory";
    public const string ChannelKeyTreasury = "sys_bot_treasury";

    private const string LegacyOrdersChannelName = "📦 رادار الطلبات والعمليات";

    // Keep Arabic readable in stored JSON (default encoder escapes every Arabic char to \uXXXX)
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly AppDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly INotificationService _notifications;
    private readonly ILogger<InternalChatBotService> _logger;

    public InternalChatBotService(
        AppDbContext db,
        IHubContext<NotificationHub> hub,
        INotificationService notifications,
        ILogger<InternalChatBotService> logger)
    {
        _db = db;
        _hub = hub;
        _notifications = notifications;
        _logger = logger;
    }

    /// <summary>
    /// Ensures system channels exist in DB without adding any members.
    /// Only Admin can manually add employees to these channels.
    /// </summary>
    public async Task EnsureSystemChannelsExistAsync()
    {
        try
        {
            var defChannels = new[]
            {
                new { Key = ChannelKeyOrders, Name = "🛒 رادار طلبات المتجر", Icon = "🛒", Desc = "طلبات المتجر الأونلاين الجديدة، المرتجعات، وتحديثات الشحن" },
                new { Key = ChannelKeyPos, Name = "🧾 رادار فواتير الكاشير", Icon = "🧾", Desc = "فواتير البيع من نقاط البيع (الكاشير) لحظة بلحظة" },
                new { Key = ChannelKeyInventory, Name = "🚨 طوارئ المخزون والنواقص", Icon = "🚨", Desc = "تنبيهات الأصناف الناقصة واقتراب نفاد المقاسات" },
                new { Key = ChannelKeyTreasury, Name = "💰 الخزينة والرقابة المالية", Icon = "💰", Desc = "تقارير إقفال الشفتات ومصروفات الخزينة اليومية" }
            };

            foreach (var def in defChannels)
            {
                var ch = await _db.InternalChatChannels
                    .FirstOrDefaultAsync(c => c.DirectKey == def.Key);

                if (ch == null)
                {
                    ch = new InternalChatChannel
                    {
                        Name = def.Name,
                        Description = def.Desc,
                        Icon = def.Icon,
                        DirectKey = def.Key,
                        Type = InternalChatChannelType.Group,
                        CreatedByUserId = BotUserId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.InternalChatChannels.Add(ch);
                    await _db.SaveChangesAsync();
                }
                else if (def.Key == ChannelKeyOrders && ch.Name == LegacyOrdersChannelName)
                {
                    // Rename the old mixed channel → it now carries store orders only (members/history preserved)
                    ch.Name = def.Name;
                    ch.Icon = def.Icon;
                    ch.Description = def.Desc;
                    await _db.SaveChangesAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error ensuring bot system channels exist");
        }
    }

    public async Task PostOrderAlertAsync(int orderId, string trigger, string? note = null)
    {
        try
        {
            var order = await _db.Orders
                .Include(o => o.Customer)
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return;

            bool isPos = order.Source == OrderSource.POS;
            var targetKey = isPos ? ChannelKeyPos : ChannelKeyOrders;

            var channel = await _db.InternalChatChannels
                .Include(c => c.Members)
                .FirstOrDefaultAsync(c => c.DirectKey == targetKey);

            if (channel == null) return;

            if (isPos && trigger == "NewOnlineOrder") trigger = "NewPosOrder";

            string triggerTitle = trigger switch
            {
                "NewOnlineOrder" => "🛍️ طلب أونلاين جديد وارد",
                "NewPosOrder" => "🧾 فاتورة كاشير جديدة",
                "HighValueOrder" => "💎 طلب استثنائي بقيمة كبرى (VIP)",
                "Cancelled" => "❌ تم إلغاء الطلب",
                "ReturnRequest" => "🔄 طلب استرجاع / استبدال جديد",
                "DelayedShipping" => "⏳ طلب متأخر لدى شركة الشحن",
                _ => "📦 تحديث على حالة الطلب"
            };

            string custName = order.Customer?.FullName ?? "عميل متجر";
            string custPhone = order.Customer?.Phone ?? "";
            string itemsSummary = string.Join(" • ", order.Items.Take(3).Select(i => $"{i.ProductNameAr} ({i.Quantity}x)"));

            var meta = new
            {
                botType = "BotOrderAlert",
                trigger,
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                customerName = custName,
                customerPhone = custPhone,
                totalAmount = order.TotalAmount,
                status = order.Status.ToString(),
                paymentMethod = order.PaymentMethod.ToString(),
                itemsCount = order.Items.Count,
                itemsSummary,
                note
            };

            string metaJson = JsonSerializer.Serialize(meta);
            string text = $"{triggerTitle}\nرقم الطلب: #{order.OrderNumber}\nالعميل: {custName} ({custPhone})\nالقيمة: {order.TotalAmount:N0} ج.م | الأصناف: {itemsSummary}";

            await SaveAndBroadcastBotMessageAsync(channel.Id, text, metaJson, "BotOrderAlert", order.Id, order.OrderNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post bot order alert for order #{OrderId}", orderId);
        }
    }

    public async Task PostStockAlertAsync(int productId, int? variantId, int remainingStock, int reorderLevel)
    {
        try
        {
            var product = await _db.Products
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == productId);

            if (product == null) return;

            ProductVariant? variant = null;
            if (variantId.HasValue)
            {
                variant = await _db.ProductVariants.FirstOrDefaultAsync(v => v.Id == variantId.Value);
            }

            var channel = await _db.InternalChatChannels
                .Include(c => c.Members)
                .FirstOrDefaultAsync(c => c.DirectKey == ChannelKeyInventory);

            if (channel == null) return;

            string variantInfo = variant != null 
                ? $"({variant.ColorAr ?? variant.Color ?? ""} - مقاس {variant.Size ?? ""})".Trim() 
                : "";

            bool isZero = remainingStock <= 0;
            string triggerTitle = isZero ? "⛔ نفاد المخزون بالكامل (رصيد صفر)" : "🚨 تنبيه اقتراب نفاد المخزون (تحت حد الطلب)";

            var meta = new
            {
                botType = "BotStockAlert",
                productId = product.Id,
                variantId = variant?.Id,
                productName = product.NameAr,
                variantInfo,
                sku = variant != null ? product.SKU : product.SKU,
                remainingStock,
                reorderLevel,
                imageUrl = variant?.ImageUrl ?? product.Images.FirstOrDefault()?.ImageUrl
            };

            string metaJson = JsonSerializer.Serialize(meta);
            string text = $"{triggerTitle}\nالمنتج: {product.NameAr} {variantInfo}\nالكمية المتبقية: {remainingStock} قطعة (حد الطلب: {reorderLevel})\nكود الصنف: {product.SKU}";

            await SaveAndBroadcastBotMessageAsync(channel.Id, text, metaJson, "BotStockAlert", product.Id, product.SKU);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post bot stock alert for product #{ProductId}", productId);
        }
    }

    public async Task PostShiftAlertAsync(int shiftId, string cashierName, decimal expectedAmount, decimal actualAmount, decimal difference)
    {
        try
        {
            var channel = await _db.InternalChatChannels
                .Include(c => c.Members)
                .FirstOrDefaultAsync(c => c.DirectKey == ChannelKeyTreasury);

            if (channel == null) return;

            string diffText = difference == 0 
                ? "متطابق تماماً ✅" 
                : (difference > 0 ? $"زيادة +{difference:N2} ج.م 🟢" : $"عجز {difference:N2} ج.م 🔴");

            var meta = new
            {
                botType = "BotShiftAlert",
                shiftId,
                cashierName,
                expectedAmount,
                actualAmount,
                difference,
                diffText
            };

            string metaJson = JsonSerializer.Serialize(meta);
            string text = $"💰 تقرير إغلاق شيفت الخزينة\nالكاشير: {cashierName}\nالمتوقع بالدرج: {expectedAmount:N2} ج.م | الفعلي: {actualAmount:N2} ج.م\nالنتيجة: {diffText}";

            await SaveAndBroadcastBotMessageAsync(channel.Id, text, metaJson, "BotShiftAlert", shiftId, $"SHIFT-{shiftId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post bot shift alert for shift #{ShiftId}", shiftId);
        }
    }

    public async Task<object> ExecuteSlashCommandAsync(int channelId, string userId, string userName, string command, string query)
    {
        var cleanCmd = (command ?? "").Trim().ToLowerInvariant().TrimStart('/');
        var cleanQuery = (query ?? "").Trim();

        string responseText = "";
        object? responseMeta = null;
        string? linkedEntityType = "BotCommandResult";
        int? linkedEntityId = null;
        string? linkedEntityRef = cleanCmd;

        // ── Channel scoping: each command belongs to a specific radar group ──
        var channel = await _db.InternalChatChannels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == channelId);

        if (channel == null || string.IsNullOrEmpty(channel.DirectKey) || !channel.DirectKey.StartsWith("sys_bot_"))
            throw new InvalidOperationException("أوامر الروبوت متاحة فقط داخل مجموعات الرادار التلقائي");

        bool isMember = await _db.InternalChatMembers.AnyAsync(m => m.ChannelId == channelId && m.UserId == userId);
        if (!isMember)
            throw new UnauthorizedAccessException("لست عضواً في هذه المجموعة");

        var allowedByChannel = new Dictionary<string, string[]>
        {
            [ChannelKeyOrders]    = new[] { "order", "طلب", "customer", "عميل", "sales", "مبيعات" },
            [ChannelKeyPos]       = new[] { "order", "طلب", "invoice", "فاتورة", "sales", "مبيعات" },
            [ChannelKeyInventory] = new[] { "stock", "مخزون" },
            [ChannelKeyTreasury]  = new[] { "sales", "مبيعات" }
        };

        if (allowedByChannel.TryGetValue(channel.DirectKey, out var allowed) && !allowed.Contains(cleanCmd))
        {
            // Unknown / out-of-scope command → show this group's help only
            cleanCmd = "help:" + channel.DirectKey;
        }

        switch (cleanCmd)
        {
            case "stock":
            case "مخزون":
                if (string.IsNullOrWhiteSpace(cleanQuery))
                {
                    responseText = "يرجى كتابة اسم الصنف أو الكود بعد الأمر، مثلاً:\n/مخزون ترينج نايكي أو /stock SPT-102";
                }
                else
                {
                    var products = await _db.Products
                        .Include(p => p.Variants)
                        .Include(p => p.Images)
                        .Where(p => p.SKU == cleanQuery || p.NameAr.Contains(cleanQuery) || p.NameEn.Contains(cleanQuery))
                        .Take(3)
                        .ToListAsync();

                    if (!products.Any())
                    {
                        responseText = $"🔍 لم يتم العثور على أي منتج يطابق: \"{cleanQuery}\"";
                    }
                    else
                    {
                        var first = products.First();
                        linkedEntityId = first.Id;
                        linkedEntityRef = first.SKU;

                        var variantsList = first.Variants.Select(v => new
                        {
                            size = v.Size ?? "-",
                            color = v.ColorAr ?? v.Color ?? "-",
                            stock = v.StockQuantity,
                            isLow = v.StockQuantity <= v.ReorderLevel
                        }).ToList();

                        responseMeta = new
                        {
                            botType = "BotStockQuery",
                            productId = first.Id,
                            name = first.NameAr,
                            sku = first.SKU,
                            price = first.Price,
                            totalStock = first.TotalStock,
                            variants = variantsList,
                            imageUrl = first.Images.FirstOrDefault()?.ImageUrl
                        };

                        var varLines = string.Join("\n", variantsList.Select(v => $"  • {v.color} - مقاس {v.size}: {v.stock} قطعة {(v.stock <= 0 ? "❌ نفد" : (v.isLow ? "⚠️ ناقص" : "✅"))}"));
                        responseText = $"📦 تقرير رصيد الصنف:\n{first.NameAr} (كود: {first.SKU})\nإجمالي المخزون: {first.TotalStock} قطعة | السعر: {first.Price:N0} ج.م\nتفاصيل المقاسات والألوان:\n{varLines}";
                    }
                }
                break;

            case "order":
            case "طلب":
            case "invoice":
            case "فاتورة":
                if (string.IsNullOrWhiteSpace(cleanQuery))
                {
                    responseText = channel.DirectKey == ChannelKeyPos
                        ? "يرجى كتابة رقم الفاتورة بعد الأمر، مثلاً:\n/فاتورة 10482 أو /طلب POS-2610-0042"
                        : "يرجى كتابة رقم الطلب بعد الأمر، مثلاً:\n/طلب 10482 أو /order SPT-2610-0042";
                }
                else
                {
                    var cleanOrderNum = cleanQuery.TrimStart('#');
                    var order = await _db.Orders
                        .Include(o => o.Customer)
                        .Include(o => o.Items)
                        .FirstOrDefaultAsync(o => o.OrderNumber == cleanOrderNum || o.Id.ToString() == cleanOrderNum);

                    if (order == null)
                    {
                        responseText = channel.DirectKey == ChannelKeyPos
                            ? $"🔍 لم يتم العثور على فاتورة كاشير برقم: \"{cleanQuery}\""
                            : $"🔍 لم يتم العثور على طلب برقم: \"{cleanQuery}\"";
                    }
                    else
                    {
                        linkedEntityId = order.Id;
                        linkedEntityRef = order.OrderNumber;

                        bool isPosOrder = order.Source == OrderSource.POS;

                        responseMeta = new
                        {
                            botType = "BotOrderQuery",
                            orderId = order.Id,
                            orderNumber = order.OrderNumber,
                            customerName = order.Customer?.FullName ?? (isPosOrder ? "عميل كاشير نقدي" : "عميل"),
                            phone = order.Customer?.Phone ?? "",
                            total = order.TotalAmount,
                            status = order.Status.ToString(),
                            payment = order.PaymentMethod.ToString(),
                            itemsCount = order.Items.Count,
                            date = order.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                        };

                        string typeTitle = isPosOrder ? "🧾 فاتورة كاشير" : "🛒 طلب متجر أونلاين";
                        responseText = $"📋 تفاصيل {typeTitle} #{order.OrderNumber}:\n" +
                                       $"العميل: {order.Customer?.FullName ?? (isPosOrder ? "عميل كاشير نقدي" : "عميل")} {(string.IsNullOrEmpty(order.Customer?.Phone) ? "" : $"({order.Customer?.Phone})")}\n" +
                                       $"الحالة: {order.Status} | الإجمالي: {order.TotalAmount:N0} ج.م\n" +
                                       $"طريقة الدفع: {order.PaymentMethod} | عدد الأصناف: {order.Items.Count} قطعة";
                    }
                }
                break;

            case "sales":
            case "مبيعات":
                var todayStart = DateTime.UtcNow.Date;
                var allTodayOrders = await _db.Orders
                    .AsNoTracking()
                    .Where(o => o.CreatedAt >= todayStart && o.Status != OrderStatus.Cancelled)
                    .ToListAsync();

                if (channel.DirectKey == ChannelKeyOrders)
                {
                    // 🛒 رادار طلبات المتجر الأونلاين فقط
                    var onlineOrders = allTodayOrders.Where(o => o.Source != OrderSource.POS).ToList();
                    var count = onlineOrders.Count;
                    var revenue = onlineOrders.Sum(o => o.TotalAmount);
                    var avg = count > 0 ? revenue / count : 0;
                    var delivered = onlineOrders.Count(o => o.Status == OrderStatus.Delivered);
                    var processing = onlineOrders.Count(o => o.Status == OrderStatus.Processing || o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.OutForDelivery);

                    responseMeta = new
                    {
                        botType = "BotSalesSummary",
                        channel = "orders",
                        date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        ordersCount = count,
                        totalRevenue = revenue,
                        avgOrderValue = avg,
                        deliveredCount = delivered
                    };

                    responseText = $"🛒 رادار مبيعات المتجر الأونلاين اليوم ({DateTime.UtcNow:dd/MM/yyyy}):\n" +
                                   $"• إجمالي مبيعات المتجر: {revenue:N0} ج.م\n" +
                                   $"• عدد طلبات المتجر: {count} طلب أونلاين\n" +
                                   $"• متوسط قيمة الطلب: {avg:N0} ج.م\n" +
                                   $"• تم التوصيل: {delivered} طلب\n" +
                                   $"• قيد التجهيز والشحن: {processing} طلب";
                }
                else if (channel.DirectKey == ChannelKeyPos)
                {
                    // 🧾 رادار فواتير الكاشير فقط
                    var posOrders = allTodayOrders.Where(o => o.Source == OrderSource.POS).ToList();
                    var count = posOrders.Count;
                    var revenue = posOrders.Sum(o => o.TotalAmount);
                    var avg = count > 0 ? revenue / count : 0;
                    var cashRev = posOrders.Where(o => o.PaymentMethod == PaymentMethod.Cash).Sum(o => o.TotalAmount);
                    var cardRev = posOrders.Where(o => o.PaymentMethod == PaymentMethod.CreditCard || o.PaymentMethod == PaymentMethod.Bank || o.PaymentMethod == PaymentMethod.InstaPay).Sum(o => o.TotalAmount);

                    responseMeta = new
                    {
                        botType = "BotSalesSummary",
                        channel = "pos",
                        date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        ordersCount = count,
                        totalRevenue = revenue,
                        avgOrderValue = avg,
                        cashRevenue = cashRev,
                        cardRevenue = cardRev
                    };

                    responseText = $"🧾 رادار فواتير الكاشير اليوم ({DateTime.UtcNow:dd/MM/yyyy}):\n" +
                                   $"• إجمالي مبيعات الكاشير: {revenue:N0} ج.م\n" +
                                   $"• عدد فواتير البيع: {count} فاتورة كاشير\n" +
                                   $"• متوسط الفاتورة: {avg:N0} ج.م\n" +
                                   $"• مدفوع نقداً (كاش): {cashRev:N0} ج.م\n" +
                                   $"• مدفوع إلكتروني (فيزا/شبكة): {cardRev:N0} ج.م";
                }
                else
                {
                    // 💰 الخزينة والرقابة المالية: إجمالي الشركة العام (متجر + كاشير)
                    var onlineOrders = allTodayOrders.Where(o => o.Source != OrderSource.POS).ToList();
                    var posOrders = allTodayOrders.Where(o => o.Source == OrderSource.POS).ToList();

                    var totalRev = allTodayOrders.Sum(o => o.TotalAmount);
                    var onlineRev = onlineOrders.Sum(o => o.TotalAmount);
                    var posRev = posOrders.Sum(o => o.TotalAmount);

                    responseMeta = new
                    {
                        botType = "BotSalesSummary",
                        channel = "treasury",
                        date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        ordersCount = allTodayOrders.Count,
                        totalRevenue = totalRev,
                        onlineRevenue = onlineRev,
                        onlineCount = onlineOrders.Count,
                        posRevenue = posRev,
                        posCount = posOrders.Count
                    };

                    responseText = $"💰 تقرير مبيعات وخزينة الشركة الشامل اليوم ({DateTime.UtcNow:dd/MM/yyyy}):\n" +
                                   $"• إجمالي إيرادات اليوم: {totalRev:N0} ج.م ({allTodayOrders.Count} عملية)\n" +
                                   $"  ├─ 🛒 مبيعات المتجر الأونلاين: {onlineRev:N0} ج.م ({onlineOrders.Count} طلب)\n" +
                                   $"  └─ 🧾 مبيعات فواتير الكاشير: {posRev:N0} ج.م ({posOrders.Count} فاتورة)\n" +
                                   $"• متوسط المعاملة: {(allTodayOrders.Count > 0 ? totalRev / allTodayOrders.Count : 0):N0} ج.م";
                }
                break;

            case "customer":
            case "عميل":
                if (string.IsNullOrWhiteSpace(cleanQuery))
                {
                    responseText = "يرجى إدخال رقم هاتف العميل أو اسمه، مثلاً:\n/عميل 01012345678";
                }
                else
                {
                    var cust = await _db.Customers
                        .Include(c => c.Orders)
                        .FirstOrDefaultAsync(c => c.Phone == cleanQuery || c.FullName.Contains(cleanQuery));

                    if (cust == null)
                    {
                        responseText = $"🔍 لم يتم العثور على عميل مسجل ببيانات: \"{cleanQuery}\"";
                    }
                    else
                    {
                        var totalOrders = cust.Orders.Count;
                        var completedOrders = cust.Orders.Count(o => o.Status == OrderStatus.Delivered);
                        var totalSpent = cust.Orders.Where(o => o.Status == OrderStatus.Delivered).Sum(o => o.TotalAmount);

                        responseMeta = new
                        {
                            botType = "BotCustomerQuery",
                            customerId = cust.Id,
                            name = cust.FullName,
                            phone = cust.Phone,
                            totalOrders,
                            completedOrders,
                            totalSpent
                        };

                        responseText = $"👤 بيانات العميل: {cust.FullName} ({cust.Phone})\nإجمالي الطلبات: {totalOrders} طلب | المسلم بنجاح: {completedOrders}\nإجمالي المشتريات: {totalSpent:N0} ج.م";
                    }
                }
                break;

            default:
                responseText = channel.DirectKey switch
                {
                    ChannelKeyOrders =>
                        "🤖 أوامر رادار طلبات المتجر الأونلاين:\n" +
                        "• /طلب [رقم الطلب]: فحص حالة وبيانات أوردر المتجر\n" +
                        "• /عميل [هاتف/اسم]: سجل ومشتريات العميل\n" +
                        "• /مبيعات: ملخص مبيعات المتجر الأونلاين فقط اليوم",
                    ChannelKeyPos =>
                        "🤖 أوامر رادار فواتير الكاشير:\n" +
                        "• /طلب أو /فاتورة [رقم الفاتورة]: فحص بيانات فاتورة الكاشير\n" +
                        "• /مبيعات: ملخص مبيعات وإيرادات الكاشير فقط اليوم",
                    ChannelKeyInventory =>
                        "🤖 أوامر طوارئ المخزون المتاحة:\n" +
                        "• /مخزون [اسم/كود]: رصيد الصنف بكل المقاسات والألوان",
                    ChannelKeyTreasury =>
                        "🤖 أوامر الخزينة والرقابة المالية:\n" +
                        "• /مبيعات: ملخص مبيعات الشركة الشامل (المتجر + الكاشير)",
                    _ => "🤖 لا توجد أوامر متاحة في هذه المجموعة"
                };
                break;
        }

        string metaJson = responseMeta != null ? JsonSerializer.Serialize(responseMeta) : "{}";
        var botMsg = await SaveAndBroadcastBotMessageAsync(channelId, responseText, metaJson, linkedEntityType, linkedEntityId, linkedEntityRef);
        return botMsg;
    }

    public async Task<object> ClaimOrResolveMessageAsync(int messageId, string userId, string userName, string action, string? note)
    {
        var msg = await _db.InternalChatMessages
            .Include(m => m.Channel)
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (msg == null) throw new InvalidOperationException("Message not found");

        var resolutionTag = action == "resolve" 
            ? $"resolved:{userId}:{userName}:{note ?? ""}:{DateTime.UtcNow:s}" 
            : $"claimed:{userId}:{userName}::{DateTime.UtcNow:s}";

        // Store claim tag in LinkedEntityRef
        msg.LinkedEntityRef = resolutionTag;
        await _db.SaveChangesAsync();

        var updatedObj = new
        {
            messageId = msg.Id,
            channelId = msg.ChannelId,
            action,
            userId,
            userName,
            note,
            linkedEntityRef = resolutionTag,
            updatedAt = DateTime.UtcNow
        };

        // Broadcast to channel members via SignalR
        var memberUserIds = await _db.InternalChatMembers
            .AsNoTracking()
            .Where(m => m.ChannelId == msg.ChannelId)
            .Select(m => m.UserId)
            .ToListAsync();

        foreach (var mid in memberUserIds)
        {
            try
            {
                await _hub.Clients.Group($"user_{mid}").SendAsync("InternalChatMessageUpdated", updatedObj);
            }
            catch { }
        }

        return updatedObj;
    }

    private async Task<object> SaveAndBroadcastBotMessageAsync(
        int channelId, string text, string metaJson, string? linkedEntityType, int? linkedEntityId, string? linkedEntityRef)
    {
        var msg = new InternalChatMessage
        {
            ChannelId = channelId,
            SenderId = BotUserId,
            SenderName = BotUserName,
            SenderAvatarUrl = BotAvatarUrl,
            Text = text,
            FileName = metaJson,
            MediaType = "bot_card",
            LinkedEntityType = linkedEntityType,
            LinkedEntityId = linkedEntityId,
            LinkedEntityRef = linkedEntityRef,
            SentAt = DateTime.UtcNow
        };

        _db.InternalChatMessages.Add(msg);
        await _db.SaveChangesAsync();

        var channel = await _db.InternalChatChannels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId);
        var channelName = channel?.Name ?? "شات العمليات";
        var channelType = channel?.Type.ToString() ?? "Group";

        var memberUserIds = await _db.InternalChatMembers
            .AsNoTracking()
            .Where(m => m.ChannelId == channelId)
            .Select(m => m.UserId)
            .ToListAsync();

        var mapped = new
        {
            id = msg.Id,
            channelId = msg.ChannelId,
            channelName,
            channelType,
            senderId = msg.SenderId,
            senderName = msg.SenderName,
            senderAvatarUrl = msg.SenderAvatarUrl,
            text = msg.Text,
            fileName = msg.FileName,
            mediaType = msg.MediaType,
            linkedEntityType = msg.LinkedEntityType,
            linkedEntityId = msg.LinkedEntityId,
            linkedEntityRef = msg.LinkedEntityRef,
            sentAt = msg.SentAt,
            isMe = false,
            isDelivered = true,
            reactions = Array.Empty<object>()
        };

        // Broadcast via SignalR to channel members
        foreach (var mid in memberUserIds)
        {
            try
            {
                await _hub.Clients.Group($"user_{mid}").SendAsync("ReceiveInternalChatMessage", mapped);
            }
            catch { }
        }

        // Web push notification to mobile devices of channel members
        try
        {
            await _notifications.SendChatWebPushAsync(memberUserIds, BotUserName, channelName, true, text, channelId, "bot_card");
        }
        catch { }

        return mapped;
    }
}
