using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/system/[controller]")]
[Authorize(Roles = AppRoles.SuperAdmin)]
public class ApiKeysController : ControllerBase
{
    /// <summary>GET /api/system/apikeys — جلب مفاتيح الـ API الخاصة بالمنصة</summary>
    [HttpGet]
    public IActionResult GetApiKeys()
    {
        // Mock data until DB schema implements PlatformApiKeys
        var keys = new[]
        {
            new { Id = 1, Name = "Mobile App Production", KeyPrefix = "pk_live_8f92...", CreatedAt = DateTime.UtcNow.AddDays(-30), LastUsedAt = (DateTime?)DateTime.UtcNow.AddMinutes(-5), Status = "Active" },
            new { Id = 2, Name = "Mobile App Staging", KeyPrefix = "pk_test_4b71...", CreatedAt = DateTime.UtcNow.AddDays(-60), LastUsedAt = (DateTime?)DateTime.UtcNow.AddDays(-1), Status = "Active" },
            new { Id = 3, Name = "Third-Party ERP Sync", KeyPrefix = "pk_live_1c99...", CreatedAt = DateTime.UtcNow.AddDays(-120), LastUsedAt = (DateTime?)null, Status = "Revoked" }
        };

        return Ok(keys);
    }

    [HttpPost]
    public IActionResult CreateApiKey([FromBody] CreateApiKeyDto dto)
    {
        var newKey = new {
            Id = new Random().Next(10, 1000),
            Name = dto.Name,
            KeyPrefix = "pk_live_" + Guid.NewGuid().ToString().Substring(0, 8) + "...",
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = (DateTime?)null,
            Status = "Active"
        };
        return Ok(newKey);
    }

    [HttpDelete("{id}")]
    public IActionResult RevokeApiKey(int id)
    {
        return Ok(new { message = "API Key revoked successfully." });
    }
}

public class CreateApiKeyDto
{
    public string Name { get; set; } = null!;
}
