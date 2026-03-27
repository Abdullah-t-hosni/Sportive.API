using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;

    public SettingsController(AppDbContext db)
    {
        _db = db;
    }

    // ══════════════════════════════════════════════════
    // GET /api/settings
    // جلب كل إعدادات المتجر العامة
    // ══════════════════════════════════════════════════
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get()
    {
        var info = await _db.StoreInfo.FirstOrDefaultAsync(x => x.Id == 1);
        if (info == null)
        {
            info = new StoreInfo { Id = 1 };
            _db.StoreInfo.Add(info);
            await _db.SaveChangesAsync();
        }
        return Ok(info);
    }

    // ══════════════════════════════════════════════════
    // PUT /api/settings
    // تحديث الإعدادات (للمديرين فقط)
    // ══════════════════════════════════════════════════
    [HttpPut]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Update([FromBody] StoreInfo dto)
    {
        var info = await _db.StoreInfo.FirstOrDefaultAsync(x => x.Id == 1);
        if (info == null) return NotFound();

        // تحديث الحقول
        info.StoreName = dto.StoreName;
        info.Slogan = dto.Slogan;
        info.Phone = dto.Phone;
        info.WhatsApp = dto.WhatsApp;
        info.Email = dto.Email;
        info.Address = dto.Address;
        info.VatPercent = dto.VatPercent;
        info.DeliveryFee = dto.DeliveryFee;
        info.FreeDeliveryThreshold = dto.FreeDeliveryThreshold;
        info.FacebookUrl = dto.FacebookUrl;
        info.InstagramUrl = dto.InstagramUrl;
        info.TikTokUrl = dto.TikTokUrl;
        info.IsMaintenanceMode = dto.IsMaintenanceMode;
        info.LastUpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(info);
    }
}
