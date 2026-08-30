using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    private readonly Sportive.API.Interfaces.ITenantContext _tenantContext;
    private readonly IServiceScopeFactory _scopeFactory;

    public NotificationHub(
        Sportive.API.Interfaces.ITenantContext tenantContext,
        IServiceScopeFactory scopeFactory)
    {
        _tenantContext = tenantContext;
        _scopeFactory = scopeFactory;
    }

    private string GetPrefix() => _tenantContext.CurrentTenant?.Slug?.ToLowerInvariant() ?? "global";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        var prefix = GetPrefix();
        var connectionId = Context.ConnectionId;

        await Groups.AddToGroupAsync(connectionId, $"{prefix}_All");
        await Groups.AddToGroupAsync(connectionId, "global_All");

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(connectionId, $"{prefix}_{userId}");
            await Groups.AddToGroupAsync(connectionId, $"global_{userId}");
            await Groups.AddToGroupAsync(connectionId, $"user_{userId}");
        }

        // Admin / Staff print group
        bool isAdminOrStaff = Context.User?.IsInRole("Admin") == true 
            || Context.User?.IsInRole("SuperAdmin") == true 
            || Context.User?.IsInRole("Manager") == true 
            || Context.User?.IsInRole("Cashier") == true;

        if (isAdminOrStaff)
        {
            await Groups.AddToGroupAsync(connectionId, $"{prefix}_Admin");

            // 🖨️ AUTO CATCH-UP PRINT: Send all unprinted store orders to the newly connected print agent
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1500); // Give connection time to settle
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var pendingStatuses = new[] { OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.ReadyForPickup };
                    var cutoffDate = DateTime.UtcNow.AddDays(-7);

                    var unprintedOrders = await db.Orders
                        .Where(o => !o.IsPrinted 
                                 && o.CreatedAt >= cutoffDate 
                                 && pendingStatuses.Contains(o.Status) 
                                 && o.Source != OrderSource.POS)
                        .OrderBy(o => o.CreatedAt)
                        .Take(20)
                        .ToListAsync();

                    foreach (var order in unprintedOrders)
                    {
                        await Clients.Client(connectionId).SendAsync("ReceiveNewOrderToPrint", order.Id);
                        order.IsPrinted = true;
                        order.PrintedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync();
                        await Task.Delay(800);
                    }
                }
                catch
                {
                    // Fail-safe silence for hub background task
                }
            });
        }

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
