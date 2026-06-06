using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;

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
                await SendViaResendAsync(to, subject, body, settings.ResendApiKey, settings.StoreEmailAddr, settings.StoreBrandName);
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

    private async Task SendViaResendAsync(string to, string subject, string body, string apiKey, string? fromEmail, string fromName)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var finalFrom = string.IsNullOrEmpty(fromEmail) ? "onboarding@resend.dev" : fromEmail;
            var fromWithLabel = $"{fromName} <{finalFrom}>";

            var payload = new
            {
                from = fromWithLabel,
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
        
        string fromName = "Sportive Store";
        try {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = db.StoreInfo.FirstOrDefault();
            if (settings != null) fromName = settings.StoreBrandName;
        } catch { }

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
                From = new MailAddress(fromEmail!, fromName),
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

    public async Task SendBulkPayrollEmailsAsync(int payrollRunId, string storeUrl, List<int>? employeeIds = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var run = await db.PayrollRuns
            .Include(p => p.Items)
            .ThenInclude(i => i.Employee)
            .FirstOrDefaultAsync(p => p.Id == payrollRunId);

        if (run == null)
        {
            _logger.LogError("Payroll run {Id} not found for bulk emails.", payrollRunId);
            return;
        }

        var settings = await db.StoreInfo.FirstOrDefaultAsync();
        var currency = settings?.CurrencySymbol ?? "ج.م";
        var storeName = settings?.StoreBrandName ?? "Sportive";

        var items = run.Items.AsEnumerable();
        if (employeeIds != null && employeeIds.Any())
        {
            items = items.Where(i => employeeIds.Contains(i.EmployeeId));
        }

        foreach (var item in items)
        {
            var email = item.Employee?.Email?.Trim();
            if (string.IsNullOrEmpty(email))
            {
                _logger.LogWarning("Employee {EmpName} has no registered email. Skipping.", item.Employee?.Name ?? "Unknown");
                continue;
            }

            try
            {
                var hash = GeneratePayslipHash(item.Id);
                var payslipUrl = $"{storeUrl.TrimEnd('/')}/admin/payslip?id={item.Id}&hash={hash}";
                
                var subject = $"قسيمة الراتب - شهر {run.PeriodMonth}/{run.PeriodYear}";
                var body = BuildPayslipEmailBody(item.Employee!.Name, run.PeriodMonth, run.PeriodYear, item.NetPayable, currency, payslipUrl, storeName);

                await SendEmailAsync(email, subject, body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send payslip email to employee {EmpId} ({Email})", item.EmployeeId, email);
            }
        }
    }

    private static string GeneratePayslipHash(int payrollItemId)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes("SportiveSecretPayslipSaltKey2026"));
        var hashBytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"payslip-{payrollItemId}"));
        return Convert.ToHexString(hashBytes).ToLower().Substring(0, 10);
    }

    private static string BuildPayslipEmailBody(string employeeName, int month, int year, decimal netPayable, string currency, string payslipUrl, string storeName)
    {
        return $@"
        <div style=""font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; max-width: 600px; margin: 0 auto; border: 1px solid #e2e8f0; border-radius: 16px; overflow: hidden; background-color: #ffffff;"">
            <div style=""background: linear-gradient(90deg, #059669 0%, #10b981 100%); padding: 30px; text-align: center; color: #ffffff;"">
                <h2 style=""margin: 0; font-size: 24px; font-weight: 800;"">قسيمة الراتب الإلكترونية</h2>
                <p style=""margin: 5px 0 0 0; font-size: 14px; opacity: 0.9;"">شهر {month} لعام {year}</p>
            </div>
            <div style=""padding: 30px; direction: rtl; text-align: right; color: #1e293b;"">
                <p style=""font-size: 16px; font-weight: bold; margin-bottom: 20px;"">عزيزي/عزيزتي {employeeName}،</p>
                <p style=""font-size: 14px; line-height: 1.6; margin-bottom: 30px;"">يسعدنا مشاركة تفاصيل راتبكم لشهر {month} لعام {year}. يمكنكم الاطلاع على تفاصيل المستحقات والاستقطاعات وتحميل قسيمة الراتب كملف PDF عبر الرابط المرفق أدناه.</p>
                
                <div style=""background-color: #f8fafc; border: 1px solid #f1f5f9; border-radius: 12px; padding: 20px; text-align: center; margin-bottom: 30px;"">
                    <p style=""margin: 0; font-size: 12px; color: #64748b; font-weight: bold; text-transform: uppercase;"">صافي الراتب المستحق</p>
                    <p style=""margin: 5px 0 0 0; font-size: 28px; font-weight: 900; color: #059669;"">{netPayable:N2} {currency}</p>
                </div>
                
                <div style=""text-align: center; margin-bottom: 30px;"">
                    <a href=""{payslipUrl}"" style=""display: inline-block; background-color: #0f172a; color: #ffffff; padding: 14px 28px; border-radius: 12px; font-size: 14px; font-weight: bold; text-decoration: none; box-shadow: 0 4px 12px rgba(15, 23, 42, 0.15);"">عرض تفاصيل قسيمة الراتب</a>
                </div>
                
                <p style=""font-size: 12px; color: #94a3b8; line-height: 1.6; margin-top: 40px; border-top: 1px solid #f1f5f9; padding-top: 20px;"">* هذه الرسالة مرسلة تلقائياً من نظام الموارد البشرية لـ {storeName}. يرجى مراجعة إدارة الحسابات أو الموارد البشرية في حال وجود أي استفسار.</p>
            </div>
        </div>";
    }
}
