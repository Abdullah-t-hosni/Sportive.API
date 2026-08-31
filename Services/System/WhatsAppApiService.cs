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
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("01") && digits.Length == 11) return "20" + digits.Substring(1);
        if (digits.StartsWith("20") && digits.Length == 12) return digits;
        return digits;
    }

    public async Task<bool> SendWhatsAppMessageAsync(string phoneNumber, string messageText, bool isPos = false)
    {
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

            var formattedPhone = NormalizePhone(phoneNumber);
            var payload = new
            {
                phone = formattedPhone,
                message = messageText
            };
            var targetUri = $"{serviceUrl.TrimEnd('/')}/send";
            _logger.LogInformation("[WhatsApp] Sending message via Gateway {Url} to {Phone}", targetUri, formattedPhone);

            var request = new HttpRequestMessage(HttpMethod.Post, targetUri);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[WhatsApp] Message successfully sent via Gateway to {Phone}", phoneNumber);
                return true;
            }
            
            var errorResponse = await response.Content.ReadAsStringAsync();
            _logger.LogError("[WhatsApp] Gateway API Error ({StatusCode}): {Error}", response.StatusCode, errorResponse);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message via WhatsApp Gateway");
            return false;
        }
    }
}
