using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs.System;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[Route("api/system/plans")]
[ApiController]
[Authorize(Roles = "SuperAdmin")]
public class PlansController : ControllerBase
{
    private readonly IPlanService _planService;

    public PlansController(IPlanService planService)
    {
        _planService = planService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllPlans([FromQuery] bool includeInactive = false)
    {
        var plans = await _planService.GetAllPlansAsync(includeInactive);
        return Ok(new { success = true, data = plans });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPlanById(int id)
    {
        var plan = await _planService.GetPlanByIdAsync(id);
        if (plan == null)
            return NotFound(new { success = false, message = "Plan not found." });

        return Ok(new { success = true, data = plan });
    }

    [HttpPost]
    public async Task<IActionResult> CreatePlan([FromBody] CreatePlanDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var (success, message, data) = await _planService.CreatePlanAsync(request);
        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message, data });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePlan(int id, [FromBody] UpdatePlanDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var (success, message, data) = await _planService.UpdatePlanAsync(id, request);
        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message, data });
    }

    [HttpPut("{id}/deactivate")]
    public async Task<IActionResult> DeactivatePlan(int id)
    {
        var (success, message) = await _planService.DeactivatePlanAsync(id);
        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message });
    }
}
