using Sportive.API.Models;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.Maintenance, requireEdit: true)]
public class BackfillController : ControllerBase
{
    private readonly IBackfillService _backfillService;

    public BackfillController(IBackfillService backfillService)
    {
        _backfillService = backfillService;
    }

    /// <summary>
    /// ÙŠÙ‚ÙˆÙ… Ø¨ØªØ±Ø­ÙŠÙ„ ÙƒØ§ÙØ© Ø§Ù„Ø·Ù„Ø¨Ø§Øª Ø§Ù„ØªÙŠ Ù„Ù… ÙŠØªÙ… ØªØ±Ø­ÙŠÙ„Ù‡Ø§ Ù…Ø­Ø§Ø³Ø¨ÙŠØ§Ù‹ Ø¨Ø¹Ø¯
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
    /// ÙŠÙ‚ÙˆÙ… Ø¨ØªØ±Ø­ÙŠÙ„ ÙƒØ§ÙØ© ÙÙˆØ§ØªÙŠØ± Ø§Ù„Ù…Ø´ØªØ±ÙŠØ§Øª Ø§Ù„ØªÙŠ Ù„Ù… ÙŠØªÙ… ØªØ±Ø­ÙŠÙ„Ù‡Ø§
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

