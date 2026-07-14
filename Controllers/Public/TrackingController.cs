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
        var ip = GetClientIpAddress();
        var ua = Request.Headers["User-Agent"].ToString();
        var referer = Request.Headers["Referer"].ToString();
        var fbp = dto.Fbp ?? Request.Cookies["_fbp"];
        var fbc = dto.Fbc ?? Request.Cookies["_fbc"];

        // Fire and forget
        _ = _capiService.SendEventAsync(dto.EventName, dto.EventId, ip, ua, fbp, fbc, dto.CustomData, dto, referer);

        return Ok(new { success = true });
    }

    private string GetClientIpAddress()
    {
        // 1. Cloudflare connecting IP
        if (Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp))
        {
            return cfIp.ToString();
        }

        // 2. X-Forwarded-For (load balancer proxies)
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedIps))
        {
            var ipList = forwardedIps.ToString();
            if (!string.IsNullOrWhiteSpace(ipList))
            {
                var firstIp = ipList.Split(',').FirstOrDefault()?.Trim();
                if (!string.IsNullOrWhiteSpace(firstIp))
                {
                    return firstIp;
                }
            }
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    }
}

public class FacebookTrackingEventDto
{
    public string EventName { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public object? CustomData { get; set; }
    public string? Fbp { get; set; }
    public string? Fbc { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? FullName { get; set; }
    public string? ExternalId { get; set; }
}
