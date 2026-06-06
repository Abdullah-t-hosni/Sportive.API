using Sportive.API.Models;
using System.Threading.Tasks;

namespace Sportive.API.Interfaces;

public interface ISecurityEventsEngine
{
    Task TrackEventAsync(
        string? userId, 
        string ipAddress, 
        string userAgent, 
        SecurityEventType eventType, 
        SecuritySeverity severity, 
        int riskScore, 
        string correlationId);
}
