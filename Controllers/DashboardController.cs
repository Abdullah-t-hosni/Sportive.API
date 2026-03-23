using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Staff")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboard;
    public DashboardController(IDashboardService dashboard) => _dashboard = dashboard;

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats() =>
        Ok(await _dashboard.GetStatsAsync());

    [HttpGet("sales-chart")]
    public async Task<IActionResult> GetSalesChart([FromQuery] string period = "monthly") =>
        Ok(await _dashboard.GetSalesChartAsync(period));

    [HttpGet("top-products")]
    public async Task<IActionResult> GetTopProducts([FromQuery] int count = 10) =>
        Ok(await _dashboard.GetTopProductsAsync(count));

    [HttpGet("order-status-stats")]
    public async Task<IActionResult> GetOrderStatusStats() =>
        Ok(await _dashboard.GetOrderStatusStatsAsync());

    [HttpGet("recent-orders")]
    public async Task<IActionResult> GetRecentOrders([FromQuery] int count = 10) =>
        Ok(await _dashboard.GetRecentOrdersAsync(count));
}
