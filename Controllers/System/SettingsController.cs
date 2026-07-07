using System.Security.Claims;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<SettingsController> _logger;
    private readonly TimeService _timeService;
    private readonly IAuditService _audit;
    private readonly MasterDbContext _masterDb;
    private readonly ITenantContext _tenantContext;

    public SettingsController(AppDbContext db, ILogger<SettingsController> logger, TimeService timeService, IAuditService audit, MasterDbContext masterDb, ITenantContext tenantContext)
    {
        _db = db;
        _logger = logger;
        _timeService = timeService;
        _audit = audit;
        _masterDb = masterDb;
        _tenantContext = tenantContext;
    }

    
    // GET /api/settings
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

            // Populate rich default templates if database values are empty or contain old simple defaults
            if (string.IsNullOrWhiteSpace(info.WhatsAppOrderTemplate) || info.WhatsAppOrderTemplate.Contains("تم تأكيد طلبك رقم #{orderNumber} في {storeName}"))
            {
                info.WhatsAppOrderTemplate = "أهلاً {customerName} 👋\n✅ *تم تأكيد طلبك بنجاح!*\n🔢 رقم الطلب: *{orderNumber}*\n📦 الطلب:\n{itemsList}\n💰 الإجمالي: *{totalAmount} ج.م*\n{discountPart}💳 الدفع: *{paymentMethod}*\n📍 النوع: *{fulfillmentType}*\nسنتواصل معك قريباً لتأكيد موعد التوصيل 🙏\n📄 الفاتورة الإلكترونية: {storeUrl}/invoice/{orderNumber}";
            }
            if (string.IsNullOrWhiteSpace(info.WhatsAppShippingTemplate) || info.WhatsAppShippingTemplate.Contains("تم شحن طلبك رقم #{orderNumber}! سيصلك خلال"))
            {
                info.WhatsAppShippingTemplate = "أهلاً {customerName} 🚚\n*طلبك رقم #{orderNumber} في الطريق إليك!*\n{trackingPart}⏱ الموعد المتوقع: خلال 2-3 أيام عمل\n📞 للاستفسار: {storePhone}";
            }
            if (string.IsNullOrWhiteSpace(info.WhatsAppReturnTemplate) || info.WhatsAppReturnTemplate.Contains("تم استلام طلب المرتجع الخاص بك رقم #{orderNumber}، وجاري مراجعته"))
            {
                info.WhatsAppReturnTemplate = "أهلاً {customerName} 🔄\n*تم استلام طلب المرتجع*\n🔢 رقم الطلب: *{orderNumber}*\n💰 المبلغ المسترد: *{totalAmount} ج.م*\nسيتم معالجة المبلغ خلال 3-5 أيام عمل 🙏\nنعتذر عن الإزعاج ونتمنى رؤيتك قريباً!";
            }
            if (string.IsNullOrWhiteSpace(info.WhatsAppProcessingTemplate) || info.WhatsAppProcessingTemplate.Contains("طلبك رقم #{orderNumber} قيد التجهيز الآن"))
            {
                info.WhatsAppProcessingTemplate = "أهلاً {customerName} 🎉\n*طلبك رقم #{orderNumber} جاهز للاستلام!*\n📍 العنوان: {storeAddress}\n🕐 مواعيد العمل: من 10 صباحاً لـ 10 مساءً يومياً\n📞 للتواصل: {storePhone}\nفي انتظارك! 💪";
            }
            if (string.IsNullOrWhiteSpace(info.WhatsAppPaymentReminderTemplate))
            {
                info.WhatsAppPaymentReminderTemplate = "أهلاً {customerName} 💬\nتذكير بخصوص طلبك *#{orderNumber}*\n💰 المبلغ المتبقي: *{totalAmount} ج.م*\n💳 طرق الدفع: كاش / فودافون كاش / انستاباي\nللسداد أو الاستفسار تواصل معنا:\n{storePhone}\nشكراً لتسوقك معنا 🙏";
            }
            if (string.IsNullOrWhiteSpace(info.WhatsAppPosOrderTemplate))
            {
                info.WhatsAppPosOrderTemplate = "مرحباً {customerName} 👋\n\nشكراً لتسوّقك معنا في {storeName} 🏃‍♂️\nفاتورتك جاهزة!\n\n🧾 رقم الفاتورة: *{orderNumber}*\n\n📲 لعرض الفاتورة الإلكترونية أو تحميلها:\n{invoiceUrl}\n\nنتمنى لك تجربة ممتازة دائماً 💪";
            }
            if (string.IsNullOrWhiteSpace(info.WhatsAppPayrollTemplate))
            {
                info.WhatsAppPayrollTemplate = "السلام عليكم ورحمة الله وبركاته،\n\nيسعدنا مشاركة تفاصيل راتب شهر {periodMonth} لعام {periodYear} معكم.\n\n👤 الموظف: {employeeName}\n💵 صافي الراتب المستحق: {netPayable}\n\nللاطلاع على تفاصيل الراتب كاملة وتحميل قسيمة الراتب كـ PDF، يرجى الضغط على الرابط التالي:\n{payslipUrl}\n\nشكرًا لجهودكم المميزة،\nإدارة الموارد البشرية - {storeName}";
            }

            // Fetch active subscription expiration
            if (_tenantContext.CurrentTenant != null)
            {
                var activeSubscription = await _masterDb.TenantSubscriptions
                    .Where(ts => ts.TenantGuid == _tenantContext.CurrentTenant.TenantGuid && ts.IsActive)
                    .OrderByDescending(ts => ts.ExpiresAt)
                    .FirstOrDefaultAsync();

                if (activeSubscription != null)
                {
                    info.SubscriptionExpiresAt = activeSubscription.ExpiresAt;
                    info.SubscriptionGraceDays = activeSubscription.GracePeriodDays;
                }
            }

            return Ok(info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Settings GET failed");
            return StatusCode(500, new { message = "فشل تحميل الإعدادات", error = ex.Message });
        }
    }


    
    // PUT /api/settings
    [HttpPut]
    [RequirePermission(ModuleKeys.Settings, requireEdit: true)]
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
            info.AnnouncementBgColor     = dto.AnnouncementBgColor;
            info.AnnouncementTextColor   = dto.AnnouncementTextColor;
            info.AnnouncementFontSize    = dto.AnnouncementFontSize;
            info.PromoPopupEnabled       = dto.PromoPopupEnabled;
            info.PromoPopupTitle         = dto.PromoPopupTitle;
            info.PromoPopupText          = dto.PromoPopupText;
            info.PromoPopupImageUrl      = dto.PromoPopupImageUrl;
            info.PromoPopupCoupon        = dto.PromoPopupCoupon;
            info.HeroTitle               = dto.HeroTitle;
            info.HeroSubtitle            = dto.HeroSubtitle;
            info.HeroImageUrl            = dto.HeroImageUrl;
            info.HomeCarouselImagesTop   = dto.HomeCarouselImagesTop;
            info.HomeCarouselImagesBottom = dto.HomeCarouselImagesBottom;
            info.HomeFeaturedCategories  = dto.HomeFeaturedCategories;
            info.HomeCategoryMenImage    = dto.HomeCategoryMenImage;
            info.HomeCategoryWomenImage  = dto.HomeCategoryWomenImage;
            info.HomeCategoryKidsImage   = dto.HomeCategoryKidsImage;
            info.HomeCategoryEquipmentImage = dto.HomeCategoryEquipmentImage;
            info.HomeCategorySpecialSizesImage = dto.HomeCategorySpecialSizesImage;
            info.ShowHeroSection         = dto.ShowHeroSection;
            info.UseGlassmorphism        = dto.UseGlassmorphism;
            info.EnablePageTransitions   = dto.EnablePageTransitions;
            info.EnableHoverEffects      = dto.EnableHoverEffects;

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
            info.EnableUrgencyTags       = dto.EnableUrgencyTags;
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
            info.ReceiptShowTime         = dto.ReceiptShowTime;
            info.ReceiptShowSKU          = dto.ReceiptShowSKU;
            info.ReceiptSoftwareProvider = dto.ReceiptSoftwareProvider;
            info.TaxNumber               = dto.TaxNumber;
            info.CommercialRegister      = dto.CommercialRegister;
            info.ReceiptShowTax          = dto.ReceiptShowTax;
            info.ReceiptShowUnitPrice    = dto.ReceiptShowUnitPrice;
            info.ReceiptShowDiscount     = dto.ReceiptShowDiscount;
            info.ReceiptShowCashier      = dto.ReceiptShowCashier;
            info.ReceiptShowNote         = dto.ReceiptShowNote;
            // Removed redundant AutoPrintReceipt and ReceiptExtraCopies assignments from here as they are handled below
            info.OrderStatusAfterPrint   = dto.OrderStatusAfterPrint;
            info.ReceiptLogoWidth        = dto.ReceiptLogoWidth;
            info.ReceiptLogoPosition     = dto.ReceiptLogoPosition;
            info.ReceiptFontFamily       = dto.ReceiptFontFamily;
            info.ReceiptLineStyle        = dto.ReceiptLineStyle;
            info.ReceiptPaperSize        = dto.ReceiptPaperSize;
            info.ReceiptDensity          = dto.ReceiptDensity;
            info.ReceiptWidth            = dto.ReceiptWidth;
            info.ReceiptFontSize         = dto.ReceiptFontSize;
            info.ReceiptBarcodeHeight    = dto.ReceiptBarcodeHeight;
            info.ReceiptSectionsOrder    = dto.ReceiptSectionsOrder;

            info.VatRatePercent          = dto.VatRatePercent;
            info.FixedDeliveryFee        = dto.FixedDeliveryFee;
            info.FreeDeliveryAt          = dto.FreeDeliveryAt;
            info.DeliveryAccountId       = dto.DeliveryAccountId;
            info.DeliveryRevenueAccountId = dto.DeliveryRevenueAccountId;
            info.StoreVatAccountId       = dto.StoreVatAccountId;

            info.LowStockThreshold       = dto.LowStockThreshold;
            info.AllowBackorders         = dto.AllowBackorders;
            info.HideOutOfStock          = dto.HideOutOfStock;

            info.WebsiteWarehouseId      = dto.WebsiteWarehouseId;
            info.InMaintenance           = dto.InMaintenance;
            info.BackupTime              = dto.BackupTime;
            info.BackupUtcOffset         = dto.BackupUtcOffset;
            info.ResendApiKey            = dto.ResendApiKey;
            info.BusinessDayEndHour      = dto.BusinessDayEndHour;

            // New fields
            info.WhatsAppOrderTemplate    = dto.WhatsAppOrderTemplate;
            info.WhatsAppShippingTemplate = dto.WhatsAppShippingTemplate;
            info.WhatsAppReturnTemplate   = dto.WhatsAppReturnTemplate;
            info.WhatsAppProcessingTemplate = dto.WhatsAppProcessingTemplate;
            info.WhatsAppDeliveredTemplate  = dto.WhatsAppDeliveredTemplate;
            info.WhatsAppCancelTemplate     = dto.WhatsAppCancelTemplate;
            info.WhatsAppWebsiteConfirmTemplate = dto.WhatsAppWebsiteConfirmTemplate;
            info.WhatsAppPaymentReminderTemplate = dto.WhatsAppPaymentReminderTemplate;
            info.WhatsAppPosOrderTemplate = dto.WhatsAppPosOrderTemplate;
            info.WhatsAppPayrollTemplate = dto.WhatsAppPayrollTemplate;

            // Wapilot WhatsApp API
            info.WapilotApiKey           = dto.WapilotApiKey;
            info.WapilotPosInstanceId    = dto.WapilotPosInstanceId;
            info.WapilotWebInstanceId    = dto.WapilotWebInstanceId;
            info.AutoSendWhatsAppInvoices = dto.AutoSendWhatsAppInvoices;

            // E-Invoicing
            info.TaxAuthorityType = dto.TaxAuthorityType;
            info.EtaTaxNumber = dto.EtaTaxNumber;
            info.EtaClientId = dto.EtaClientId;
            info.EtaClientSecret = dto.EtaClientSecret;
            info.EtaEnvironment = dto.EtaEnvironment;
            info.ZatcaEnvironment = dto.ZatcaEnvironment;
            info.ZatcaTaxNumber = dto.ZatcaTaxNumber;
            info.ZatcaCertificate = dto.ZatcaCertificate;
            info.WhatsAppInstallmentFriendlyTemplate = dto.WhatsAppInstallmentFriendlyTemplate;
            info.WhatsAppInstallmentNoticeTemplate   = dto.WhatsAppInstallmentNoticeTemplate;
            info.WhatsAppInstallmentWarningTemplate  = dto.WhatsAppInstallmentWarningTemplate;
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
            info.BarcodeShowStoreName     = dto.BarcodeShowStoreName;
            info.BarcodeLabelWidth        = dto.BarcodeLabelWidth;
            info.BarcodeLabelHeight       = dto.BarcodeLabelHeight;
                        info.BarcodeSvgWidth         = dto.BarcodeSvgWidth;
            info.BarcodeSvgHeight        = dto.BarcodeSvgHeight;
                        info.BarcodeMarginTop        = dto.BarcodeMarginTop;
            info.BarcodeMarginLeft       = dto.BarcodeMarginLeft;
            info.BarcodeMarginRight      = dto.BarcodeMarginRight;
            info.BarcodeMarginBottom     = dto.BarcodeMarginBottom;
            info.BarcodePaddingTop       = dto.BarcodePaddingTop;
            info.BarcodePaddingLeft      = dto.BarcodePaddingLeft;
            info.BarcodePaddingRight     = dto.BarcodePaddingRight;
            info.BarcodePaddingBottom= dto.BarcodePaddingBottom;
            info.BarcodeDirection = dto.BarcodeDirection;
            info.BarcodeRotation = dto.BarcodeRotation;
            info.BarcodeStoreFontSize    = dto.BarcodeStoreFontSize;
            info.BarcodeNameFontSize     = dto.BarcodeNameFontSize;
            info.BarcodePriceFontSize    = dto.BarcodePriceFontSize;
            info.BarcodeVariantFontSize  = dto.BarcodeVariantFontSize;
            info.BarcodeCodeFontSize     = dto.BarcodeCodeFontSize;

            info.ReceiptPaperSize         = dto.ReceiptPaperSize ?? "Receipt";
            info.EnablePOS                = dto.EnablePOS;
            info.EnableECommerce          = dto.EnableECommerce;
            info.EnableHR                 = dto.EnableHR;
            info.EnableFixedAssets        = dto.EnableFixedAssets;
            info.EnableAccounting         = dto.EnableAccounting;
            info.DefaultLanguage          = dto.DefaultLanguage ?? "ar";
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

            info.ReceiptShowRecipientSignature = dto.ReceiptShowRecipientSignature;
            info.ReceiptShowStoreSeal          = dto.ReceiptShowStoreSeal;
            info.ReceiptStoreNameFontSize      = dto.ReceiptStoreNameFontSize;
            info.ReceiptHeaderFontSize         = dto.ReceiptHeaderFontSize;
            info.ReceiptItemsFontSize          = dto.ReceiptItemsFontSize;
            info.ReceiptTotalsFontSize         = dto.ReceiptTotalsFontSize;
            info.ReceiptFooterFontSize         = dto.ReceiptFooterFontSize;

            info.AccountingLockDate      = dto.AccountingLockDate;
            info.LinktreeConfig          = dto.LinktreeConfig;
            info.DailyTarget             = dto.DailyTarget;
            info.LastUpdateDate          = TimeHelper.GetEgyptTime();

            info.EnableGraduatedDelayPolicy = dto.EnableGraduatedDelayPolicy;
            info.DelayGraceMinutes       = dto.DelayGraceMinutes;
            info.DelayQuarterDayLimitMinutes = dto.DelayQuarterDayLimitMinutes;
            info.DelayHalfDayLimitMinutes = dto.DelayHalfDayLimitMinutes;

            if (!string.IsNullOrWhiteSpace(dto.TimeZoneId))
                info.TimeZoneId = dto.TimeZoneId;

            await _db.SaveChangesAsync();

            // cache 
            _timeService.InvalidateCache();
            
            try { await _audit.LogAsync("UpdateSettings", "StoreInfo", "1", $"Updated store settings", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

            return Ok(info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Settings Update failed");
            var innerMsg = ex.InnerException != null ? $" | Inner: {ex.InnerException.Message}" : "";
            return StatusCode(500, new { message = "Update error", error = ex.Message + innerMsg });
        }
    }
}





