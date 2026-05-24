namespace Sportive.API.Interfaces;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body);
}

public interface IAiAssistantService
{
    Task<string> ChatAsync(string userMessage, string? conversationId = null, bool isAdmin = false);
}
