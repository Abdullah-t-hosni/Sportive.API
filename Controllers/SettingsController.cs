using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<SettingsController> _logger;
    private readonly TimeService _timeService;

    public SettingsController(AppDbContext db, ILogger<SettingsController> logger, TimeService timeService)
    {
        _db = db;
        _logger = logger;
        _timeService = timeService;
    }

    // ══════════════════════════════════════════════════
    // GET /api/settings
    // جلب كل إعدادات المتجر العامة
    // ══════════════════════════════════════════════════
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get()
    {
        try
        {
            var info = await _db.StoreInfo.FirstOrDefaultAsync(x => x.StoreConfigId == 1);
            if (info == null)
            {
                info = new StoreInfo { StoreConfigId = 1 };
                _db.StoreInfo.Add(info);
                await _db.SaveChangesAsync();
            }
            return Ok(info);
        }
        catch (Exception ex)
        {
            // If database table is missing or connection fails, we return a default object
            // to prevent front-end crash (500 error), allowing the admin to see the UI.
            _logger.LogError(ex, "Settings GET failed, returning default object");
            return Ok(new StoreInfo { StoreConfigId = 1 });
        }
    }


    // ══════════════════════════════════════════════════
    // PUT /api/settings
    // تحديث الإعدادات (للمديرين فقط)
    // ══════════════════════════════════════════════════
    [HttpPut]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Update([FromBody] StoreInfo dto)
    {
        try 
        {
            var info = await _db.StoreInfo.FirstOrDefaultAsync(x => x.StoreConfigId == 1);
            if (info == null)
            {
                info = new StoreInfo { StoreConfigId = 1 };
                _db.StoreInfo.Add(info);
            }

            info.StoreBrandName          = dto.StoreBrandName;
            info.StoreSlogan             = dto.StoreSlogan;
            info.StorePhoneNo            = dto.StorePhoneNo;
            info.StoreWhatsAppNo         = dto.StoreWhatsAppNo;
            info.StoreEmailAddr          = dto.StoreEmailAddr;
            info.StorePhysicalAddr       = dto.StorePhysicalAddr;
            info.VatRatePercent          = dto.VatRatePercent;
            info.FixedDeliveryFee        = dto.FixedDeliveryFee;
            info.FreeDeliveryAt          = dto.FreeDeliveryAt;
            info.FacebookPage            = dto.FacebookPage;
            info.InstagramPage           = dto.InstagramPage;
            info.TikTokPage              = dto.TikTokPage;
            info.InMaintenance           = dto.InMaintenance;
            info.DeliveryAccountId       = dto.DeliveryAccountId;
            info.DeliveryRevenueAccountId = dto.DeliveryRevenueAccountId;
            info.StoreVatAccountId       = dto.StoreVatAccountId;
            info.BackupTime              = dto.BackupTime;
            info.BackupUtcOffset         = dto.BackupUtcOffset;
            info.LastUpdateDate          = TimeHelper.GetEgyptTime();

            if (!string.IsNullOrWhiteSpace(dto.TimeZoneId))
                info.TimeZoneId = dto.TimeZoneId;

            await _db.SaveChangesAsync();

            // إلغاء cache التوقيت فوراً حتى تنعكس التغييرات على الفور
            _timeService.InvalidateCache();

            return Ok(info);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Update error", error = ex.Message });
        }
    }
}
