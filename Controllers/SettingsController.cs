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
            info.OrderNumberPrefix       = dto.OrderNumberPrefix;
            info.CurrencyCode            = dto.CurrencyCode;
            info.CurrencySymbol          = dto.CurrencySymbol;
            info.LogoUrl                 = dto.LogoUrl;
            info.FaviconUrl              = dto.FaviconUrl;

            info.BrandColorH             = dto.BrandColorH;
            info.BrandColorS             = dto.BrandColorS;
            info.BrandColorL             = dto.BrandColorL;
            info.AnnouncementEnabled     = dto.AnnouncementEnabled;
            info.AnnouncementText        = dto.AnnouncementText;
            info.HeroTitle               = dto.HeroTitle;
            info.HeroSubtitle            = dto.HeroSubtitle;
            info.HeroImageUrl            = dto.HeroImageUrl;

            info.StorePhoneNo            = dto.StorePhoneNo;
            info.StoreWhatsAppNo         = dto.StoreWhatsAppNo;
            info.StoreEmailAddr          = dto.StoreEmailAddr;
            info.StorePhysicalAddr       = dto.StorePhysicalAddr;
            info.FacebookPage            = dto.FacebookPage;
            info.InstagramPage           = dto.InstagramPage;
            info.TikTokPage              = dto.TikTokPage;
            if (dto.YoutubeUrl != null)  info.YoutubeUrl = dto.YoutubeUrl;
            if (dto.TwitterUrl != null)  info.TwitterUrl = dto.TwitterUrl;

            info.MinOrderAmount          = dto.MinOrderAmount;
            info.AllowGuestCheckout      = dto.AllowGuestCheckout;
            info.EnableCoupons           = dto.EnableCoupons;
            info.EnableReviews           = dto.EnableReviews;
            info.ReviewsRequirePurchase  = dto.ReviewsRequirePurchase;
            info.AllowedPaymentMethods   = dto.AllowedPaymentMethods;
            info.ReceiptHeaderText       = dto.ReceiptHeaderText;
            info.ReceiptFooterText       = dto.ReceiptFooterText;
            info.ReceiptShowLogo         = dto.ReceiptShowLogo;
            info.ReceiptShowBarcode      = dto.ReceiptShowBarcode;
            info.ReceiptTermsAndConditions = dto.ReceiptTermsAndConditions;
            info.ReceiptShowCustomerDetails = dto.ReceiptShowCustomerDetails;
            info.ReceiptShowAddress      = dto.ReceiptShowAddress;
            info.ReceiptShowPhone        = dto.ReceiptShowPhone;
            info.ReceiptComplaintsPhone  = dto.ReceiptComplaintsPhone;
            info.ReceiptShowTotalPieceCount = dto.ReceiptShowTotalPieceCount;
            info.ReceiptShowItemCount    = dto.ReceiptShowItemCount;
            info.ReceiptShowBalance      = dto.ReceiptShowBalance;
            info.ReceiptSoftwareProvider = dto.ReceiptSoftwareProvider;

            info.VatRatePercent          = dto.VatRatePercent;
            info.FixedDeliveryFee        = dto.FixedDeliveryFee;
            info.FreeDeliveryAt          = dto.FreeDeliveryAt;
            info.DeliveryAccountId       = dto.DeliveryAccountId;
            info.DeliveryRevenueAccountId = dto.DeliveryRevenueAccountId;
            info.StoreVatAccountId       = dto.StoreVatAccountId;

            info.LowStockThreshold       = dto.LowStockThreshold;
            info.AllowBackorders         = dto.AllowBackorders;
            info.HideOutOfStock          = dto.HideOutOfStock;

            info.InMaintenance           = dto.InMaintenance;
            info.BackupTime              = dto.BackupTime;
            info.BackupUtcOffset         = dto.BackupUtcOffset;

            // New fields
            info.WhatsAppOrderTemplate    = dto.WhatsAppOrderTemplate;
            info.WhatsAppShippingTemplate = dto.WhatsAppShippingTemplate;
            info.WhatsAppReturnTemplate   = dto.WhatsAppReturnTemplate;
            info.OrderSuccessMessageAr    = dto.OrderSuccessMessageAr;
            info.OrderSuccessMessageEn    = dto.OrderSuccessMessageEn;
            info.AutoPrintReceipt         = dto.AutoPrintReceipt;
            info.ReceiptExtraCopies       = dto.ReceiptExtraCopies;
            info.GoogleAnalyticsId        = dto.GoogleAnalyticsId;
            info.FacebookPixelId          = dto.FacebookPixelId;
            info.SiteMetaDescriptionAr    = dto.SiteMetaDescriptionAr;
            info.SiteMetaDescriptionEn    = dto.SiteMetaDescriptionEn;
            info.SiteKeywords             = dto.SiteKeywords;
            info.BarcodeShowPrice         = dto.BarcodeShowPrice;
            info.BarcodeShowName          = dto.BarcodeShowName;
            info.BarcodeShowSize          = dto.BarcodeShowSize;
            info.BarcodeShowColor         = dto.BarcodeShowColor;

            info.ReceiptPaperSize         = dto.ReceiptPaperSize ?? "Receipt";
            info.ReceiptWidth             = dto.ReceiptWidth;
            info.ReceiptFontSize          = dto.ReceiptFontSize;
            info.ReceiptFontFamily        = dto.ReceiptFontFamily ?? "Alexandria";
            info.ReceiptLogoPosition      = dto.ReceiptLogoPosition ?? "center";
            info.ReceiptLogoWidth         = dto.ReceiptLogoWidth;
            info.OrderStatusAfterPrint    = dto.OrderStatusAfterPrint;
            info.QzReceiptPrinter         = dto.QzReceiptPrinter;
            info.QzA4Printer              = dto.QzA4Printer;
            info.QzBarcodePrinter         = dto.QzBarcodePrinter;
            info.ReceiptLineStyle         = dto.ReceiptLineStyle ?? "dashed";
            info.ReceiptDensity           = dto.ReceiptDensity;
            info.ReceiptBarcodeHeight     = dto.ReceiptBarcodeHeight;
            info.ReceiptSectionsOrder     = dto.ReceiptSectionsOrder ?? "header,order_info,items_table,totals_area,tafqeet,payment_info,footer_text,terms_conditions,barcode";

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
