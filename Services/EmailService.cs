using System.Net;
using System.Net.Mail;
using Sportive.API.Interfaces;

namespace Sportive.API.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string body)
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
