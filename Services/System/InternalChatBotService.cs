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

using Sportive.API.Utils;

namespace Sportive.API.Services;

public interface IInternalChatBotService
{
    Task EnsureSystemChannelsExistAsync();
    Task PostOrderAlertAsync(int orderId, string trigger, string? note = null);
    Task PostStockAlertAsync(int productId, int? variantId, int remainingStock, int reorderLevel);
    Task PostShiftAlertAsync(POSShiftClosure closure);
    Task PostShiftAlertAsync(int shiftId, string cashierName, decimal expectedAmount, decimal actualAmount, decimal difference);
    Task PostDailyPartnersAndStoreReportAsync(DateTime? forDate = null);
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
    public const string ChannelKeyPartners = "sys_bot_partners";  // تقارير الشركاء ومحاسبة المتجر اليومية

    private const string LegacyOrdersChannelName = "📦 رادار الطلبات والعمليات";

    // Keep Arabic readable in stored JSON (default encoder escapes every Arabic char to \uXXXX)
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly AppDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly INotificationService _notifications;
    private readonly Sportive.API.Utils.EncryptionHelper _encryptionHelper;
    private readonly ILogger<InternalChatBotService> _logger;

    public InternalChatBotService(
        AppDbContext db,
        IHubContext<NotificationHub> hub,
        INotificationService notifications,
        Sportive.API.Utils.EncryptionHelper encryptionHelper,
        ILogger<InternalChatBotService> logger)
    {
        _db = db;
        _hub = hub;
        _notifications = notifications;
        _encryptionHelper = encryptionHelper ?? Customer.EncryptionHelper!;
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
                new { Key = ChannelKeyTreasury, Name = "💰 الخزينة والرقابة المالية", Icon = "💰", Desc = "تقارير إقفال الشفتات ومصروفات الخزينة اليومية" },
                new { Key = ChannelKeyPartners, Name = "🤝 الشركاء", Icon = "🤝", Desc = "التقارير اليومية الشاملة للشركاء ومحاسبة وأرباح المتجر الإلكتروني" }
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

                // Auto-enroll Admins & Managers into Partners channel
                if (def.Key == ChannelKeyPartners)
                {
                    var partnerRoleIds = await _db.Roles
                        .Where(r => r.Name == AppRoles.SuperAdmin || r.Name == AppRoles.Admin || r.Name == AppRoles.Manager)
                        .Select(r => r.Id)
                        .ToListAsync();

                    var partnerUserIds = await _db.UserRoles
                        .Where(ur => partnerRoleIds.Contains(ur.RoleId))
                        .Select(ur => ur.UserId)
                        .Distinct()
                        .ToListAsync();

                    var partnerUsers = await _db.Users
                        .Where(u => u.IsActive && partnerUserIds.Contains(u.Id))
                        .Select(u => new { u.Id, u.FullName, u.UserName })
                        .ToListAsync();

                    var existingMemberIds = await _db.InternalChatMembers
                        .Where(m => m.ChannelId == ch.Id)
                        .Select(m => m.UserId)
                        .ToListAsync();

                    bool addedMember = false;
                    foreach (var u in partnerUsers)
                    {
                        if (!existingMemberIds.Contains(u.Id))
                        {
                            _db.InternalChatMembers.Add(new InternalChatMember
                            {
                                ChannelId = ch.Id,
                                UserId = u.Id,
                                UserName = string.IsNullOrWhiteSpace(u.FullName) ? (u.UserName ?? "شريك") : u.FullName,
                                JoinedAt = DateTime.UtcNow
                            });
                            addedMember = true;
                        }
                    }
                    if (addedMember)
                    {
                        await _db.SaveChangesAsync();
                    }
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

            string metaJson = JsonSerializer.Serialize(meta, JsonOpts);
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

            string metaJson = JsonSerializer.Serialize(meta, JsonOpts);
            string text = $"{triggerTitle}\nالمنتج: {product.NameAr} {variantInfo}\nالكمية المتبقية: {remainingStock} قطعة (حد الطلب: {reorderLevel})\nكود الصنف: {product.SKU}";

            await SaveAndBroadcastBotMessageAsync(channel.Id, text, metaJson, "BotStockAlert", product.Id, product.SKU);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post bot stock alert for product #{ProductId}", productId);
        }
    }

