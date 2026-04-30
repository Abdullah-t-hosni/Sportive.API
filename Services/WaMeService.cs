using System.Text;
using Sportive.API.Models;
using Sportive.API.Interfaces;

namespace Sportive.API.Services;

/// <summary>
/// WaMeService — Generates wa.me links ready for copying or opening.
/// No API Key or subscription required.
/// </summary>
public class WaMeService : IWaMeService
{
    private readonly IConfiguration _config;
    private readonly ITranslator _t;

    public WaMeService(IConfiguration config, ITranslator t)
    {
        _config = config;
        _t = t;
    }

    private string StorePhone => _config["Store:WhatsApp"] ?? _config["Store:Phone"] ?? "";
    private string StoreUrl   => _config["Store:BaseUrl"]  ?? "https://sportive-sportwear.com";

    private WaMeResult CreateLink(string phone, string message)
    {
        if (string.IsNullOrEmpty(phone)) return new WaMeResult("", "", "");

        // Clean phone: remove +, spaces, dashes
        var cleanPhone = new string(phone.Where(char.IsDigit).ToArray());
        if (!cleanPhone.StartsWith("20") && cleanPhone.Length == 11) cleanPhone = "20" + cleanPhone;

        var encodedMsg = Uri.EscapeDataString(message);
        var url = $"https://wa.me/{cleanPhone}?text={encodedMsg}";

        var lines = message.Split('\n');
        var shortMsg = lines.Length > 2 ? string.Join('\n', lines.Take(2)) + "..." : message;

        return new WaMeResult(url, message, shortMsg);
    }

    public WaMeResult OrderConfirmation(Order order)
    {
        var itemsSummary = new StringBuilder();
        foreach (var item in order.Items)
        {
            itemsSummary.AppendLine($"• {item.ProductNameAr} ({item.Size} - {item.Color}) x{item.Quantity}");
        }

        var discountPart = order.DiscountAmount > 0
            ? $"🎁 {_t.Get("WhatsApp.Discount")}: *{order.DiscountAmount:N2}*\n"
            : "";

        var message = string.Format(
            _t.Get("WhatsApp.OrderConfirm"),
            CustomerFirstName(order),
            order.OrderNumber,
            itemsSummary.ToString().TrimEnd(),
            order.TotalAmount,
            discountPart,
            PaymentMethodLabel(order.PaymentMethod),
            FulfillmentLabel(order.FulfillmentType),
            StoreUrl
        );

        return CreateLink(order.Customer?.Phone ?? "", message);
    }

    public WaMeResult ShippingUpdate(Order order, string? tracking = null)
    {
        var trackingPart = string.IsNullOrEmpty(tracking) ? "" : $"📍 {_t.Get("WhatsApp.TrackingCode")}: *{tracking}*\n";

        var message = string.Format(
            _t.Get("WhatsApp.ShippingUpdate"),
            CustomerFirstName(order),
            order.OrderNumber,
            trackingPart,
            StorePhone
        );

        return CreateLink(order.Customer?.Phone ?? "", message);
    }

    public WaMeResult ReturnConfirmation(Order order)
    {
        var message = string.Format(
            _t.Get("WhatsApp.ReturnConfirm"),
            CustomerFirstName(order),
            order.OrderNumber,
            order.TotalAmount
        );

        return CreateLink(order.Customer?.Phone ?? "", message);
    }

    public WaMeResult OrderReady(Order order)
    {
        var address = _config["Store:Address"] ?? "فرع Sportive الرئيسي";

        var message = string.Format(
            _t.Get("WhatsApp.ReadyForPickup"),
            CustomerFirstName(order),
            order.OrderNumber,
            address,
            StorePhone
        );

        return CreateLink(order.Customer?.Phone ?? "", message);
    }

    public WaMeResult PaymentReminder(Order order)
    {
        var message = string.Format(
            _t.Get("WhatsApp.PaymentReminder"),
            CustomerFirstName(order),
            order.OrderNumber,
            order.TotalAmount,
            StorePhone
        );

        return CreateLink(order.Customer?.Phone ?? "", message);
    }

    public WaMeResult CustomMessage(string phone, string message) => CreateLink(phone, message);

    private string CustomerFirstName(Order o)
    {
        var name = o.Customer?.FullName?.Split(' ').FirstOrDefault();
        return !string.IsNullOrEmpty(name) ? name : _t.Get("WhatsApp.DefaultCustomer");
    }

    private string PaymentMethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash       => _t.Get("WhatsApp.Payment.Cash"),
        PaymentMethod.CreditCard => _t.Get("WhatsApp.Payment.CreditCard"),
        PaymentMethod.Vodafone   => _t.Get("WhatsApp.Payment.Vodafone"),
        PaymentMethod.InstaPay   => _t.Get("WhatsApp.Payment.InstaPay"),
        _                        => method.ToString()
    };

    private string FulfillmentLabel(FulfillmentType type) => type switch
    {
        FulfillmentType.Delivery => _t.Get("WhatsApp.Fulfillment.Delivery"),
        FulfillmentType.Pickup   => _t.Get("WhatsApp.Fulfillment.Pickup"),
        _                        => type.ToString()
    };
}
