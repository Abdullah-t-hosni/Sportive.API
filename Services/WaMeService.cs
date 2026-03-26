using Sportive.API.Models;

namespace Sportive.API.Services;

// ══════════════════════════════════════════════════════
// WaMeService — يولّد روابط wa.me جاهزة للنسخ أو الفتح
// لا يحتاج API Key أو اشتراك
// ══════════════════════════════════════════════════════

public interface IWaMeService
{
    WaMeResult OrderConfirmation(Order order);
    WaMeResult ShippingUpdate(Order order, string? tracking = null);
    WaMeResult ReturnConfirmation(Order order);
    WaMeResult OrderReady(Order order);
    WaMeResult PaymentReminder(Order order);
    WaMeResult CustomMessage(string phone, string message);
}

public record WaMeResult(
    string Phone,
    string Message,
    string Link,         // wa.me/20201xxxxxxxx?text=...
    string ShortMessage  // للعرض في الـ UI (أول سطرين)
);

public class WaMeService : IWaMeService
{
    private readonly IConfiguration _config;
    private string StoreName => _config["Store:Name"] ?? "Sportive";
    private string StorePhone => _config["Store:Phone"] ?? "201000000000";
    private string StoreUrl => _config["Store:Url"] ?? "https://sportive-sportwear.com";

    public WaMeService(IConfiguration config) => _config = config;

    // ── 1. تأكيد الطلب ───────────────────────────────
    public WaMeResult OrderConfirmation(Order order)
    {
        var itemsList = order.Items?.Select(i =>
            $"  • {i.ProductNameAr ?? i.ProductName} × {i.Quantity}").ToList()
            ?? new List<string>();

        var msg = $"""
أهلاً {CustomerName(order)} 👋

✅ *تم تأكيد طلبك بنجاح!*

🔢 رقم الطلب: *{order.OrderNumber}*
📦 الطلب:
{string.Join("\n", itemsList)}

💰 الإجمالي: *{order.TotalAmount:N2} ج.م*
{(order.DiscountAmount > 0 ? $"🎁 الخصم: *{order.DiscountAmount:N2} ج.م*\n" : "")}💳 الدفع: *{PaymentMethodAr(order.PaymentMethod)}*
📍 النوع: *{FulfillmentAr(order.FulfillmentType)}*

سنتواصل معك قريباً لتأكيد التسليم 🙏
_{StoreName}_
""";
        return Build(order.Customer?.Phone, msg);
    }

    // ── 2. تحديث الشحن ───────────────────────────────
    public WaMeResult ShippingUpdate(Order order, string? tracking = null)
    {
        var msg = $"""
مرحباً {CustomerName(order)} 🚚

*طلبك #{order.OrderNumber} في الطريق!*

{(string.IsNullOrEmpty(tracking) ? "" : $"📍 كود التتبع: *{tracking}*\n")}⏱ متوقع الوصول: 2-3 أيام عمل
📞 للاستفسار: {StorePhone}

_{StoreName}_
""";
        return Build(order.Customer?.Phone, msg);
    }

    // ── 3. تأكيد المرتجع ─────────────────────────────
    public WaMeResult ReturnConfirmation(Order order)
    {
        var msg = $"""
مرحباً {CustomerName(order)} 🔄

*تم استلام طلب الإرجاع*

🔢 رقم الطلب: *{order.OrderNumber}*
💰 المبلغ المسترجع: *{order.TotalAmount:N2} ج.م*

سيتم رد المبلغ خلال 3-5 أيام عمل 🙏
نأسف للإزعاج ونتمنى رؤيتك قريباً!

_{StoreName}_
""";
        return Build(order.Customer?.Phone, msg);
    }

    // ── 4. جاهز للاستلام ─────────────────────────────
    public WaMeResult OrderReady(Order order)
    {
        var msg = $"""
أهلاً {CustomerName(order)} 🎉

*طلبك #{order.OrderNumber} جاهز للاستلام!*

📍 العنوان: {_config["Store:Address"] ?? "فرع Sportive الرئيسي"}
🕐 مواعيد العمل: 10 ص — 10 م يومياً
📞 للتواصل: {StorePhone}

انتظرنا! 💪
_{StoreName}_
""";
        return Build(order.Customer?.Phone, msg);
    }

    // ── 5. تذكير بالدفع ──────────────────────────────
    public WaMeResult PaymentReminder(Order order)
    {
        var msg = $"""
مرحباً {CustomerName(order)} 💬

تذكير بخصوص طلبك *#{order.OrderNumber}*

💰 المبلغ المستحق: *{order.TotalAmount:N2} ج.م*
💳 طرق الدفع: كاش / فودافون / انستاباي

للدفع أو الاستفسار تواصل معنا:
📞 {StorePhone}

شكراً لتعاملك معنا 🙏
_{StoreName}_
""";
        return Build(order.Customer?.Phone, msg);
    }

    // ── 6. رسالة مخصصة ───────────────────────────────
    public WaMeResult CustomMessage(string phone, string message)
        => Build(phone, message);

    // ── Helpers ───────────────────────────────────────
    private WaMeResult Build(string? phone, string message)
    {
        var normalized = NormalizePhone(phone ?? "");
        var encoded    = Uri.EscapeDataString(message.Trim());
        var link       = string.IsNullOrEmpty(normalized)
            ? $"https://wa.me/?text={encoded}"
            : $"https://wa.me/{normalized}?text={encoded}";

        var lines  = message.Trim().Split('\n');
        var shortMsg = string.Join(" • ", lines.Take(2)
            .Select(l => l.Replace("*", "").Trim())
            .Where(l => !string.IsNullOrEmpty(l)));

        return new WaMeResult(normalized, message.Trim(), link, shortMsg);
    }

    private static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("01") && digits.Length == 11) return "20" + digits;
        if (digits.StartsWith("20") && digits.Length == 12) return digits;
        return digits;
    }

    private static string CustomerName(Order o) =>
        o.Customer?.FullName?.Split(' ').FirstOrDefault() ?? "عزيزنا";

    private static string PaymentMethodAr(PaymentMethod m) => m switch
    {
        PaymentMethod.Cash     => "كاش عند الاستلام",
        PaymentMethod.Vodafone => "فودافون كاش",
        PaymentMethod.InstaPay => "انستاباي",
        _                      => m.ToString()
    };

    private static string FulfillmentAr(FulfillmentType f) => f switch
    {
        FulfillmentType.Delivery => "توصيل للمنزل",
        FulfillmentType.Pickup   => "استلام من الفرع",
        _                        => f.ToString()
    };
}
