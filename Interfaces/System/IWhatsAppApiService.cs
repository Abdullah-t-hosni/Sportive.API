using System.Threading.Tasks;

namespace Sportive.API.Interfaces;

public interface IWhatsAppApiService
{
    Task<bool> SendOtpAsync(string phoneNumber, string otpCode);
    Task<bool> SendWhatsAppMessageAsync(string phoneNumber, string messageText, bool isPos = false);
}
