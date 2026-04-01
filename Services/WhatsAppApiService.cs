using System.Text;
using System.Text.Json;
using Sportive.API.Interfaces;

namespace Sportive.API.Services;

public class WhatsAppApiService : IWhatsAppApiService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<WhatsAppApiService> _logger;

    public WhatsAppApiService(HttpClient httpClient, IConfiguration config, ILogger<WhatsAppApiService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<bool> SendOtpAsync(string phoneNumber, string otpCode)
    {
        try
        {
            var phoneNumberId = _config["WhatsApp:PhoneNumberId"];
            var accessToken = _config["WhatsApp:AccessToken"];
            var templateName = _config["WhatsApp:TemplateName"] ?? "auth_otp_template";
            var languageCode = _config["WhatsApp:LanguageCode"] ?? "ar";
            
            if (string.IsNullOrEmpty(phoneNumberId) || string.IsNullOrEmpty(accessToken))
            {
                _logger.LogWarning("WhatsApp API is not configured. Missing PhoneNumberId or AccessToken.");
                return false;
            }

            var url = $"https://graph.facebook.com/v19.0/{phoneNumberId}/messages";
            var formattedPhone = NormalizePhone(phoneNumber);

            var payload = new
            {
                messaging_product = "whatsapp",
                to = formattedPhone,
                type = "template",
                template = new
                {
                    name = templateName,
                    language = new { code = languageCode },
                    components = new[]
                    {
                        new
                        {
                            type = "body",
                            parameters = new[]
                            {
                                new { type = "text", text = otpCode }
                            }
                        },
                        new 
                        {
                            type = "button",
                            sub_type = "url",
                            index = "0",
                            parameters = new[]
                            {
                                new { type = "text", text = otpCode }
                            }
                        }
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorResponse = await response.Content.ReadAsStringAsync();
                _logger.LogError("WhatsApp API Error: {Error}", errorResponse);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OTP via WhatsApp");
            return false;
        }
    }

    private static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("01") && digits.Length == 11) return "20" + digits;
        if (digits.StartsWith("20") && digits.Length == 12) return digits;
        return digits;
    }
}
