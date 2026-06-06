using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sportive.API.Data;
using Sportive.API.Utils;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Sportive.API.Middleware;

public class SessionLastSeenMiddleware
{
    private readonly RequestDelegate _next;

    public SessionLastSeenMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var sessionIdClaim = context.User.FindFirst("SessionId")?.Value;
            if (!string.IsNullOrEmpty(sessionIdClaim) && Guid.TryParse(sessionIdClaim, out var sessionId))
            {
                try
                {
                    using var scope = context.RequestServices.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var session = await db.UserSessions.FindAsync(sessionId);
                    if (session != null && !session.IsRevoked)
                    {
                        var now = TimeHelper.GetEgyptTime();
                        if ((now - session.LastSeen).TotalMinutes >= 5)
                        {
                            session.LastSeen = now;
                            await db.SaveChangesAsync();
                        }
                    }
                }
                catch
                {
                    // Fail silently so session tracking never breaks API requests
                }
            }
        }
        await _next(context);
    }
}
