using Sportive.API.Models;

namespace Sportive.API.Interfaces;

/// <summary>
/// Return type for WaMe link generation
/// </summary>
public record WaMeResult(string Url, string FullMessage, string ShortMessage);

/// <summary>
/// Contract for WhatsApp message link generation via wa.me
/// </summary>
public interface IWaMeService
{
    WaMeResult OrderConfirmation(Order order);
    WaMeResult ShippingUpdate(Order order, string? tracking = null);
    WaMeResult ReturnConfirmation(Order order);
    WaMeResult OrderReady(Order order);
    WaMeResult PaymentReminder(Order order);
    WaMeResult CustomMessage(string phone, string message);
}
