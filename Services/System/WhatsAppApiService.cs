using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
            var message = $"رمز التحقق الخاص بك في متجر Sportive هو: *{otpCode}*\nرمز التحقق صالح لمدة 5 دقائق.";
            return await SendWhatsAppMessageAsync(phoneNumber, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OTP via WhatsApp");
            return false;
        }
    }

    private static string NormalizePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "";
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0020")) digits = digits.Substring(2);
        if (digits.StartsWith("01") && digits.Length == 11) return "20" + digits.Substring(1);
        if (digits.StartsWith("20") && digits.Length == 12) return digits;
        if (digits.Length == 10 && !digits.StartsWith("20")) return "20" + digits;
        return digits;
    }

    public async Task<bool> SendWhatsAppMessageAsync(string phoneNumber, string messageText, bool isPos = false)
    {
        var formattedPhone = NormalizePhone(phoneNumber);
        if (string.IsNullOrEmpty(formattedPhone))
        {
            _logger.LogWarning("[WhatsApp] Aborting send: Phone number is empty or invalid ({Phone})", phoneNumber);
            return false;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Sportive.API.Data.AppDbContext>();
            var storeSettings = await db.StoreInfo.OrderBy(s => s.StoreConfigId).FirstOrDefaultAsync();

            // 🎯 DIRECT & STRICT: Read whatever URL is saved in Store Settings UI
            var serviceUrl = isPos 
                ? storeSettings?.WhatsAppPosGatewayUrl 
                : storeSettings?.WhatsAppStoreGatewayUrl;

            if (string.IsNullOrWhiteSpace(serviceUrl))
            {
                serviceUrl = _config["WhatsApp:ServiceUrl"];
            }

            if (string.IsNullOrWhiteSpace(serviceUrl))
            {
                serviceUrl = "https://sportive-frontend-production-65ac.up.railway.app";
            }

            var targetUri = $"{serviceUrl.TrimEnd('/')}/send";

            // 🔄 Retry loop: 3 attempts with 2.5s delay to handle temporary disconnects / cold starts
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    var payload = new
                    {
                        phone = formattedPhone,
                        message = messageText
                    };

                    _logger.LogInformation("[WhatsApp Attempt {Attempt}/3] Sending message via Gateway {Url} to {Phone}", attempt, targetUri, formattedPhone);

                    using var request = new HttpRequestMessage(HttpMethod.Post, targetUri);
                    request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("[WhatsApp] Message successfully sent via Gateway to {Phone}", formattedPhone);
                        return true;
                    }

                    var errorResponse = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("[WhatsApp Attempt {Attempt}/3] Gateway API Error ({StatusCode}): {Error}", attempt, response.StatusCode, errorResponse);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[WhatsApp Attempt {Attempt}/3] Exception sending to Gateway for {Phone}", attempt, formattedPhone);
                }

                if (attempt < 3)
                {
                    await Task.Delay(2500);
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message via WhatsApp Gateway for {Phone}", phoneNumber);
            return false;
        }
    }
}
