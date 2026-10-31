using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
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
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMinutes(5);

    public SessionLastSeenMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IMemoryCache cache)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var sessionIdClaim = context.User.FindFirst("SessionId")?.Value;
            if (!string.IsNullOrEmpty(sessionIdClaim) && Guid.TryParse(sessionIdClaim, out var sessionId))
            {
                var cacheKey = $"session_seen_{sessionId}";
                if (!cache.TryGetValue(cacheKey, out _))
                {
                    cache.Set(cacheKey, true, ThrottleInterval);
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
        }
        await _next(context);
    }
}
