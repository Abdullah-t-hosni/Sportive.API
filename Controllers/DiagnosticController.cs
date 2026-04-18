using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiagnosticController : ControllerBase
{
    private readonly AppDbContext _db;

    public DiagnosticController(AppDbContext db) => _db = db;

    [HttpGet("debt-analysis")]
    public async Task<IActionResult> GetDebtAnalysis()
    {
        var overpaidOrders = await _db.Orders
            .Where(o => o.PaidAmount > o.TotalAmount + 0.01m && o.Status != OrderStatus.Cancelled)
            .Select(o => new {
                o.OrderNumber,
                o.TotalAmount,
                o.PaidAmount,
                Diff = o.PaidAmount - o.TotalAmount,
                o.Status,
                o.PaymentStatus
            })
            .ToListAsync();

        var totalDebt = await _db.Orders
            .Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned)
            .SumAsync(o => (decimal?)(o.TotalAmount - o.PaidAmount)) ?? 0;

        return Ok(new {
            totalDebt,
            count = overpaidOrders.Count,
            orders = overpaidOrders
        });
    }
}
