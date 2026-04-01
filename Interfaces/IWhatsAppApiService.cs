namespace Sportive.API.Interfaces;

public interface IWhatsAppApiService
{
    Task<bool> SendOtpAsync(string phoneNumber, string otpCode);
}
