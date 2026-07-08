using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Services;

public interface IFacebookCapiService
{
    Task SendPurchaseEventAsync(Order order, string clientIp, string userAgent, string? fbp, string? fbc);
    Task SendEventAsync(string eventName, string eventId, string clientIp, string userAgent, string? fbp, string? fbc, object? customData = null, object? userData = null);
}

public class FacebookCapiService : IFacebookCapiService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _httpClient;
    private readonly ILogger<FacebookCapiService> _logger;

    public FacebookCapiService(IServiceScopeFactory scopeFactory, HttpClient httpClient, ILogger<FacebookCapiService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task SendPurchaseEventAsync(Order order, string clientIp, string userAgent, string? fbp, string? fbc)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var settings = await db.StoreInfo.FirstOrDefaultAsync(x => x.StoreConfigId == 1);
            if (settings == null || string.IsNullOrEmpty(settings.FacebookPixelId) || string.IsNullOrEmpty(settings.FacebookCapiToken))
            {
                return; // CAPI not configured
            }

            var url = $"https://graph.facebook.com/v20.0/{settings.FacebookPixelId}/events";
            var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var eventData = new
            {
                data = new[]
                {
                    new
                    {
                        event_name = "Purchase",
                        event_time = unixTime,
                        action_source = "website",
                        event_id = order.OrderNumber,
                        user_data = new
                        {
                            client_ip_address = clientIp,
                            client_user_agent = userAgent,
                            em = HashData(order.Customer?.Email),
                            ph = HashData(order.Customer?.Phone),
                            fn = HashData(GetFirstName(order.Customer?.FullName)),
                            ln = HashData(GetLastName(order.Customer?.FullName)),
                            ct = HashData(order.DeliveryAddress?.City),
                            country = HashData("eg"), // Default to Egypt, or extract from address if needed
                            external_id = HashData(order.CustomerId.ToString()),
                            fbp = fbp,
                            fbc = fbc
                        },
                        custom_data = new
                        {
                            currency = "EGP",
                            value = order.TotalAmount,
                            content_ids = order.Items?.Select(i => i.ProductId.ToString()).ToArray() ?? Array.Empty<string>(),
                            content_type = "product"
                        }
                    }
                },
                access_token = settings.FacebookCapiToken,
                test_event_code = string.IsNullOrWhiteSpace(settings.FacebookTestEventCode) ? null : settings.FacebookTestEventCode
            };

            var json = JsonSerializer.Serialize(eventData, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning($"Facebook CAPI Error (Purchase): {error}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Facebook CAPI Purchase event");
        }
    }

    public async Task SendEventAsync(string eventName, string eventId, string clientIp, string userAgent, string? fbp, string? fbc, object? customData = null, object? userData = null)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var settings = await db.StoreInfo.FirstOrDefaultAsync(x => x.StoreConfigId == 1);
            if (settings == null || string.IsNullOrEmpty(settings.FacebookPixelId) || string.IsNullOrEmpty(settings.FacebookCapiToken))
            {
                return; // CAPI not configured
            }

            var url = $"https://graph.facebook.com/v20.0/{settings.FacebookPixelId}/events";
            var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var eventData = new
            {
                data = new[]
                {
                    new
                    {
                        event_name = eventName,
                        event_time = unixTime,
                        action_source = "website",
                        event_id = eventId,
                        user_data = userData ?? new
                        {
                            client_ip_address = clientIp,
                            client_user_agent = userAgent,
                            fbp = fbp,
                            fbc = fbc
                        },
                        custom_data = customData
                    }
                },
                access_token = settings.FacebookCapiToken,
                test_event_code = string.IsNullOrWhiteSpace(settings.FacebookTestEventCode) ? null : settings.FacebookTestEventCode
            };

            var json = JsonSerializer.Serialize(eventData, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning($"Facebook CAPI Error ({eventName}): {error}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Facebook CAPI generic event {EventName}", eventName);
        }
    }

    private string? HashData(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim().ToLowerInvariant();
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    private string? GetFirstName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return null;
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : null;
    }

    private string? GetLastName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return null;
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[parts.Length - 1] : null;
    }
}
