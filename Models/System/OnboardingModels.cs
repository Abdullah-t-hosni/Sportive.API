using System;
using System.ComponentModel.DataAnnotations;

namespace Sportive.API.Models;

public class OnboardTenantRequest
{
    [Required] 
    public string Name { get; set; } = string.Empty;
    
    [Required] 
    public string Slug { get; set; } = string.Empty;
    
    [Required] 
    public string Subdomain { get; set; } = string.Empty;
    
    [Required] 
    public string DatabaseName { get; set; } = string.Empty;
    
    [Required] 
    public string DatabaseUser { get; set; } = string.Empty;
    
    [Required] 
    public string DatabasePassword { get; set; } = string.Empty;
}

public class TenantOnboardingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid TenantGuid { get; set; }
    public string? AdminEmail { get; set; }
    public string? TemporaryPassword { get; set; }
}

/// <summary>
/// طلب تسجيل العميل الذاتي من الموقع التسويقي — بدون credentials قاعدة بيانات
/// النظام يولدها تلقائياً بناءً على slug
/// </summary>
public class SelfRegisterRequest
{
    /// <summary>اسم الشركة / المؤسسة</summary>
    [Required(ErrorMessage = "اسم الشركة مطلوب")]
    [MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>اسم المسؤول (الشخص المُسجِّل)</summary>
    [Required(ErrorMessage = "اسم المسؤول مطلوب")]
    [MaxLength(200)]
    public string ContactName { get; set; } = string.Empty;

    /// <summary>البريد الإلكتروني للمسؤول — سيُرسَل عليه البيانات</summary>
    [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
    [EmailAddress(ErrorMessage = "البريد الإلكتروني غير صالح")]
    public string Email { get; set; } = string.Empty;

    /// <summary>رقم الهاتف</summary>
    [Required(ErrorMessage = "رقم الهاتف مطلوب")]
    [MaxLength(20)]
    public string Phone { get; set; } = string.Empty;

    /// <summary>الـ Slug المختار: يتحول لـ slug.raakiza.com</summary>
    [Required(ErrorMessage = "اسم النطاق الفرعي مطلوب")]
    [RegularExpression(@"^[a-z0-9][a-z0-9\-]{1,48}[a-z0-9]$", ErrorMessage = "الـ Slug يجب أن يحتوي على أحرف إنجليزية صغيرة وأرقام وشرطات فقط (3-50 حرف)")]
    public string Slug { get; set; } = string.Empty;

    /// <summary>ID الباقة المختارة (اختياري — إذا لم يُحدَّد يبدأ بـ Trial)</summary>
    public int? PlanId { get; set; }
}

public class SelfRegisterResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Subdomain { get; set; }
    public string? AdminEmail { get; set; }
}