    public async Task PostShiftAlertAsync(POSShiftClosure closure)
    {
        try
        {
            var channel = await _db.InternalChatChannels
                .Include(c => c.Members)
                .FirstOrDefaultAsync(c => c.DirectKey == ChannelKeyTreasury);

            if (channel == null) return;

            string diffText = closure.Variance == 0 
                ? "متطابق تماماً ✅" 
                : (closure.Variance > 0 ? $"زيادة +{closure.Variance:N2} ج.م 🟢" : $"عجز {closure.Variance:N2} ج.م 🔴");

            var meta = new
            {
                botType = "BotShiftAlert",
                shiftId = closure.Id,
                cashierName = closure.ClosedBy,
                stationId = closure.StationId,
                branchId = closure.BranchId,
                closureDate = closure.ClosureDate,
                expectedAmount = closure.ExpectedCash,
                actualAmount = closure.ActualCash,
                difference = closure.Variance,
                diffText,
                grossSales = closure.GrossSales,
                netSales = closure.NetSales,
                cashSales = closure.CashSales,
                cardSales = closure.CardSales,
                vodafoneCashSales = closure.VodafoneCashSales,
                instapaySales = closure.InstapaySales,
                walletSales = closure.WalletSales,
                creditSales = closure.CreditSales,
                expenses = closure.Expenses,
                safeDrops = closure.SafeDrops,
                returns = closure.Returns,
                discounts = closure.Discounts,
                startingBalance = closure.StartingBalance,
                createdAt = closure.CreatedAt
            };

            string metaJson = JsonSerializer.Serialize(meta, JsonOpts);
            string text = $"💰 تقرير تقفيل وردية كاشير #{closure.Id}\n" +
                          $"• الكاشير: {closure.ClosedBy} | المحطة: {closure.StationId}\n" +
                          $"• صافي المبيعات: {closure.NetSales:N2} ج.م (كاش: {closure.CashSales:N2} | فيزا: {closure.CardSales:N2})\n" +
                          $"• المصروفات والمرتجع: {(closure.Expenses + closure.Returns):N2} ج.م\n" +
                          $"• المتوقع بالدرج: {closure.ExpectedCash:N2} ج.م | الفعلي: {closure.ActualCash:N2} ج.م\n" +
                          $"• النتيجة: {diffText}";

            await SaveAndBroadcastBotMessageAsync(channel.Id, text, metaJson, "BotShiftAlert", closure.Id, $"SHIFT-{closure.Id}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post bot shift alert for shift #{ShiftId}", closure.Id);
        }
    }

