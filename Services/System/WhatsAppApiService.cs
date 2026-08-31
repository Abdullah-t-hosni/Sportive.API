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
            var storeSettings = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.StoreInfo, s => s.StoreConfigId == 1);

            // 🌟 100% DYNAMIC: Read whatever URL is saved in Store Settings UI by the user!
            var userConfiguredUrl = isPos 
                ? storeSettings?.WhatsAppPosGatewayUrl 
                : storeSettings?.WhatsAppStoreGatewayUrl;

            // Auto-repair legacy/broken Railway URL if missing -65ac suffix
            if (!string.IsNullOrWhiteSpace(userConfiguredUrl) && userConfiguredUrl.Equals("https://sportive-frontend-production.up.railway.app", StringComparison.OrdinalIgnoreCase))
            {
                userConfiguredUrl = "https://sportive-frontend-production-65ac.up.railway.app";
            }

            // Fallback hierarchy: 1. User DB Setting -> 2. AppSettings Config -> 3. Hardcoded Fallback
            var serviceUrl = !string.IsNullOrWhiteSpace(userConfiguredUrl)
                ? userConfiguredUrl
                : (_config["WhatsApp:ServiceUrl"] ?? "https://sportive-frontend-production-65ac.up.railway.app");

            if (!string.IsNullOrWhiteSpace(serviceUrl))
            {
                var formattedPhone = NormalizePhone(phoneNumber);
                var payload = new
                {
                    phone = formattedPhone,
                    message = messageText
                };
                var targetUri = $"{serviceUrl.TrimEnd('/')}/send";
                _logger.LogInformation("[WhatsApp] Sending message via Dynamic Gateway {Url} to {Phone}", targetUri, formattedPhone);

                var request = new HttpRequestMessage(HttpMethod.Post, targetUri);
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[WhatsApp] Message successfully sent via Dynamic Gateway to {Phone}", phoneNumber);
                    return true;
                }
                
                var errorResponse = await response.Content.ReadAsStringAsync();
                _logger.LogError("[WhatsApp] Node WhatsApp Gateway API Error ({StatusCode}): {Error}", response.StatusCode, errorResponse);
            }
            else
            {
                _logger.LogWarning("[WhatsApp] No active Node WhatsApp Gateway configured for sending message.");
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send generic message via Node WhatsApp Gateway");
            return false;
        }
    }
}
