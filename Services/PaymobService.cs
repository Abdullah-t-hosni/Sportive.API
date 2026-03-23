using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sportive.API.Services;

// ─── DTOs ────────────────────────────────────────────────────
public record PaymobOrderRequest(decimal Amount, int OrderId, string OrderNumber, string Email, string Phone, string FullName);
public record PaymobPaymentResponse(bool Success, string? PaymentUrl, string? Error);

// ─── Service Interface ───────────────────────────────────────
public interface IPaymobService
{
    Task<PaymobPaymentResponse> CreatePaymentAsync(PaymobOrderRequest request);
    bool VerifyCallback(Dictionary<string, string> callbackData);
}

// ─── Implementation ──────────────────────────────────────────
public class PaymobService : IPaymobService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<PaymobService> _logger;

    private string ApiKey       => _config["Paymob:ApiKey"]       ?? throw new InvalidOperationException("Paymob:ApiKey not configured");
    private string IntegrationId => _config["Paymob:IntegrationId"] ?? throw new InvalidOperationException("Paymob:IntegrationId not configured");
    private string IframeId     => _config["Paymob:IframeId"]     ?? throw new InvalidOperationException("Paymob:IframeId not configured");
    private string HmacSecret   => _config["Paymob:HmacSecret"]   ?? "";

    public PaymobService(IConfiguration config, IHttpClientFactory factory, ILogger<PaymobService> logger)
    {
        _config = config;
        _httpFactory = factory;
        _logger = logger;
    }

    public async Task<PaymobPaymentResponse> CreatePaymentAsync(PaymobOrderRequest request)
    {
        try
        {
            var client = _httpFactory.CreateClient("Paymob");

            // Step 1: Authentication
            var authToken = await AuthenticateAsync(client);
            if (authToken == null)
                return new PaymobPaymentResponse(false, null, "Authentication failed");

            // Step 2: Register order
            var paymobOrderId = await RegisterOrderAsync(client, authToken, request);
            if (paymobOrderId == null)
                return new PaymobPaymentResponse(false, null, "Order registration failed");

            // Step 3: Payment key
            var paymentKey = await GetPaymentKeyAsync(client, authToken, paymobOrderId, request);
            if (paymentKey == null)
                return new PaymobPaymentResponse(false, null, "Payment key generation failed");

            // Step 4: Build iframe URL
            var paymentUrl = $"https://accept.paymob.com/api/acceptance/iframes/{IframeId}?payment_token={paymentKey}";

            return new PaymobPaymentResponse(true, paymentUrl, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paymob payment creation failed for order {OrderId}", request.OrderId);
            return new PaymobPaymentResponse(false, null, ex.Message);
        }
    }

    private async Task<string?> AuthenticateAsync(HttpClient client)
    {
        var body = JsonSerializer.Serialize(new { api_key = ApiKey });
        var response = await client.PostAsync(
            "https://accept.paymob.com/api/auth/tokens",
            new StringContent(body, Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("token").GetString();
    }

    private async Task<string?> RegisterOrderAsync(HttpClient client, string authToken, PaymobOrderRequest request)
    {
        // Paymob uses cents (amount * 100)
        var amountCents = (int)(request.Amount * 100);

        var body = JsonSerializer.Serialize(new
        {
            auth_token = authToken,
            delivery_needed = false,
            amount_cents = amountCents.ToString(),
            currency = "EGP",
            merchant_order_id = request.OrderNumber,
            items = new[]
            {
                new
                {
                    name = $"Order {request.OrderNumber}",
                    amount_cents = amountCents.ToString(),
                    description = $"Sportive Order #{request.OrderId}",
                    quantity = "1"
                }
            }
        });

        var response = await client.PostAsync(
            "https://accept.paymob.com/api/ecommerce/orders",
            new StringContent(body, Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetInt64().ToString();
    }

    private async Task<string?> GetPaymentKeyAsync(
        HttpClient client, string authToken, string paymobOrderId, PaymobOrderRequest request)
    {
        var amountCents = (int)(request.Amount * 100);

        var body = JsonSerializer.Serialize(new
        {
            auth_token = authToken,
            amount_cents = amountCents.ToString(),
            expiration = 3600,
            order_id = paymobOrderId,
            billing_data = new
            {
                apartment       = "NA",
                email           = request.Email,
                floor           = "NA",
                first_name      = request.FullName.Split(' ').FirstOrDefault() ?? "Sportive",
                street          = "NA",
                building        = "NA",
                phone_number    = request.Phone,
                shipping_method = "NA",
                postal_code     = "NA",
                city            = "Cairo",
                country         = "EG",
                last_name       = request.FullName.Split(' ').Skip(1).FirstOrDefault() ?? "Customer",
                state           = "NA"
            },
            currency = "EGP",
            integration_id = int.Parse(IntegrationId),
            lock_order_when_paid = false
        });

        var response = await client.PostAsync(
            "https://accept.paymob.com/api/acceptance/payment_keys",
            new StringContent(body, Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("token").GetString();
    }

    public bool VerifyCallback(Dictionary<string, string> callbackData)
    {
        if (string.IsNullOrEmpty(HmacSecret)) return true; // skip in dev

        // Paymob HMAC verification
        var fields = new[]
        {
            "amount_cents", "created_at", "currency", "error_occured",
            "has_parent_transaction", "id", "integration_id", "is_3d_secure",
            "is_auth", "is_capture", "is_refunded", "is_standalone_payment",
            "is_voided", "order", "owner", "pending",
            "source_data.pan", "source_data.sub_type", "source_data.type",
            "success"
        };

        var values = fields
            .Where(f => callbackData.ContainsKey(f))
            .Select(f => callbackData[f]);

        var concatenated = string.Concat(values);

        using var hmac = new System.Security.Cryptography.HMACSHA512(
            Encoding.UTF8.GetBytes(HmacSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(concatenated));
        var computed = BitConverter.ToString(hash).Replace("-", "").ToLower();

        callbackData.TryGetValue("hmac", out var received);
        return computed == received?.ToLower();
    }
}
