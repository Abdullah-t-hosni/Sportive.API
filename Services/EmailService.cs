using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Interfaces;

namespace Sportive.API.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;

    public EmailService(IConfiguration config, ILogger<EmailService> logger, IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
    }

    public async Task SendEmailAsync(string to, string subject, string body)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await db.StoreInfo.FirstOrDefaultAsync();

            if (settings != null && !string.IsNullOrEmpty(settings.ResendApiKey))
            {
                await SendViaResendAsync(to, subject, body, settings.ResendApiKey, settings.StoreEmailAddr);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RESEND_CONFIG_ERROR: Failed to fetch settings from DB.");
        }

        // Fallback to SMTP
        await SendViaSmtpAsync(to, subject, body);
    }

    private async Task SendViaResendAsync(string to, string subject, string body, string apiKey, string? fromEmail)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var payload = new
            {
                from = string.IsNullOrEmpty(fromEmail) ? "onboarding@resend.dev" : fromEmail,
                to = new[] { to },
                subject = subject,
                html = body
            };

            var response = await client.PostAsync("https://api.resend.com/emails", 
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("RESEND_SUCCESS: Sent to {To}", to);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("RESEND_API_ERROR: {Error}", error);
                // Fallback to SMTP if Resend fails?
                await SendViaSmtpAsync(to, subject, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RESEND_GENERIC_ERROR: Failed to send to {To}", to);
            await SendViaSmtpAsync(to, subject, body);
        }
    }

    private async Task SendViaSmtpAsync(string to, string subject, string body)
    {
        var smtpHost = _config["Email:Host"] ?? "smtp.gmail.com";
        var smtpPort = int.Parse(_config["Email:Port"] ?? "587");
        var smtpUser = _config["Email:User"];
        var smtpPass = _config["Email:Pass"];
        var fromEmail = _config["Email:From"] ?? smtpUser;

        if (string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(smtpUser))
        {
            _logger.LogWarning("EMAIL_ERROR: Email service is not configured (Host or User is empty).");
            return;
        }

        try
        {
            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                Credentials = new NetworkCredential(smtpUser, smtpPass),
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Timeout = 10000 // 10 seconds
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(fromEmail!, "Sportive Store"),
                Subject = subject,
                Body = body,
                IsBodyHtml = true,
                Priority = MailPriority.High
            };
            mailMessage.To.Add(to);

            await client.SendMailAsync(mailMessage);
            _logger.LogInformation("EMAIL_SUCCESS: Sent to {To}", to);
        }
        catch (SmtpException smtpEx)
        {
            _logger.LogError(smtpEx, "EMAIL_SMTP_ERROR: Failed to send to {To}. Status: {StatusCode}, Details: {Msg}", to, smtpEx.StatusCode, smtpEx.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EMAIL_GENERIC_ERROR: Unexpected error sending to {To}", to);
        }
    }
}
