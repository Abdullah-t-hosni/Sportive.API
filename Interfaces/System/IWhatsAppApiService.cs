namespace Sportive.API.Interfaces;

public interface IWhatsAppApiService
{
    Task<bool> SendOtpAsync(string phoneNumber, string otpCode);
    Task<bool> SendWapilotMessageAsync(string phoneNumber, string messageText, string apiKey, string instanceId);
}
