using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs.System;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[Route("api/system/subscriptions")]
[ApiController]
[Authorize(Roles = "SuperAdmin")]
public class SubscriptionsController : ControllerBase
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionsController(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllSubscriptions()
    {
        var subscriptions = await _subscriptionService.GetAllSubscriptionsAsync();
        return Ok(new { success = true, data = subscriptions });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetSubscriptionById(int id)
    {
        var subscription = await _subscriptionService.GetSubscriptionByIdAsync(id);
        if (subscription == null)
            return NotFound(new { success = false, message = "Subscription not found." });

        return Ok(new { success = true, data = subscription });
    }

    [HttpPost]
    public async Task<IActionResult> CreateSubscription([FromBody] CreateSubscriptionDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var (success, message, data) = await _subscriptionService.CreateSubscriptionAsync(request);
        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message, data });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateSubscription(int id, [FromBody] UpdateSubscriptionDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var (success, message, data) = await _subscriptionService.UpdateSubscriptionAsync(id, request);
        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message, data });
    }
}
