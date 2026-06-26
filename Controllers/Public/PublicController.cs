using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers.Public;

[Route("api/public")]
[ApiController]
[AllowAnonymous]
public class PublicController : ControllerBase
{
    private readonly IPlanService _planService;

    public PublicController(IPlanService planService)
    {
        _planService = planService;
    }

    [HttpGet("plans")]
    public async Task<IActionResult> GetActivePlans()
    {
        // Only return active plans to the public frontend
        var plans = await _planService.GetAllPlansAsync(includeInactive: false);
        return Ok(new { success = true, data = plans });
    }
}
