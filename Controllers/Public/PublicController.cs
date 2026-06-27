using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Controllers.Public;

[Route("api/public")]
[ApiController]
[AllowAnonymous]
public class PublicController : ControllerBase
{
    private readonly IPlanService _planService;
    private readonly ITenantService _tenantService;

    public PublicController(IPlanService planService, ITenantService tenantService)
    {
        _planService = planService;
        _tenantService = tenantService;
    }

    /// <summary>جلب الباقات المتاحة للعرض في صفحة الأسعار</summary>
    [HttpGet("plans")]
    public async Task<IActionResult> GetActivePlans()
    {
        var plans = await _planService.GetAllPlansAsync(includeInactive: false);
        return Ok(new { success = true, data = plans });
    }

    /// <summary>
    /// التحقق من توافر اسم النطاق الفرعي (Slug) قبل التسجيل
    /// GET /api/public/check-slug/{slug}
    /// </summary>
    [HttpGet("check-slug/{slug}")]
    public async Task<IActionResult> CheckSlugAvailability(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length < 3)
            return BadRequest(new { available = false, message = "الـ Slug يجب أن يكون 3 أحرف على الأقل." });

        // Validate format
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[a-z0-9][a-z0-9\-]{1,48}[a-z0-9]$"))
            return BadRequest(new { available = false, message = "الـ Slug يحتوي على أحرف غير مسموحة. استخدم أحرفاً إنجليزية صغيرة وأرقام وشرطات فقط." });

        // Reserved slugs
        var reserved = new[] { "admin", "api", "www", "app", "mail", "smtp", "raakiza", "sportive", "support", "help", "test", "staging", "dev" };
        if (System.Array.Exists(reserved, r => r == slug.ToLowerInvariant()))
            return Ok(new { available = false, message = "هذا الاسم محجوز ولا يمكن استخدامه." });

        var available = await _tenantService.IsSlugAvailableAsync(slug);
        return Ok(new
        {
            available,
            message = available ? "هذا الاسم متاح! ✓" : "هذا الاسم محجوز مسبقاً.",
            subdomain = available ? $"{slug.ToLowerInvariant()}.raakiza.com" : null
        });
    }

    /// <summary>
    /// تسجيل عميل جديد من الموقع التسويقي (Self-Onboarding)
    /// POST /api/public/self-register
    /// </summary>
    [HttpPost("self-register")]
    public async Task<IActionResult> SelfRegister([FromBody] SelfRegisterRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, errors = ModelState });

        var result = await _tenantService.SelfRegisterAsync(request);

        if (!result.Success)
            return BadRequest(new { success = false, message = result.Message });

        return Ok(new
        {
            success = true,
            message = result.Message,
            subdomain = result.Subdomain,
            adminEmail = result.AdminEmail
        });
    }
}
