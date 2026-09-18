using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Attributes;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Services.Loyalty;

namespace Sportive.API.Controllers.Customers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LoyaltyController : ControllerBase
{
    private readonly ILoyaltyService _loyaltyService;
    private readonly IAuditService _audit;
    private readonly ILogger<LoyaltyController> _logger;

    public LoyaltyController(ILoyaltyService loyaltyService, IAuditService audit, ILogger<LoyaltyController> logger)
    {
        _loyaltyService = loyaltyService;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet("settings")]
    [RequirePermission(ModuleKeys.Customers + "," + ModuleKeys.Settings + "," + ModuleKeys.Pos)]
    public async Task<ActionResult<LoyaltyProgramSettingsDto>> GetSettings()
    {
        return Ok(await _loyaltyService.GetSettingsAsync());
    }

    [HttpPut("settings")]
    [RequirePermission(ModuleKeys.Settings, requireEdit: true)]
    public async Task<ActionResult<LoyaltyProgramSettingsDto>> UpdateSettings([FromBody] UpdateLoyaltySettingsDto dto)
    {
        var userName = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        var updated = await _loyaltyService.UpdateSettingsAsync(dto, userName);

        try
        {
            await _audit.LogAsync(
                "UpdateLoyaltySettings",
                "LoyaltyProgramSettings",
                "1",
                $"Updated loyalty program settings (IsEnabled={dto.IsEnabled}, PointsPerCurrency={dto.PointsPerCurrencyUnit})",
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                userName
            );
        }
        catch { }

        return Ok(updated);
    }

    [HttpGet("customers/{customerId:int}/balance")]
    [RequirePermission(ModuleKeys.Customers + "," + ModuleKeys.Pos + "," + ModuleKeys.Orders)]
    public async Task<ActionResult<CustomerLoyaltyInfoDto>> GetCustomerBalance(int customerId)
    {
        try
        {
            var info = await _loyaltyService.GetCustomerLoyaltyInfoAsync(customerId);
            return Ok(info);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("customers/{customerId:int}/transactions")]
    [RequirePermission(ModuleKeys.Customers + "," + ModuleKeys.Pos)]
    public async Task<ActionResult<List<LoyaltyPointTransactionDto>>> GetCustomerTransactions(int customerId, [FromQuery] int take = 50)
    {
        return Ok(await _loyaltyService.GetCustomerTransactionsAsync(customerId, take));
    }

    [HttpPost("customers/{customerId:int}/adjust")]
    [RequirePermission(ModuleKeys.Customers, requireEdit: true)]
    public async Task<ActionResult<LoyaltyPointTransactionDto>> AdjustPoints(int customerId, [FromBody] AdjustLoyaltyPointsDto dto)
    {
        if (dto.Points == 0) return BadRequest("Points adjustment cannot be zero");

        var userName = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        try
        {
            var tx = await _loyaltyService.AdjustCustomerPointsAsync(customerId, dto.Points, dto.Note, userName);

            try
            {
                await _audit.LogAsync(
                    "AdjustLoyaltyPoints",
                    "Customer",
                    customerId.ToString(),
                    $"Adjusted loyalty points: {dto.Points} points. Note: {dto.Note}",
                    User.FindFirstValue(ClaimTypes.NameIdentifier),
                    userName
                );
            }
            catch { }

            return Ok(tx);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
