using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class TrackingController : ControllerBase
{
    private readonly IFacebookCapiService _capiService;

    public TrackingController(IFacebookCapiService capiService)
    {
        _capiService = capiService;
    }

    [HttpPost("facebook")]
    public IActionResult PostFacebookEvent([FromBody] FacebookTrackingEventDto dto)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var ua = Request.Headers["User-Agent"].ToString();
        var fbp = dto.Fbp ?? Request.Cookies["_fbp"];
        var fbc = dto.Fbc ?? Request.Cookies["_fbc"];

        // Fire and forget
        _ = _capiService.SendEventAsync(dto.EventName, dto.EventId, ip, ua, fbp, fbc, dto.CustomData);

        return Ok(new { success = true });
    }
}

public class FacebookTrackingEventDto
{
    public string EventName { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public object? CustomData { get; set; }
    public string? Fbp { get; set; }
    public string? Fbc { get; set; }
}
