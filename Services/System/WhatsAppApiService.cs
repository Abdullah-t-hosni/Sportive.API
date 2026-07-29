using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sportive.API.Interfaces;

namespace Sportive.API.Services;

public class WhatsAppApiService : IWhatsAppApiService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<WhatsAppApiService> _logger;

    private readonly IServiceScopeFactory _scopeFactory;

    public WhatsAppApiService(HttpClient httpClient, IConfiguration config, ILogger<WhatsAppApiService> logger, IServiceScopeFactory scopeFactory)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task<bool> SendOtpAsync(string phoneNumber, string otpCode)
    {
        try
        {
            var phoneNumberId = _config["WhatsApp:PhoneNumberId"];
            var accessToken = _config["WhatsApp:AccessToken"];
            
            // 1. Meta Cloud API (if configured)
            if (!string.IsNullOrEmpty(phoneNumberId) && !string.IsNullOrEmpty(accessToken))
            {
                var templateName = _config["WhatsApp:TemplateName"] ?? "auth_otp_template";
                var languageCode = _config["WhatsApp:LanguageCode"] ?? "ar";
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
                        components = new object[]
                        {
                            new
                            {
                                type = "body",
                                parameters = new object[]
                                {
                                    new { type = "text", text = otpCode }
                                }
                            },
                            new 
                            {
                                type = "button",
                                sub_type = "url",
                                index = "0",
                                parameters = new object[]
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
                if (response.IsSuccessStatusCode) return true;
                
                var errorResponse = await response.Content.ReadAsStringAsync();
                _logger.LogError("WhatsApp Meta API Error: {Error}", errorResponse);
            }

            // 2. Node Baileys WhatsApp Gateway (sportive-whatsapp-service)
            var serviceUrl = _config["WhatsApp:ServiceUrl"] ?? "https://sportive-whatsapp-production.up.railway.app";
            if (!string.IsNullOrEmpty(serviceUrl))
            {
                try
                {
                    var formattedPhone = NormalizePhone(phoneNumber);
                    var payload = new
                    {
                        phone = formattedPhone,
                        message = $"رمز التحقق الخاص بك في متجر Sportive هو: *{otpCode}*\nرمز التحقق صالحة لمدة 5 دقائق."
                    };
                    var request = new HttpRequestMessage(HttpMethod.Post, $"{serviceUrl.TrimEnd('/')}/send");
                    request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("OTP sent via Node WhatsApp Gateway to {Phone}", phoneNumber);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send OTP via Node WhatsApp Gateway, trying Wapilot fallback");
                }
            }

            // 3. Wapilot Gateway Fallback
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Sportive.API.Data.ApplicationDbContext>();
                var storeSettings = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.StoreInfo, s => s.StoreConfigId == 1);
                if (storeSettings != null && !string.IsNullOrEmpty(storeSettings.WapilotApiKey) && !string.IsNullOrEmpty(storeSettings.WapilotWebInstanceId))
                {
                    var messageText = $"رمز التحقق الخاص بك في متجر Sportive هو: *{otpCode}*\nرمز التحقق صالحة لمدة 5 دقائق.";
                    return await SendWapilotMessageAsync(phoneNumber, messageText, storeSettings.WapilotApiKey, storeSettings.WapilotWebInstanceId);
                }
            }

            _logger.LogWarning("No WhatsApp service configured for sending OTP.");
            return false;
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
        if (digits.StartsWith("01") && digits.Length == 11) return "20" + digits.Substring(1);
        if (digits.StartsWith("20") && digits.Length == 12) return digits;
        return digits;
    }

    public async Task<bool> SendWapilotMessageAsync(string phoneNumber, string messageText, string apiKey, string instanceId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(instanceId))
            {
                _logger.LogWarning("Wapilot API is not configured completely. Missing API Key or Instance ID.");
                return false;
            }

            var url = $"https://api.wapilot.net/api/v2/{instanceId}/send-message";
            var formattedPhone = NormalizePhone(phoneNumber);

            var payload = new
            {
                chat_id = formattedPhone + "@c.us",
                text = messageText
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("token", apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorResponse = await response.Content.ReadAsStringAsync();
                _logger.LogError("Wapilot API Error: {Error}", errorResponse);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message via Wapilot");
            return false;
        }
    }
}
