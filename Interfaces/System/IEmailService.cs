using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sportive.API.Interfaces;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body);
    Task SendBulkPayrollEmailsAsync(int payrollRunId, string storeUrl, List<int>? employeeIds = null);
}

public interface IAiAssistantService
{
    Task<string> ChatAsync(string userMessage, string? conversationId = null, bool isAdmin = false);
}
