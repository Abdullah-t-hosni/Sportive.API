using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/system/[controller]")]
[Authorize(Roles = AppRoles.SuperAdmin)]
public class IntegrationsController : ControllerBase
{
    [HttpGet]
    public IActionResult GetIntegrations()
    {
        var integrations = new[]
        {
            new { Id = 1, Name = "Paymob", Type = "Payment Gateway", Status = "Connected", LastSync = (DateTime?)DateTime.UtcNow.AddHours(-1) },
            new { Id = 2, Name = "WhatsApp Business", Type = "Messaging", Status = "Connected", LastSync = (DateTime?)DateTime.UtcNow.AddMinutes(-5) },
            new { Id = 3, Name = "Resend", Type = "Email Service", Status = "Connected", LastSync = (DateTime?)DateTime.UtcNow.AddDays(-2) },
            new { Id = 4, Name = "ZKTeco Devices", Type = "Biometrics", Status = "Disconnected", LastSync = (DateTime?)null },
            new { Id = 5, Name = "Cloudinary", Type = "Media Storage", Status = "Connected", LastSync = (DateTime?)DateTime.UtcNow.AddMinutes(-1) }
        };

        return Ok(integrations);
    }

    [HttpPost("{id}/toggle")]
    public IActionResult ToggleIntegration(int id)
    {
        return Ok(new { message = "Integration status toggled." });
    }

    [HttpPost("{id}/sync")]
    public IActionResult SyncIntegration(int id)
    {
        return Ok(new { message = "Integration sync triggered successfully." });
    }
}
