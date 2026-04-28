using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class BackfillController : ControllerBase
{
    private readonly IBackfillService _backfillService;

    public BackfillController(IBackfillService backfillService)
    {
        _backfillService = backfillService;
    }

    /// <summary>
    /// يقوم بترحيل كافة الطلبات التي لم يتم ترحيلها محاسبياً بعد
    /// </summary>
    [HttpPost("post-missing-orders")]
    public async Task<IActionResult> PostMissingOrders()
    {
        var result = await _backfillService.PostMissingOrdersAsync();
        return Ok(new { 
            total = result.Total,
            success = result.Success,
            failed = result.Failed,
            errors = result.Errors
        });
    }

    /// <summary>
    /// يقوم بترحيل كافة فواتير المشتريات التي لم يتم ترحيلها
    /// </summary>
    [HttpPost("post-missing-purchases")]
    public async Task<IActionResult> PostMissingPurchases()
    {
        var result = await _backfillService.PostMissingPurchasesAsync();
        return Ok(new { 
            total = result.Total,
            success = result.Success,
            failed = result.Failed,
            errors = result.Errors
        });
    }
}
