using System.Text;
using System.Linq;
using Sportive.API.Models;
using Sportive.API.Interfaces;
using Sportive.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Sportive.API.Services;

/// <summary>
/// WaMeService — Generates wa.me links ready for copying or opening.
/// No API Key or subscription required.
/// </summary>
public class WaMeService : IWaMeService
{
    private readonly IConfiguration _config;
    private readonly ITranslator _t;
    private readonly AppDbContext _db;

    public WaMeService(IConfiguration config, ITranslator t, AppDbContext db)
    {
        _config = config;
        _t = t;
        _db = db;
    }

    private string StorePhone => _config["Store:WhatsApp"] ?? _config["Store:Phone"] ?? "";
    private string StoreUrl   => _config["Store:BaseUrl"]  ?? "https://sportive-sportwear.com";

    private string FormatTemplate(string template, Order order, string? tracking = null)
    {
        if (string.IsNullOrWhiteSpace(template)) return "";

        var customerName = order.Customer?.FullName ?? _t.Get("WhatsApp.DefaultCustomer");
        var customerFirstName = CustomerFirstName(order);
        
        var itemsSummary = new StringBuilder();
        foreach (var item in order.Items)
        {
            itemsSummary.AppendLine($"• {item.ProductNameAr} ({item.Size} - {item.Color}) x{item.Quantity}");
        }

        var discountPart = order.DiscountAmount > 0
            ? $"🎁 {_t.Get("WhatsApp.Discount")}: *{order.DiscountAmount:N2}*\n"
            : "";

        var address = _config["Store:Address"] ?? "فرع Sportive الرئيسي";
        var trackingPart = string.IsNullOrEmpty(tracking) ? "" : $"📍 {_t.Get("WhatsApp.TrackingCode")}: *{tracking}*\n";
        var storeBrandName = _db.StoreInfo.AsNoTracking().FirstOrDefault(s => s.StoreConfigId == 1)?.StoreBrandName ?? "Sportive";

        return template
            .Replace("{customerName}", customerName)
            .Replace("{customerFirstName}", customerFirstName)
            .Replace("{orderNumber}", order.OrderNumber)
            .Replace("{storeName}", storeBrandName)
            .Replace("{itemsList}", itemsSummary.ToString().TrimEnd())
            .Replace("{totalAmount}", $"{order.TotalAmount:N2}")
            .Replace("{discountPart}", discountPart)
            .Replace("{paymentMethod}", PaymentMethodLabel(order.PaymentMethod))
            .Replace("{fulfillmentType}", FulfillmentLabel(order.FulfillmentType))
            .Replace("{storeUrl}", StoreUrl)
            .Replace("{storePhone}", StorePhone)
            .Replace("{storeAddress}", address)
            .Replace("{trackingPart}", trackingPart)
            .Replace("{trackingCode}", tracking ?? "");
    }

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
        var settings = _db.StoreInfo.AsNoTracking().FirstOrDefault(s => s.StoreConfigId == 1);
        string message;
        if (settings != null && !string.IsNullOrWhiteSpace(settings.WhatsAppOrderTemplate))
        {
            message = FormatTemplate(settings.WhatsAppOrderTemplate, order);
        }
        else
        {
            var itemsSummary = new StringBuilder();
            foreach (var item in order.Items)
            {
                itemsSummary.AppendLine($"• {item.ProductNameAr} ({item.Size} - {item.Color}) x{item.Quantity}");
            }

            var discountPart = order.DiscountAmount > 0
                ? $"🎁 {_t.Get("WhatsApp.Discount")}: *{order.DiscountAmount:N2}*\n"
                : "";

            message = string.Format(
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
        }

        return CreateLink(order.Customer?.Phone ?? "", message);
    }

    public WaMeResult ShippingUpdate(Order order, string? tracking = null)
    {
        var settings = _db.StoreInfo.AsNoTracking().FirstOrDefault(s => s.StoreConfigId == 1);
        string message;
        if (settings != null && !string.IsNullOrWhiteSpace(settings.WhatsAppShippingTemplate))
        {
            message = FormatTemplate(settings.WhatsAppShippingTemplate, order, tracking);
        }
        else
        {
            var trackingPart = string.IsNullOrEmpty(tracking) ? "" : $"📍 {_t.Get("WhatsApp.TrackingCode")}: *{tracking}*\n";

            message = string.Format(
                _t.Get("WhatsApp.ShippingUpdate"),
                CustomerFirstName(order),
                order.OrderNumber,
                trackingPart,
                StorePhone
            );
        }

        return CreateLink(order.Customer?.Phone ?? "", message);
    }

    public WaMeResult ReturnConfirmation(Order order)
    {
        var settings = _db.StoreInfo.AsNoTracking().FirstOrDefault(s => s.StoreConfigId == 1);
        string message;
        if (settings != null && !string.IsNullOrWhiteSpace(settings.WhatsAppReturnTemplate))
        {
            message = FormatTemplate(settings.WhatsAppReturnTemplate, order);
        }
        else
        {
            message = string.Format(
                _t.Get("WhatsApp.ReturnConfirm"),
                CustomerFirstName(order),
                order.OrderNumber,
                order.TotalAmount
            );
        }

        return CreateLink(order.Customer?.Phone ?? "", message);
    }

    public WaMeResult OrderReady(Order order)
    {
        var settings = _db.StoreInfo.AsNoTracking().FirstOrDefault(s => s.StoreConfigId == 1);
        string message;
        if (settings != null && !string.IsNullOrWhiteSpace(settings.WhatsAppProcessingTemplate))
        {
            message = FormatTemplate(settings.WhatsAppProcessingTemplate, order);
        }
        else
        {
            var address = _config["Store:Address"] ?? "فرع Sportive الرئيسي";

            message = string.Format(
                _t.Get("WhatsApp.ReadyForPickup"),
                CustomerFirstName(order),
                order.OrderNumber,
                address,
                StorePhone
            );
        }

        return CreateLink(order.Customer?.Phone ?? "", message);
    }

    public WaMeResult PaymentReminder(Order order)
    {
        var settings = _db.StoreInfo.AsNoTracking().FirstOrDefault(s => s.StoreConfigId == 1);
        string message;
        if (settings != null && !string.IsNullOrWhiteSpace(settings.WhatsAppPaymentReminderTemplate))
        {
            message = FormatTemplate(settings.WhatsAppPaymentReminderTemplate, order);
        }
        else
        {
            message = string.Format(
                _t.Get("WhatsApp.PaymentReminder"),
                CustomerFirstName(order),
                order.OrderNumber,
                order.TotalAmount,
                StorePhone
            );
        }

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
        PaymentMethod.Cash            => _t.Get("WhatsApp.Payment.Cash"),
        PaymentMethod.CreditCard      => _t.Get("WhatsApp.Payment.CreditCard"),
        PaymentMethod.Vodafone        => _t.Get("WhatsApp.Payment.Vodafone"),
        PaymentMethod.InstaPay        => _t.Get("WhatsApp.Payment.InstaPay"),
        PaymentMethod.CustomerBalance => _t.Get("WhatsApp.Payment.CustomerBalance"),
        _                             => method.ToString()
    };

    private string FulfillmentLabel(FulfillmentType type) => type switch
    {
        FulfillmentType.Delivery => _t.Get("WhatsApp.Fulfillment.Delivery"),
        FulfillmentType.Pickup   => _t.Get("WhatsApp.Fulfillment.Pickup"),
        _                        => type.ToString()
    };
}
