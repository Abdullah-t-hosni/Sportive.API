using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Sportive.API.Models;

public enum TaxAuthorityType
{
    None = 0,
    EgyptETA = 1,
    SaudiZATCA = 2
}

[Table("StoreSettings")]
public class StoreInfo
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [JsonPropertyName("id")]
    public int StoreConfigId { get; set; } = 1;
    
    // --- 1. Brand & Identity ---
    [MaxLength(100)]
    [JsonPropertyName("storeName")]
    public string StoreBrandName { get; set; } = "Sportive";
    
    [MaxLength(200)]
    [JsonPropertyName("slogan")]
    public string StoreSlogan { get; set; } = "Beyond Performance";

    [MaxLength(10)]
    [JsonPropertyName("orderNumberPrefix")]
    public string OrderNumberPrefix { get; set; } = "SPT";

    [MaxLength(10)]
    [JsonPropertyName("defaultLanguage")]
    public string DefaultLanguage { get; set; } = "ar";

    [MaxLength(10)]
    [JsonPropertyName("currencyCode")]
    public string CurrencyCode { get; set; } = "EGP";

    [MaxLength(10)]
    [JsonPropertyName("currencySymbol")]
    public string CurrencySymbol { get; set; } = "ج.م";

    [MaxLength(500)]
    [JsonPropertyName("logoUrl")]
    public string? LogoUrl { get; set; }

    [MaxLength(500)]
    [JsonPropertyName("faviconUrl")]
    public string? FaviconUrl { get; set; }

    // --- 2. Appearance & UI ---
    [JsonPropertyName("brandColorH")]
    public int BrandColorH { get; set; } = 221; // Blue Hue

    [JsonPropertyName("brandColorS")]
    public int BrandColorS { get; set; } = 83; // Saturation

    [JsonPropertyName("brandColorL")]
    public int BrandColorL { get; set; } = 53; // Lightness

    [JsonPropertyName("announcementEnabled")]
    public bool AnnouncementEnabled { get; set; } = false;

    [JsonPropertyName("showHeroSection")]
    public bool ShowHeroSection { get; set; } = true;

    [JsonPropertyName("useGlassmorphism")]
    public bool UseGlassmorphism { get; set; } = false;

    [JsonPropertyName("enablePageTransitions")]
    public bool EnablePageTransitions { get; set; } = true;

    [JsonPropertyName("enableHoverEffects")]
    public bool EnableHoverEffects { get; set; } = true;

    [MaxLength(500)]
    [JsonPropertyName("announcementText")]
    public string? AnnouncementText { get; set; }

    [JsonPropertyName("announcementBgColor")]
    public string? AnnouncementBgColor { get; set; }

    [JsonPropertyName("announcementTextColor")]
    public string? AnnouncementTextColor { get; set; }

    [JsonPropertyName("announcementFontSize")]
    public string? AnnouncementFontSize { get; set; }

    // --- Promo Popup ---
    [JsonPropertyName("promoPopupEnabled")]
    public bool PromoPopupEnabled { get; set; } = false;

    [MaxLength(200)]
    [JsonPropertyName("promoPopupTitle")]
    public string? PromoPopupTitle { get; set; }

    [MaxLength(1000)]
    [JsonPropertyName("promoPopupText")]
    public string? PromoPopupText { get; set; }

    [MaxLength(50)]
    [JsonPropertyName("promoPopupCoupon")]
    public string? PromoPopupCoupon { get; set; }

    [MaxLength(500)]
    [JsonPropertyName("promoPopupImageUrl")]
    public string? PromoPopupImageUrl { get; set; }

    [MaxLength(200)]
    [JsonPropertyName("heroTitle")]
    public string? HeroTitle { get; set; }

    [MaxLength(500)]
    [JsonPropertyName("heroSubtitle")]
    public string? HeroSubtitle { get; set; }

    [MaxLength(1000)]
    [JsonPropertyName("heroImageUrl")]
    public string? HeroImageUrl { get; set; }

    [MaxLength(3000)]
    [JsonPropertyName("homeCarouselImagesTop")]
    public string? HomeCarouselImagesTop { get; set; } // JSON array of string URLs

    [MaxLength(3000)]
    [JsonPropertyName("homeCarouselImagesBottom")]
    public string? HomeCarouselImagesBottom { get; set; } // JSON array of string URLs

    [MaxLength(1000)]
    [JsonPropertyName("homeFeaturedCategories")]
    public string? HomeFeaturedCategories { get; set; } // JSON array of integers (category IDs)

    [MaxLength(1000)]
    [JsonPropertyName("homeCategoryMenImage")]
    public string? HomeCategoryMenImage { get; set; }

    [MaxLength(1000)]
    [JsonPropertyName("homeCategoryWomenImage")]
    public string? HomeCategoryWomenImage { get; set; }

    [MaxLength(1000)]
    [JsonPropertyName("homeCategoryKidsImage")]
    public string? HomeCategoryKidsImage { get; set; }

    [MaxLength(1000)]
    [JsonPropertyName("homeCategoryEquipmentImage")]
    public string? HomeCategoryEquipmentImage { get; set; }

    [MaxLength(1000)]
    [JsonPropertyName("homeCategorySpecialSizesImage")]
    public string? HomeCategorySpecialSizesImage { get; set; }

    // --- 3. Contact Information ---
    [MaxLength(20)]
    [JsonPropertyName("phone")]
    public string StorePhoneNo { get; set; } = "";
    
    [MaxLength(20)]
    [JsonPropertyName("whatsApp")]
    public string StoreWhatsAppNo { get; set; } = "";
    
    [MaxLength(100)]
    [JsonPropertyName("email")]
    public string StoreEmailAddr { get; set; } = "";
    
    [MaxLength(500)]
    [JsonPropertyName("address")]
    public string StorePhysicalAddr { get; set; } = "";
    
    // Social Media
    [MaxLength(500)]
    [JsonPropertyName("facebookUrl")]
    public string FacebookPage { get; set; } = "";
    
    [MaxLength(500)]
    [JsonPropertyName("instagramUrl")]
    public string InstagramPage { get; set; } = "";
    
    [MaxLength(500)]
    [JsonPropertyName("tikTokUrl")]
    public string TikTokPage { get; set; } = "";

    [MaxLength(500)]
    [JsonPropertyName("youtubeUrl")]
    public string? YoutubeUrl { get; set; }

    [MaxLength(500)]
    [JsonPropertyName("twitterUrl")]
    public string? TwitterUrl { get; set; }

    // --- 4. Sales & Orders ---
    [JsonPropertyName("minOrderAmount")]
    public decimal MinOrderAmount { get; set; } = 0;

    [JsonPropertyName("allowGuestCheckout")]
    public bool AllowGuestCheckout { get; set; } = true;

    [JsonPropertyName("enableCoupons")]
    public bool EnableCoupons { get; set; } = true;

    [JsonPropertyName("enableReviews")]
    public bool EnableReviews { get; set; } = true;

    [JsonPropertyName("reviewsRequirePurchase")]
    public bool ReviewsRequirePurchase { get; set; } = true;

    [JsonPropertyName("enableUrgencyTags")]
    public bool EnableUrgencyTags { get; set; } = true;

    [MaxLength(500)]
    [JsonPropertyName("allowedPaymentMethods")]
    public string AllowedPaymentMethods { get; set; } = "Cash,Vodafone,InstaPay";

    // مخزن الموقع الإلكتروني - يُستخدم لخصم مبيعات الموقع منه تلقائياً
    [JsonPropertyName("websiteWarehouseId")]
    public int? WebsiteWarehouseId { get; set; }

    [MaxLength(50)]
    [JsonPropertyName("taxNumber")]
    public string? TaxNumber { get; set; }

    [MaxLength(50)]
    [JsonPropertyName("commercialRegister")]
    public string? CommercialRegister { get; set; }

    [JsonPropertyName("receiptHeaderText")]
    public string? ReceiptHeaderText { get; set; }

    [JsonPropertyName("receiptFooterText")]
    public string? ReceiptFooterText { get; set; }

    [JsonPropertyName("receiptShowLogo")]
    public bool ReceiptShowLogo { get; set; } = true;

    [JsonPropertyName("receiptShowBarcode")]
    public bool ReceiptShowBarcode { get; set; } = true;

    [MaxLength(2000)]
    [JsonPropertyName("receiptTermsAndConditions")]
    public string? ReceiptTermsAndConditions { get; set; }

    [JsonPropertyName("receiptShowCustomerDetails")]
    public bool ReceiptShowCustomerDetails { get; set; } = true;

    [JsonPropertyName("receiptShowAddress")]
    public bool ReceiptShowAddress { get; set; } = true;

    [JsonPropertyName("receiptShowPhone")]
    public bool ReceiptShowPhone { get; set; } = true;

    [MaxLength(50)]
    [JsonPropertyName("receiptComplaintsPhone")]
    public string? ReceiptComplaintsPhone { get; set; }

    [JsonPropertyName("receiptShowTotalPieceCount")]
    public bool ReceiptShowTotalPieceCount { get; set; } = true;

    [JsonPropertyName("receiptShowItemCount")]
    public bool ReceiptShowItemCount { get; set; } = true;

    [JsonPropertyName("receiptShowBalance")]
    public bool ReceiptShowBalance { get; set; } = true;

    [JsonPropertyName("receiptShowTime")]
    public bool ReceiptShowTime { get; set; } = true;

    [JsonPropertyName("receiptShowSKU")]
    public bool ReceiptShowSKU { get; set; } = true;

    [JsonPropertyName("receiptShowTax")]
    public bool ReceiptShowTax { get; set; } = true;

    [JsonPropertyName("receiptShowUnitPrice")]
    public bool ReceiptShowUnitPrice { get; set; } = true;

    [JsonPropertyName("receiptShowDiscount")]
    public bool ReceiptShowDiscount { get; set; } = true;

    [JsonPropertyName("receiptShowCashier")]
    public bool ReceiptShowCashier { get; set; } = true;

    [JsonPropertyName("receiptShowNote")]
    public bool ReceiptShowNote { get; set; } = true;

    [MaxLength(200)]
    [JsonPropertyName("receiptSoftwareProvider")]
    public string? ReceiptSoftwareProvider { get; set; } = "Eng.Abdullah-Taha";

    [MaxLength(20)]
    [JsonPropertyName("receiptPaperSize")]
    public string ReceiptPaperSize { get; set; } = "Receipt";

    [JsonPropertyName("enablePOS")]
    public bool EnablePOS { get; set; } = true;

    [JsonPropertyName("enableECommerce")]
    public bool EnableECommerce { get; set; } = true;

    [JsonPropertyName("enableHR")]
    public bool EnableHR { get; set; } = true;

    [JsonPropertyName("enableFixedAssets")]
    public bool EnableFixedAssets { get; set; } = true;

    [JsonPropertyName("enableAccounting")]
    public bool EnableAccounting { get; set; } = true;

    [JsonPropertyName("receiptWidth")]
    public int ReceiptWidth { get; set; } = 80;

    [JsonPropertyName("receiptFontSize")]
    public int ReceiptFontSize { get; set; } = 11;

    [MaxLength(50)]
    [JsonPropertyName("receiptFontFamily")]
    public string ReceiptFontFamily { get; set; } = "Alexandria";

    [MaxLength(20)]
    [JsonPropertyName("receiptLogoPosition")]
    public string ReceiptLogoPosition { get; set; } = "center";

    [JsonPropertyName("receiptLogoWidth")]
    public int ReceiptLogoWidth { get; set; } = 80;

    [MaxLength(50)]
    [JsonPropertyName("orderStatusAfterPrint")]
    public string? OrderStatusAfterPrint { get; set; }

    [JsonPropertyName("autoPrintReceipt")]
    public bool AutoPrintReceipt { get; set; } = false;

    [JsonPropertyName("receiptExtraCopies")]
    public int ReceiptExtraCopies { get; set; } = 0;

    [MaxLength(100)]
    [JsonPropertyName("qzReceiptPrinter")]
    public string? QzReceiptPrinter { get; set; }

    [MaxLength(100)]
    [JsonPropertyName("qzA4Printer")]
    public string? QzA4Printer { get; set; }

    [MaxLength(100)]
    [JsonPropertyName("qzBarcodePrinter")]
    public string? QzBarcodePrinter { get; set; }

    [MaxLength(10)]
    [JsonPropertyName("receiptLineStyle")]
    public string ReceiptLineStyle { get; set; } = "dashed";

    [JsonPropertyName("receiptDensity")]
    public double ReceiptDensity { get; set; } = 1.4;

    [JsonPropertyName("receiptBarcodeHeight")]
    public int ReceiptBarcodeHeight { get; set; } = 10;

    [MaxLength(1000)]
    [JsonPropertyName("receiptSectionsOrder")]
    public string ReceiptSectionsOrder { get; set; } = "header,order_info,items_table,totals_area,tafqeet,payment_info,customer_signature,footer_text,terms_conditions,barcode";

    [JsonPropertyName("receiptShowRecipientSignature")]
    public bool ReceiptShowRecipientSignature { get; set; } = true;

    [JsonPropertyName("receiptShowStoreSeal")]
    public bool ReceiptShowStoreSeal { get; set; } = true;

    [JsonPropertyName("receiptStoreNameFontSize")]
    public int ReceiptStoreNameFontSize { get; set; } = 24;

    [JsonPropertyName("receiptHeaderFontSize")]
    public int ReceiptHeaderFontSize { get; set; } = 11;

    [JsonPropertyName("receiptItemsFontSize")]
    public int ReceiptItemsFontSize { get; set; } = 11;

    [JsonPropertyName("receiptTotalsFontSize")]
    public int ReceiptTotalsFontSize { get; set; } = 11;

    [JsonPropertyName("receiptFooterFontSize")]
    public int ReceiptFooterFontSize { get; set; } = 10;

    // --- 5. Finance & VAT ---
    [JsonPropertyName("vatRatePercent")]
    public decimal VatRatePercent { get; set; } = 14;
    
    [JsonPropertyName("fixedDeliveryFee")]
    public decimal FixedDeliveryFee { get; set; } = 50;
    
    [JsonPropertyName("freeDeliveryAt")]
    public decimal FreeDeliveryAt { get; set; } = 2000;
    
    [JsonPropertyName("deliveryAccountId")]
    public string? DeliveryAccountId { get; set; } 

    [JsonPropertyName("deliveryRevenueAccountId")]
    public string? DeliveryRevenueAccountId { get; set; } 

    [JsonPropertyName("storeVatAccountId")]
    public string? StoreVatAccountId { get; set; } 

    // --- 6. Inventory Logic ---
    [JsonPropertyName("lowStockThreshold")]
    public int LowStockThreshold { get; set; } = 5;

    [JsonPropertyName("allowBackorders")]
    public bool AllowBackorders { get; set; } = false;

    [JsonPropertyName("hideOutOfStock")]
    public bool HideOutOfStock { get; set; } = false;
    
    // --- 7. System & Maintenance ---
    [JsonPropertyName("inMaintenance")]
    public bool InMaintenance { get; set; } = false;

    [MaxLength(10)]
    [JsonPropertyName("backupTime")]
    public string BackupTime { get; set; } = "02:00"; 

    [JsonPropertyName("backupUtcOffset")]
    public int BackupUtcOffset { get; set; } = 2; 

    [MaxLength(100)]
    [JsonPropertyName("timeZoneId")]
    public string TimeZoneId { get; set; } = "Egypt Standard Time";

    [JsonPropertyName("accountingLockDate")]
    public DateTime? AccountingLockDate { get; set; }

    [MaxLength(200)]
    [JsonPropertyName("resendApiKey")]
    public string? ResendApiKey { get; set; }

    [JsonPropertyName("businessDayEndHour")]
    public int? BusinessDayEndHour { get; set; }

    // --- 8. WhatsApp Quick Replies ---
    [JsonPropertyName("whatsAppOrderTemplate")]
    public string? WhatsAppOrderTemplate { get; set; } = "أهلاً {customerName} 👋\n✅ *تم تأكيد طلبك بنجاح!*\n🔢 رقم الطلب: *{orderNumber}*\n📦 الطلب:\n{itemsList}\n💰 الإجمالي: *{totalAmount} ج.م*\n{discountPart}💳 الدفع: *{paymentMethod}*\n📍 النوع: *{fulfillmentType}*\nسنتواصل معك قريباً لتأكيد موعد التوصيل 🙏\n📄 الفاتورة الإلكترونية: {storeUrl}/invoice/{orderNumber}";

    [JsonPropertyName("whatsAppShippingTemplate")]
    public string? WhatsAppShippingTemplate { get; set; } = "أهلاً {customerName} 🚚\n*طلبك رقم #{orderNumber} في الطريق إليك!*\n{trackingPart}⏱ الموعد المتوقع: خلال 2-3 أيام عمل\n📞 للاستفسار: {storePhone}";

    [JsonPropertyName("whatsAppReturnTemplate")]
    public string? WhatsAppReturnTemplate { get; set; } = "أهلاً {customerName} 🔄\n*تم استلام طلب المرتجع*\n🔢 رقم الطلب: *{orderNumber}*\n💰 المبلغ المسترد: *{totalAmount} ج.م*\nسيتم معالجة المبلغ خلال 3-5 أيام عمل 🙏\nنعتذر عن الإزعاج ونتمنى رؤيتك قريباً!";

    [JsonPropertyName("whatsAppProcessingTemplate")]
    public string? WhatsAppProcessingTemplate { get; set; } = "أهلاً {customerName} 🎉\n*طلبك رقم #{orderNumber} جاهز للاستلام!*\n📍 العنوان: {storeAddress}\n🕐 مواعيد العمل: من 10 صباحاً لـ 10 مساءً يومياً\n📞 للتواصل: {storePhone}\nفي انتظارك! 💪";

    [JsonPropertyName("whatsAppDeliveredTemplate")]
    public string? WhatsAppDeliveredTemplate { get; set; } = "🎉 تم توصيل طلبك رقم #{orderNumber} بنجاح! نتمنى أن ينال إعجابك. {storeName}";

    [JsonPropertyName("whatsAppCancelTemplate")]
    public string? WhatsAppCancelTemplate { get; set; } = "❌ تم إلغاء طلبك رقم #{orderNumber}. للاستفسار تواصل معنا.";

    [JsonPropertyName("whatsAppWebsiteConfirmTemplate")]
    public string? WhatsAppWebsiteConfirmTemplate { get; set; } = "🛍️ *طلب جديد - {storeName}*\nرقم الطلب: {orderNumber}\n─────────────────\n{itemsList}\n─────────────────\nالإجمالي: *{totalAmount}*\n\nتم تأكيد الطلب بنجاح! ✅";

    [JsonPropertyName("whatsAppPaymentReminderTemplate")]
    public string? WhatsAppPaymentReminderTemplate { get; set; } = "أهلاً {customerName} 💬\nتذكير بخصوص طلبك *#{orderNumber}*\n💰 المبلغ المتبقي: *{totalAmount} ج.م*\n💳 طرق الدفع: كاش / فودافون كاش / انستاباي\nللسداد أو الاستفسار تواصل معنا:\n{storePhone}\nشكراً لتسوقك معنا 🙏";

    [JsonPropertyName("whatsAppPosOrderTemplate")]
    public string? WhatsAppPosOrderTemplate { get; set; } = "مرحباً {customerName} 👋\n\nشكراً لتسوّقك معنا في {storeName} 🏃‍♂️\nفاتورتك جاهزة!\n\n🧾 رقم الفاتورة: *{orderNumber}*\n\n📲 لعرض الفاتورة الإلكترونية أو تحميلها:\n{invoiceUrl}\n\nنتمنى لك تجربة ممتازة دائماً 💪";

    [JsonPropertyName("whatsAppPayrollTemplate")]
    public string? WhatsAppPayrollTemplate { get; set; } = "السلام عليكم ورحمة الله وبركاته،\n\nيسعدنا مشاركة تفاصيل راتب شهر {periodMonth} لعام {periodYear} معكم.\n\n👤 الموظف: {employeeName}\n💵 صافي الراتب المستحق: {netPayable}\n\nللاطلاع على تفاصيل الراتب كاملة وتحميل قسيمة الراتب كـ PDF، يرجى الضغط على الرابط التالي:\n{payslipUrl}\n\nشكرًا لجهودكم المميزة،\nإدارة الموارد البشرية - {storeName}";

    [JsonPropertyName("whatsAppInstallmentFriendlyTemplate")]
    public string? WhatsAppInstallmentFriendlyTemplate { get; set; } = "مرحباً {name}، تذكير ودي بقيمة الأقساط المستحقة بقيمة {remaining} ج.م. نتمنى لك يوماً سعيداً.";

    [JsonPropertyName("whatsAppInstallmentNoticeTemplate")]
    public string? WhatsAppInstallmentNoticeTemplate { get; set; } = "عزيزي {name}، نود تذكيرك بأن إجمالي مستحقات الأقساط بقيمة {remaining} ج.م يستحق أقرب موعد لها بتاريخ {formattedDueDate}. يرجى التكرم بالسداد في الموعد المحدد. شكراً لك.";

    [JsonPropertyName("whatsAppInstallmentWarningTemplate")]
    public string? WhatsAppInstallmentWarningTemplate { get; set; } = "تنبيه عاجل: العميل {name}، نود إعلامك بتأخر سداد قسط مستحق بقيمة {remaining} ج.م (تاريخ الاستحقاق: {formattedDueDate}). يرجى السداد فوراً أو التواصل معنا لتسوية المديونية.";

    // --- 9. Order Success Page ---
    [JsonPropertyName("orderSuccessMessageAr")]
    public string? OrderSuccessMessageAr { get; set; } = "شكراً لتسوقك معنا! سيقوم فريقنا بالتواصل معك قريباً لتأكيد الطلب.";

    [JsonPropertyName("orderSuccessMessageEn")]
    public string? OrderSuccessMessageEn { get; set; } = "Thank you for shopping with us! Our team will contact you soon to confirm your order.";


    // --- 11. SEO & Analytics ---
    [JsonPropertyName("googleAnalyticsId")]
    public string? GoogleAnalyticsId { get; set; }

    [JsonPropertyName("facebookPixelId")]
    public string? FacebookPixelId { get; set; }

    [JsonPropertyName("siteMetaDescriptionAr")]
    public string? SiteMetaDescriptionAr { get; set; }

    [JsonPropertyName("siteMetaDescriptionEn")]
    public string? SiteMetaDescriptionEn { get; set; }

    [JsonPropertyName("siteKeywords")]
    public string? SiteKeywords { get; set; }

    // --- 12. Barcode Customization ---
    [JsonPropertyName("barcodeShowPrice")]
    public bool BarcodeShowPrice { get; set; } = true;

    [JsonPropertyName("barcodeShowName")]
    public bool BarcodeShowName { get; set; } = true;

    [JsonPropertyName("barcodeShowSize")]
    public bool BarcodeShowSize { get; set; } = true;

    [JsonPropertyName("barcodeShowColor")]
    public bool BarcodeShowColor { get; set; } = true;

    [JsonPropertyName("barcodeLabelWidth")]
    public int BarcodeLabelWidth { get; set; } = 50;

    [JsonPropertyName("barcodeLabelHeight")]
    public int BarcodeLabelHeight { get; set; } = 30;

    [JsonPropertyName("barcodeSvgWidth")]
    public int BarcodeSvgWidth { get; set; } = 40;

    [JsonPropertyName("barcodeSvgHeight")]
    public int BarcodeSvgHeight { get; set; } = 10;

    [JsonPropertyName("barcodeShowStoreName")]
    public bool BarcodeShowStoreName { get; set; } = true;

    // --- 13. Barcode Advanced Scaling & Margins ---
    [JsonPropertyName("barcodeMarginLeft")]
    public int BarcodeMarginLeft { get; set; } = 0;

    [JsonPropertyName("barcodeMarginTop")]
    public int BarcodeMarginTop { get; set; } = 0;

    [JsonPropertyName("barcodeStoreFontSize")]
    public int BarcodeStoreFontSize { get; set; } = 8;

    [JsonPropertyName("barcodeNameFontSize")]
    public int BarcodeNameFontSize { get; set; } = 10;

    [JsonPropertyName("barcodePriceFontSize")]
    public int BarcodePriceFontSize { get; set; } = 14;

    [JsonPropertyName("barcodeVariantFontSize")]
    public int BarcodeVariantFontSize { get; set; } = 9;

    [JsonPropertyName("barcodeCodeFontSize")]
    public int BarcodeCodeFontSize { get; set; } = 15;

    [JsonPropertyName("lastUpdatedAt")]
    public DateTime LastUpdateDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("barcodeMarginRight")]
    public int BarcodeMarginRight { get; set; } = 0;

    [JsonPropertyName("barcodeMarginBottom")]
    public int BarcodeMarginBottom { get; set; } = 0;

    [JsonPropertyName("barcodePaddingTop")]
    public int BarcodePaddingTop { get; set; } = 0;

    [JsonPropertyName("barcodePaddingLeft")]
    public int BarcodePaddingLeft { get; set; } = 0;

    [JsonPropertyName("barcodePaddingRight")]
    public int BarcodePaddingRight { get; set; } = 0;

    [JsonPropertyName("barcodePaddingBottom")]
    public int BarcodePaddingBottom { get; set; } = 0;

    [JsonPropertyName("barcodeDirection")]
    public int BarcodeDirection { get; set; } = 0;

    [JsonPropertyName("barcodeRotation")]
    public int BarcodeRotation { get; set; } = 0;

    // --- 14. Attendance Delay Deduction Policy ---
    [JsonPropertyName("enableGraduatedDelayPolicy")]
    public bool EnableGraduatedDelayPolicy { get; set; } = true;

    [JsonPropertyName("delayGraceMinutes")]
    public int DelayGraceMinutes { get; set; } = 15;

    [JsonPropertyName("delayQuarterDayLimitMinutes")]
    public int DelayQuarterDayLimitMinutes { get; set; } = 30;

    [JsonPropertyName("delayHalfDayLimitMinutes")]
    public int DelayHalfDayLimitMinutes { get; set; } = 60;

    // --- 15. Linktree Customizations ---
    [JsonPropertyName("linktreeConfig")]
    public string? LinktreeConfig { get; set; }

    // --- 16. Sales Target ---
    [JsonPropertyName("dailyTarget")]
    public decimal DailyTarget { get; set; } = 15000;

    // --- 17. E-Invoicing (Tax Authority) ---
    [JsonPropertyName("taxAuthorityType")]
    public TaxAuthorityType TaxAuthorityType { get; set; } = TaxAuthorityType.None;

    [MaxLength(50)]
    [JsonPropertyName("etaTaxNumber")]
    public string? EtaTaxNumber { get; set; }

    [MaxLength(200)]
    [JsonPropertyName("etaClientId")]
    public string? EtaClientId { get; set; }

    [MaxLength(200)]
    [JsonPropertyName("etaClientSecret")]
    public string? EtaClientSecret { get; set; }

    [MaxLength(50)]
    [JsonPropertyName("etaEnvironment")]
    public string? EtaEnvironment { get; set; } // "PreProduction" or "Production"

    [MaxLength(50)]
    [JsonPropertyName("etaSignatureType")]
    public string? EtaSignatureType { get; set; } // "LocalUsbToken", "CloudHSM"

    [MaxLength(100)]
    [JsonPropertyName("etaPosSerial")]
    public string? EtaPosSerial { get; set; } // For E-Receipts

    [MaxLength(50)]
    [JsonPropertyName("zatcaEnvironment")]
    public string? ZatcaEnvironment { get; set; } // "Developer", "Simulation", "Production"

    [MaxLength(50)]
    [JsonPropertyName("zatcaTaxNumber")]
    public string? ZatcaTaxNumber { get; set; }

    [JsonPropertyName("zatcaCertificate")]
    public string? ZatcaCertificate { get; set; } // Base64 encoded cert for ZATCA Phase 2

    // --- 18. Wapilot WhatsApp API ---
    [MaxLength(200)]
    [JsonPropertyName("wapilotApiKey")]
    public string? WapilotApiKey { get; set; }

    [MaxLength(100)]
    [JsonPropertyName("wapilotPosInstanceId")]
    public string? WapilotPosInstanceId { get; set; }

    [MaxLength(100)]
    [JsonPropertyName("wapilotWebInstanceId")]
    public string? WapilotWebInstanceId { get; set; }

    [JsonPropertyName("autoSendWhatsAppInvoices")]
    public bool AutoSendWhatsAppInvoices { get; set; } = false;

    // --- Subscription Info (Not Mapped to Tenant DB) ---
    [NotMapped]
    [JsonPropertyName("subscriptionExpiresAt")]
    public DateTime? SubscriptionExpiresAt { get; set; }

    [NotMapped]
    [JsonPropertyName("subscriptionGraceDays")]
    public int SubscriptionGraceDays { get; set; }
}
