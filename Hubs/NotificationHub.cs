using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Sportive.API.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    private readonly Sportive.API.Interfaces.ITenantContext _tenantContext;

    public NotificationHub(Sportive.API.Interfaces.ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    private string GetPrefix() => _tenantContext.CurrentTenant?.Slug?.ToLowerInvariant() ?? "global";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        var prefix = GetPrefix();

        await Groups.AddToGroupAsync(Context.ConnectionId, $"{prefix}_All");

        if (!string.IsNullOrEmpty(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"{prefix}_{userId}");

        // Admin group
        if (Context.User?.IsInRole("Admin") == true || Context.User?.IsInRole("SuperAdmin") == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"{prefix}_Admin");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        var prefix = GetPrefix();

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"{prefix}_All");

        if (!string.IsNullOrEmpty(userId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"{prefix}_{userId}");

        await base.OnDisconnectedAsync(exception);
    }
}