    public async Task PostShiftAlertAsync(int shiftId, string cashierName, decimal expectedAmount, decimal actualAmount, decimal difference)
    {
        var closure = await _db.POSShiftClosures.FindAsync(shiftId);
        if (closure != null)
        {
            await PostShiftAlertAsync(closure);
            return;
        }

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

            string metaJson = JsonSerializer.Serialize(meta, JsonOpts);
            string text = $"💰 تقرير تقفيل شيفت الخزينة #{shiftId}\nالكاشير: {cashierName}\nالمتوقع بالدرج: {expectedAmount:N2} ج.م | الفعلي: {actualAmount:N2} ج.م\nالنتيجة: {diffText}";

            await SaveAndBroadcastBotMessageAsync(channel.Id, text, metaJson, "BotShiftAlert", shiftId, $"SHIFT-{shiftId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post bot shift alert for shift #{ShiftId}", shiftId);
        }
    }

    public async Task PostDailyPartnersAndStoreReportAsync(DateTime? forDate = null)
    {
        try
        {
            await EnsureSystemChannelsExistAsync();

            var channel = await _db.InternalChatChannels
                .Include(c => c.Members)
                .FirstOrDefaultAsync(c => c.DirectKey == ChannelKeyPartners);

            if (channel == null)
            {
                _logger.LogWarning("Channel sys_bot_partners not found. Skipping daily report.");
                return;
            }

            var storeNow = TimeHelper.GetEgyptTime();
            var targetDate = (forDate ?? storeNow.Date.AddDays(-1)).Date;
            var endHour = TimeHelper.GetBusinessDayEndHour();

            var dayStart = targetDate.AddHours(endHour);
            var dayEnd = targetDate.AddDays(1).AddHours(endHour).AddTicks(-1);

            string dateDisplay = targetDate.ToString("yyyy-MM-dd");
            string dayNameAr = targetDate.ToString("dddd", new System.Globalization.CultureInfo("ar-EG"));

            // ════════════════════════════════════════════════════════════
            // 1. تقرير الشركاء المالي الشامل (PARTNERS COMPREHENSIVE REPORT)
            // ════════════════════════════════════════════════════════════
            var salesQuery = _db.Orders.AsNoTracking().Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned);
            var posDailyGross = await salesQuery.Where(o => o.Source == OrderSource.POS && o.CreatedAt >= dayStart && o.CreatedAt <= dayEnd).SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;
            var webDailyGross = await salesQuery.Where(o => o.Source == OrderSource.Website && o.CreatedAt >= dayStart && o.CreatedAt <= dayEnd).SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;

            var salesReturnsQuery = _db.JournalEntries.AsNoTracking().Where(j => j.Type == JournalEntryType.SalesReturn);
            var dailyPosReturns = await salesReturnsQuery
                .Where(j => j.EntryDate >= dayStart && j.EntryDate <= dayEnd && j.Order != null && j.Order.Source == OrderSource.POS)
                .SelectMany(j => j.Lines).Where(l => l.Account.Type == AccountType.Asset)
                .SumAsync(l => (decimal?)l.Credit) ?? 0m;

            var dailyWebReturns = await salesReturnsQuery
                .Where(j => j.EntryDate >= dayStart && j.EntryDate <= dayEnd && j.Order != null && j.Order.Source == OrderSource.Website)
                .SelectMany(j => j.Lines).Where(l => l.Account.Type == AccountType.Asset)
                .SumAsync(l => (decimal?)l.Credit) ?? 0m;

            var posNet = posDailyGross - dailyPosReturns;
            var webNet = webDailyGross - dailyWebReturns;
            var totalNetSales = posNet + webNet;
            var totalReturns = dailyPosReturns + dailyWebReturns;

            // Cash Flow
            var cashAccTypes = new[] { "1101", "1102", "1103" };
            var cashAccounts = await _db.Accounts.AsNoTracking()
                .Where(a => cashAccTypes.Any(c => a.Code.StartsWith(c)) 
                         && a.IsLeaf 
                         && a.Code != "110106"
                         && !a.NameAr.Contains("جرد") 
                         && !a.NameAr.Contains("مخزون") 
                         && !a.NameAr.Contains("عجز") 
                         && !a.NameAr.Contains("زيادة") 
                         && !a.NameAr.Contains("تقفيل"))
                .Select(a => a.Id)
                .ToListAsync();

            var jlQuery = _db.JournalLines.AsNoTracking().Where(l => cashAccounts.Contains(l.AccountId));
            var dailyCollections = await jlQuery.Where(l => l.JournalEntry.Type != JournalEntryType.Manual && l.JournalEntry.EntryDate >= dayStart && l.JournalEntry.EntryDate <= dayEnd).SumAsync(l => (decimal?)l.Debit) ?? 0m;
            var dailyOutflows = await jlQuery.Where(l => l.JournalEntry.Type != JournalEntryType.OpeningBalance && l.JournalEntry.EntryDate >= dayStart && l.JournalEntry.EntryDate <= dayEnd).SumAsync(l => (decimal?)l.Credit) ?? 0m;
            var dailyNetCashFlow = dailyCollections - dailyOutflows;

            // Liquidity up to dayEnd
            var balanceAccounts = await _db.Accounts.AsNoTracking()
                .Where(a => a.IsLeaf && (a.Code.StartsWith("1101") || a.Code.StartsWith("1102") || a.Code.StartsWith("1103")))
                .Where(a => a.Code != "1106" && a.Code != "110104" && a.Code != "110106" && !a.Code.StartsWith("1105") && !a.Code.StartsWith("1107") && !a.NameAr.Contains("مخزون") && !a.NameAr.Contains("جرد") && !a.NameAr.Contains("عجز") && !a.NameAr.Contains("زيادة") && (!a.NameAr.Contains("تقفيل") || a.Code == "110105"))
                .ToListAsync();

            decimal totalCashSafes = 0;
            decimal totalBanksAndWallets = 0;
            foreach (var acc in balanceAccounts)
            {
                var lq = _db.JournalLines.AsNoTracking().Where(l => l.AccountId == acc.Id);
                decimal cumulative = await lq.Where(l => l.JournalEntry.EntryDate <= dayEnd).SumAsync(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0m;
                cumulative += acc.OpeningBalance;

                if (acc.Code.StartsWith("1102") || acc.Code.StartsWith("1103"))
                    totalBanksAndWallets += cumulative;
                else
                    totalCashSafes += cumulative;
            }
            decimal totalLiquidity = totalCashSafes + totalBanksAndWallets;

            // Debts
            var customerAccountIds = await _db.Accounts.AsNoTracking().Where(a => a.Code.StartsWith("1107") && a.IsLeaf).Select(a => a.Id).ToListAsync();
            var customerDebt = await _db.JournalLines.AsNoTracking().Where(l => customerAccountIds.Contains(l.AccountId)).SumAsync(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0m;

            var supplierAccountIds = await _db.Accounts.AsNoTracking().Where(a => a.Code.StartsWith("2101") && a.IsLeaf).Select(a => a.Id).ToListAsync();
            var supplierDebt = await _db.JournalLines.AsNoTracking().Where(l => supplierAccountIds.Contains(l.AccountId)).SumAsync(l => (decimal?)l.Credit - (decimal?)l.Debit) ?? 0m;

            var expenseAccounts = await _db.Accounts.AsNoTracking().Where(a => a.Type == AccountType.Expense).Select(a => a.Id).ToListAsync();
            var topExpenses = await _db.JournalLines.AsNoTracking()
                .Where(l => expenseAccounts.Contains(l.AccountId) && l.JournalEntry.EntryDate >= dayStart && l.JournalEntry.EntryDate <= dayEnd)
                .GroupBy(l => l.Account.NameAr)
                .Select(g => new { Name = g.Key, Amount = g.Sum(l => l.Debit - l.Credit) })
                .Where(x => x.Amount > 0)
                .OrderByDescending(x => x.Amount)
                .Take(3)
                .ToListAsync();

            var partnersMeta = new
            {
                botType = "BotPartnersReport",
                reportDate = dateDisplay,
                dayName = dayNameAr,
                totalNetSales,
                posNet,
                webNet,
                totalReturns,
                dailyCollections,
                dailyOutflows,
                dailyNetCashFlow,
                totalCashSafes,
                totalBanksAndWallets,
                totalLiquidity,
                customerDebt,
                supplierDebt,
                topExpenses = topExpenses.Select(e => new { e.Name, e.Amount }).ToList()
            };

            string topExpText = topExpenses.Any() 
                ? "\n📌 أهم بنود المصروفات:\n" + string.Join("\n", topExpenses.Select(e => $"  • {e.Name}: {e.Amount:N0} ج.م"))
                : "";

            string partnersText = 
                $"🤝 تقرير الشركاء المالي والتشغيلي\n" +
                $"📅 ليوم: {dayNameAr} ({dateDisplay})\n" +
                $"━━━━━━━━━━━━━━━━━━━━━\n" +
                $"📊 المبيعات اليومية:\n" +
                $"• إجمالي صافي المبيعات: {totalNetSales:N0} ج.م\n" +
                $"  - مبيعات الفروع والكاشير (POS): {posNet:N0} ج.م\n" +
                $"  - مبيعات المتجر الأونلاين (Website): {webNet:N0} ج.م\n" +
                (totalReturns > 0 ? $"  - المرتجعات المخصومة: {totalReturns:N0} ج.م\n" : "") +
                $"━━━━━━━━━━━━━━━━━━━━━\n" +
                $"💵 حركة النقدية اليومية:\n" +
                $"• المقبوضات والتحصيلات: {dailyCollections:N0} ج.م\n" +
                $"• المدفوعات والمصروفات: {dailyOutflows:N0} ج.م\n" +
                $"• صافي التدفق اليومي: {(dailyNetCashFlow >= 0 ? "+" : "")}{dailyNetCashFlow:N0} ج.م {(dailyNetCashFlow >= 0 ? "🟢" : "🔴")}\n" +
                $"━━━━━━━━━━━━━━━━━━━━━\n" +
                $"🏦 أرصدة السيولة المتاحة:\n" +
                $"• الخزائن النقدية: {totalCashSafes:N0} ج.م\n" +
                $"• البنوك والمحافظ الإلكترونية: {totalBanksAndWallets:N0} ج.م\n" +
                $"• إجمالي السيولة الحالية: {totalLiquidity:N0} ج.م 💰\n" +
                $"━━━━━━━━━━━━━━━━━━━━━\n" +
                $"⚖️ المديونيات والمستحقات:\n" +
                $"• مديونيات العملاء (لنا): {customerDebt:N0} ج.م\n" +
                $"• مستحقات الموردين (علينا): {supplierDebt:N0} ج.م" +
                topExpText;

            await SaveAndBroadcastBotMessageAsync(channel.Id, partnersText, JsonSerializer.Serialize(partnersMeta, JsonOpts), "BotPartnersReport", null, $"PARTNERS-{dateDisplay}");

            // Short pause between messages for clean sequencing
            await Task.Delay(1000);

            // ════════════════════════════════════════════════════════════
            // 2. تقرير محاسبة وأرباح المتجر الإلكتروني (STORE ACCOUNTING)
            // ════════════════════════════════════════════════════════════
            var storeOrders = await _db.Orders
                .AsNoTracking()
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.ShippingCompany)
                .Where(o => o.Source == OrderSource.Website && o.CreatedAt >= dayStart && o.CreatedAt <= dayEnd)
                .ToListAsync();

            int storeTotalOrders = storeOrders.Count;
            int storeDelivered = storeOrders.Count(o => o.Status == OrderStatus.Delivered);
            int storeInShipping = storeOrders.Count(o => o.Status == OrderStatus.OutForDelivery || o.Status == OrderStatus.ReturnInShipping);
            int storeReturned = storeOrders.Count(o => o.Status == OrderStatus.Returned);
            int storePending = storeOrders.Count(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Processing);

            var deliveredOrders = storeOrders.Where(o => o.Status == OrderStatus.Delivered).ToList();
            decimal deliveredSalesVal = deliveredOrders.Sum(o => o.TotalAmount);
            decimal storeDeliveryRevenue = storeOrders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.DeliveryFee);

            decimal storeCogs = deliveredOrders.Sum(o => o.Items.Sum(i => (i.Product?.CostPrice ?? 0) * (i.Quantity > 0 ? i.Quantity : 1)));
            decimal deliveredGrossItems = deliveredOrders.Sum(o => (o.SubTotal > 0 ? o.SubTotal : o.Items.Sum(i => i.TotalPrice)) - (o.DiscountAmount + o.TemporalDiscount));
            decimal storeGrossProfit = deliveredGrossItems - storeCogs;

            decimal storeCourierCost = deliveredOrders.Sum(o => o.ActualDeliveryCost);
            decimal storeReturnShippingLoss = storeOrders.Where(o => o.Status == OrderStatus.Returned || o.Status == OrderStatus.ReturnInShipping).Sum(o => o.ActualDeliveryCost);

            var delivAcc = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "5220706" || a.NameAr.Contains("مصروف خدمة التوصيل"));
            var postedDeliveryExpense = await _db.JournalLines.AsNoTracking()
                .Where(l => (l.AccountId == (delivAcc != null ? delivAcc.Id : 0) || l.Account.Code == "5220706" || l.Account.NameAr.Contains("مصروف خدمة التوصيل"))
                         && l.JournalEntry.EntryDate >= dayStart && l.JournalEntry.EntryDate <= dayEnd && l.JournalEntry.Status == JournalEntryStatus.Posted)
                .SumAsync(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0m;

            if (postedDeliveryExpense > 0) storeCourierCost = postedDeliveryExpense;

            var eComBranch = await _db.Branches.FirstOrDefaultAsync(b => b.Name.Contains("متجر") || b.Name.Contains("الموقع") || b.Name.Contains("أونلاين") || b.Name.Contains("Online") || b.Name.Contains("الويب") || b.Name.Contains("Website"));
            int? eComBranchId = eComBranch?.Id;

            var storeGeneralExpenses = await _db.JournalLines.AsNoTracking()
                .Where(l => l.JournalEntry.EntryDate >= dayStart && l.JournalEntry.EntryDate <= dayEnd
                         && l.JournalEntry.Status == JournalEntryStatus.Posted
                         && l.JournalEntry.Type != JournalEntryType.SalesInvoice
                         && l.JournalEntry.Type != JournalEntryType.SalesReturn
                         && l.JournalEntry.OrderId == null
                         && l.OrderId == null
                         && (l.CostCenter == OrderSource.Website || (eComBranchId.HasValue && l.BranchId == eComBranchId.Value))
                         && (l.Account.Type == AccountType.Expense || l.Account.Code.StartsWith("5"))
                         && l.Account.Code != "51101" && l.Account.Code != "5220706" && !l.Account.NameAr.Contains("مصروف خدمة التوصيل")
                         && l.Debit > 0)
                .SumAsync(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0m;

            decimal netOperatingProfit = (storeGrossProfit + storeDeliveryRevenue) - (storeCourierCost + storeReturnShippingLoss + storeGeneralExpenses);
            decimal profitMarginPct = deliveredSalesVal > 0 ? Math.Round((netOperatingProfit / deliveredSalesVal) * 100m, 1) : 0m;

            var courierGroups = storeOrders
                .GroupBy(o => o.ShippingCompany?.NameAr ?? (!string.IsNullOrEmpty(o.ShippingCarrierName) ? o.ShippingCarrierName : (o.ShippingType == "Pickup" ? "استلام فرع" : "غير محدد")))
                .Select(g => new
                {
                    Carrier = g.Key,
                    Count = g.Count(),
                    Delivered = g.Count(o => o.Status == OrderStatus.Delivered),
                    Returned = g.Count(o => o.Status == OrderStatus.Returned)
                })
                .OrderByDescending(x => x.Count)
                .Take(3)
                .ToList();

            var storeMeta = new
            {
                botType = "BotStoreReport",
                reportDate = dateDisplay,
                dayName = dayNameAr,
                storeTotalOrders,
                storeDelivered,
                storeInShipping,
                storeReturned,
                storePending,
                deliveredSalesVal,
                storeCogs,
                storeGrossProfit,
                storeDeliveryRevenue,
                storeCourierCost,
                storeReturnShippingLoss,
                storeGeneralExpenses,
                netOperatingProfit,
                profitMarginPct,
                couriers = courierGroups
            };

            string couriersText = courierGroups.Any()
                ? "\n🚚 توزيع شركات الشحن:\n" + string.Join("\n", courierGroups.Select(c => $"  • {c.Carrier}: {c.Count} طلب (تم تسليم {c.Delivered} | مرتجع {c.Returned})"))
                : "";

            string storeText = 
                $"🛒 تقرير محاسبة وأرباح المتجر الإلكتروني\n" +
                $"📅 ليوم: {dayNameAr} ({dateDisplay})\n" +
                $"━━━━━━━━━━━━━━━━━━━━━\n" +
                $"📦 نشاط طلبات المتجر:\n" +
                $"• إجمالي الطلبات الواردة: {storeTotalOrders} طلب\n" +
                $"  - مسلم للعملاء: {storeDelivered} طلب ({deliveredSalesVal:N0} ج.م) ✅\n" +
                $"  - جاري الشحن والتوصيل: {storeInShipping} طلب 🚚\n" +
                $"  - قيد المعالجة والتأكيد: {storePending} طلب ⏳\n" +
                $"  - مرتجعات: {storeReturned} طلب 🔄\n" +
                $"━━━━━━━━━━━━━━━━━━━━━\n" +
                $"💰 الربحية وتكلفة البضاعة:\n" +
                $"• قيمة المبيعات المسلمة: {deliveredSalesVal:N0} ج.م\n" +
                $"• تكلفة البضاعة المباعة (COGS): {storeCogs:N0} ج.م\n" +
                $"• مجمل ربح المنتجات: {storeGrossProfit:N0} ج.م\n" +
                $"• إيرادات الشحن المحصلة: {storeDeliveryRevenue:N0} ج.م\n" +
                $"• مصاريف شركات الشحن والمرتجعات: {(storeCourierCost + storeReturnShippingLoss):N0} ج.م\n" +
                (storeGeneralExpenses > 0 ? $"• مصاريف تشغيل وإعلانات المتجر: {storeGeneralExpenses:N0} ج.م\n" : "") +
                $"━━━━━━━━━━━━━━━━━━━━━\n" +
                $"📈 صافي الربح التشغيلي لليوم:\n" +
                $"• صافي الربح: {(netOperatingProfit >= 0 ? "+" : "")}{netOperatingProfit:N0} ج.م {(netOperatingProfit >= 0 ? "🟢" : "🔴")}\n" +
                $"• نسبة هامش الربح: {profitMarginPct}%" +
                couriersText;

            await SaveAndBroadcastBotMessageAsync(channel.Id, storeText, JsonSerializer.Serialize(storeMeta, JsonOpts), "BotStoreReport", null, $"STORE-{dateDisplay}");
            _logger.LogInformation("Posted daily partners and store report for {TargetDate}", dateDisplay);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post daily partners and store report");
        }
    }

    private (List<string> phoneHashes, List<string> emailHashes) GetSearchHashes(string input)
    {
        var pHashes = new List<string>();
        var eHashes = new List<string>();
        if (string.IsNullOrWhiteSpace(input)) return (pHashes, eHashes);

        var s = input.Trim();
        var helper = _encryptionHelper ?? Customer.EncryptionHelper;
        if (helper == null) return (pHashes, eHashes);

        var variants = new List<string> { s, s.ToLowerInvariant() };
        var digits = new string(s.Where(char.IsDigit).ToArray());

        if (!string.IsNullOrEmpty(digits))
        {
            string local = digits.StartsWith("20") && digits.Length == 12 ? digits.Substring(2) : digits;
            if (local.Length == 10 && !local.StartsWith("0")) local = "0" + local;
            string intl = local.StartsWith("0") ? "2" + local : "20" + local;
            string bare = local.StartsWith("0") ? local.Substring(1) : local;

            variants.Add(digits);
            variants.Add(local);
            variants.Add(intl);
            variants.Add(bare);
        }

        foreach (var v in variants.Distinct())
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            var ph = helper.ComputeSearchHash(v);
            if (!string.IsNullOrEmpty(ph) && !pHashes.Contains(ph)) pHashes.Add(ph);

            var eh = helper.ComputeSearchHash($"{v}@sportive.com");
            if (!string.IsNullOrEmpty(eh) && !eHashes.Contains(eh)) eHashes.Add(eh);

            var directEh = helper.ComputeSearchHash(v);
            if (!string.IsNullOrEmpty(directEh) && !eHashes.Contains(directEh)) eHashes.Add(directEh);
        }

        return (pHashes, eHashes);
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
            [ChannelKeyPos]       = new[] { "order", "طلب", "invoice", "فاتورة", "customer", "عميل", "sales", "مبيعات" },
            [ChannelKeyInventory] = new[] { "stock", "مخزون" },
            [ChannelKeyTreasury]  = new[] { "sales", "مبيعات" },
            [ChannelKeyPartners]  = new[] { "report", "تقرير", "partners", "شركاء", "store", "متجر", "sales", "مبيعات" }
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
                        ? "يرجى كتابة رقم الفاتورة أو آخر 4 أرقام بعد الأمر، مثلاً:\n/فاتورة 0051 أو /طلب POS-2610-0051"
                        : "يرجى كتابة رقم الطلب أو آخر 4 أرقام بعد الأمر، مثلاً:\n/طلب 0015 أو /order SPT-2610-0015";
                }
                else
                {
                    var raw = cleanQuery.Trim().TrimStart('#').Trim();
                    int? parsedSeq = int.TryParse(raw, out var sNum) ? sNum : null;
                    string? formatted4 = parsedSeq.HasValue ? parsedSeq.Value.ToString("D4") : null;
                    string dashRaw = "-" + raw;
                    string? dashD4 = formatted4 != null ? "-" + formatted4 : null;

                    var ordersQuery = _db.Orders
                        .Include(o => o.Customer)
                        .Include(o => o.Items)
                        .AsNoTracking();

                    List<Order> matchedOrders;
                    if (parsedSeq.HasValue)
                    {
                        matchedOrders = await ordersQuery
                            .Where(o => o.OrderNumber == raw ||
                                        o.OrderNumber.EndsWith(dashRaw) ||
                                        o.OrderNumber.EndsWith(dashD4!) ||
                                        o.OrderNumber.EndsWith(raw) ||
                                        o.Id == parsedSeq.Value ||
                                        o.OrderNumber.Contains(raw))
                            .OrderByDescending(o => o.CreatedAt)
                            .Take(10)
                            .ToListAsync();
                    }
                    else
                    {
                        matchedOrders = await ordersQuery
                            .Where(o => o.OrderNumber == raw ||
                                        o.OrderNumber.EndsWith(dashRaw) ||
                                        o.OrderNumber.EndsWith(raw) ||
                                        o.OrderNumber.Contains(raw))
                            .OrderByDescending(o => o.CreatedAt)
                            .Take(10)
                            .ToListAsync();
                    }

                    // Fallback: If no order matched and input looks like a phone number, search by customer phone hash!
                    if (!matchedOrders.Any() && raw.Length >= 9 && raw.All(char.IsDigit))
                    {
                        var (phoneHashes, _) = GetSearchHashes(raw);
                        if (phoneHashes.Any())
                        {
                            var custIds = await _db.Customers
                                .AsNoTracking()
                                .Where(c => c.PhoneHash != null && phoneHashes.Contains(c.PhoneHash))
                                .Select(c => c.Id)
                                .ToListAsync();

                            if (custIds.Any())
                            {
                                matchedOrders = await ordersQuery
                                    .Where(o => custIds.Contains(o.CustomerId))
                                    .OrderByDescending(o => o.CreatedAt)
                                    .Take(5)
                                    .ToListAsync();
                            }
                        }
                    }

                    Order? order = null;
                    if (matchedOrders.Any())
                    {
                        // 1. Exact order number match has highest priority
                        var exact = matchedOrders.FirstOrDefault(o => o.OrderNumber.Equals(raw, StringComparison.OrdinalIgnoreCase));
                        if (exact != null)
                        {
                            order = exact;
                        }
                        // 2. Channel context priority
                        else if (channel.DirectKey == ChannelKeyPos)
                        {
                            order = matchedOrders.FirstOrDefault(o => o.Source == OrderSource.POS) ?? matchedOrders.FirstOrDefault();
                        }
                        else if (channel.DirectKey == ChannelKeyOrders)
                        {
                            order = matchedOrders.FirstOrDefault(o => o.Source != OrderSource.POS) ?? matchedOrders.FirstOrDefault();
                        }
                        else
                        {
                            order = matchedOrders.FirstOrDefault();
                        }
                    }

                    if (order == null)
                    {
                        responseText = channel.DirectKey == ChannelKeyPos
                            ? $"🔍 لم يتم العثور على فاتورة كاشير مطابقة لـ: \"{cleanQuery}\""
                            : $"🔍 لم يتم العثور على طلب مطابق لـ: \"{cleanQuery}\"";
                    }
                    else
                    {
                        linkedEntityType = "BotOrderQuery";
                        linkedEntityId = order.Id;
                        linkedEntityRef = order.OrderNumber;

                        bool isPosOrder = order.Source == OrderSource.POS;

                        var customerName = order.Customer?.FullName ?? (isPosOrder ? "عميل كاشير نقدي" : "عميل");
                        string customerPhone = "";
                        if (order.Customer != null)
                        {
                            try
                            {
                                customerPhone = !string.IsNullOrEmpty(order.Customer.Phone)
                                    ? order.Customer.Phone
                                    : (!string.IsNullOrEmpty(order.Customer.PhoneEncrypted) ? (_encryptionHelper?.Decrypt(order.Customer.PhoneEncrypted) ?? "") : "");
                            }
                            catch { customerPhone = ""; }
                        }

                        responseMeta = new
                        {
                            botType = "BotOrderQuery",
                            orderId = order.Id,
                            orderNumber = order.OrderNumber,
                            customerName,
                            phone = customerPhone,
                            total = order.TotalAmount,
                            status = order.Status.ToString(),
                            payment = order.PaymentMethod.ToString(),
                            itemsCount = order.Items.Count,
                            date = order.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                        };

                        string typeTitle = isPosOrder ? "🧾 فاتورة كاشير" : "🛒 طلب متجر أونلاين";
                        string phoneSuffix = string.IsNullOrEmpty(customerPhone) ? "" : $" ({customerPhone})";

                        var orderDetails = $"📋 تفاصيل {typeTitle} #{order.OrderNumber}:\n" +
                                           $"العميل: {customerName}{phoneSuffix}\n" +
                                           $"الحالة: {order.Status} | الإجمالي: {order.TotalAmount:N0} ج.م\n" +
                                           $"طريقة الدفع: {order.PaymentMethod} | عدد الأصناف: {order.Items.Count} قطعة";

                        // If multiple orders matched (e.g. from previous months or another source), inform user
                        var otherMatches = matchedOrders.Where(o => o.Id != order.Id).Take(2).ToList();
                        if (otherMatches.Any())
                        {
                            var otherRefs = string.Join(" ، ", otherMatches.Select(o => "#" + o.OrderNumber));
                            orderDetails += $"\n💡 (أحدث طلب مطابق. يوجد طلبات أخرى: {otherRefs})";
                        }

                        responseText = orderDetails;
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
                    responseText = "يرجى إدخال رقم هاتف العميل أو اسمه، مثلاً:\n/عميل 01012345678 أو /عميل أحمد";
                }
                else
                {
                    var s = cleanQuery.Trim();
                    var sLower = s.ToLowerInvariant();
                    int? parsedCid = int.TryParse(s, out var cid) ? cid : null;

                    var (phoneHashes, emailHashes) = GetSearchHashes(s);

                    var custQuery = _db.Customers
                        .Include(c => c.Orders)
                        .AsNoTracking();

                    Customer? cust = null;
                    if (phoneHashes.Any() || emailHashes.Any())
                    {
                        cust = await custQuery
                            .Where(c => (c.PhoneHash != null && phoneHashes.Contains(c.PhoneHash)) ||
                                        (c.EmailHash != null && emailHashes.Contains(c.EmailHash)) ||
                                        (parsedCid.HasValue && c.Id == parsedCid.Value) ||
                                        c.FullName.ToLower().Contains(sLower))
                            .OrderByDescending(c => c.Orders.Count)
                            .ThenByDescending(c => c.CreatedAt)
                            .FirstOrDefaultAsync();
                    }
                    else
                    {
                        cust = await custQuery
                            .Where(c => (parsedCid.HasValue && c.Id == parsedCid.Value) ||
                                        c.FullName.ToLower().Contains(sLower))
                            .OrderByDescending(c => c.Orders.Count)
                            .ThenByDescending(c => c.CreatedAt)
                            .FirstOrDefaultAsync();
                    }

                    if (cust == null)
                    {
                        responseText = $"🔍 لم يتم العثور على عميل مسجل ببيانات: \"{cleanQuery}\"";
                    }
                    else
                    {
                        linkedEntityType = "BotCustomerQuery";
                        linkedEntityId = cust.Id;
                        linkedEntityRef = cust.FullName;

                        string phoneDisplay = "";
                        try
                        {
                            phoneDisplay = !string.IsNullOrEmpty(cust.Phone) 
                                ? cust.Phone 
                                : (!string.IsNullOrEmpty(cust.PhoneEncrypted) ? (_encryptionHelper?.Decrypt(cust.PhoneEncrypted) ?? "") : "");
                        }
                        catch { phoneDisplay = ""; }

                        if (string.IsNullOrEmpty(phoneDisplay)) phoneDisplay = "غير مسجل";

                        var totalOrders = cust.Orders.Count;
                        var completedOrders = cust.Orders.Count(o => o.Status == OrderStatus.Delivered);
                        var totalSpent = cust.Orders.Where(o => o.Status == OrderStatus.Delivered).Sum(o => o.TotalAmount);
                        var latestOrder = cust.Orders.OrderByDescending(o => o.CreatedAt).FirstOrDefault();

                        responseMeta = new
                        {
                            botType = "BotCustomerQuery",
                            customerId = cust.Id,
                            name = cust.FullName,
                            phone = phoneDisplay,
                            totalOrders,
                            completedOrders,
                            totalSpent,
                            latestOrder = latestOrder?.OrderNumber
                        };

                        var responseLines = new List<string>
                        {
                            $"• العميل: {cust.FullName}",
                            $"• الهاتف: {phoneDisplay}",
                            $"• إجمالي الطلبات: {totalOrders} طلب | المسلم بنجاح: {completedOrders}",
                            $"• إجمالي المشتريات: {totalSpent:N0} ج.م"
                        };

                        if (latestOrder != null)
                        {
                            responseLines.Add($"• أحدث طلب: #{latestOrder.OrderNumber} ({latestOrder.Status}) بتاريخ {latestOrder.CreatedAt:yyyy-MM-dd}");
                        }

                        responseText = string.Join("\n", responseLines);
                    }
                }
                break;

            case "report":
            case "تقرير":
            case "partners":
            case "شركاء":
            case "store":
            case "متجر":
                DateTime? customDate = null;
                if (!string.IsNullOrWhiteSpace(cleanQuery) && DateTime.TryParse(cleanQuery, out var parsedD))
                {
                    customDate = parsedD;
                }
                await PostDailyPartnersAndStoreReportAsync(customDate);
                responseText = "✅ تم استخراج وإرسال تقرير اليوم السابق للشركاء والمتجر في المجموعة بنجاح.";
                break;

            default:
                responseText = channel.DirectKey switch
                {
                    ChannelKeyOrders =>
                        "🤖 أوامر رادار طلبات المتجر الأونلاين:\n" +
                        "• /طلب [رقم الطلب أو آخره]: فحص حالة وبيانات أوردر المتجر\n" +
                        "• /عميل [هاتف/اسم]: سجل ومشتريات العميل\n" +
                        "• /مبيعات: ملخص مبيعات المتجر الأونلاين فقط اليوم",
                    ChannelKeyPos =>
                        "🤖 أوامر رادار فواتير الكاشير:\n" +
                        "• /طلب أو /فاتورة [رقم الفاتورة أو آخرها]: فحص بيانات فاتورة الكاشير\n" +
                        "• /عميل [هاتف/اسم]: سجل وبيانات العميل\n" +
                        "• /مبيعات: ملخص مبيعات وإيرادات الكاشير فقط اليوم",
                    ChannelKeyInventory =>
                        "🤖 أوامر طوارئ المخزون المتاحة:\n" +
                        "• /مخزون [اسم/كود]: رصيد الصنف بكل المقاسات والألوان",
                    ChannelKeyTreasury =>
                        "🤖 أوامر الخزينة والرقابة المالية:\n" +
                        "• /مبيعات: ملخص مبيعات الشركة الشامل (المتجر + الكاشير)",
                    ChannelKeyPartners =>
                        "🤝 أوامر روبوت الشركاء:\n" +
                        "• /تقرير أو /شركاء: توليد وإرسال التقرير الشامل لليوم السابق (أو كتابة تاريخ محدد مثل: /تقرير 2026-10-04)\n" +
                        "• /متجر: ملخص تقرير أرباح ومحاسبة المتجر الإلكتروني\n" +
                        "• /مبيعات: ملخص مبيعات اليوم اللحظية",
                    _ => "🤖 لا توجد أوامر متاحة في هذه المجموعة"
                };
                break;
        }

        string metaJson = responseMeta != null ? JsonSerializer.Serialize(responseMeta, JsonOpts) : "{}";
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
