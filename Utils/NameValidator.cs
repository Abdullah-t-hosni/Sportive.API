using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sportive.API.Utils;

public static class NameValidator
{
    public static (bool IsValid, string Message) ValidateCustomerName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length < 3)
            return (false, "يرجى كتابة الاسم ثنائي على الأقل (الاسم الأول والعائلة)");

        var words = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                           .Where(w => w.Length >= 2)
                           .ToList();

        if (words.Count < 2)
            return (false, "يرجى كتابة الاسم ثنائي على الأقل (مثال: أحمد علي)");

        // Reject 3 or more consecutive identical characters (e.g. 'سسس', 'aaa')
        if (Regex.IsMatch(trimmed, @"(.)\1\1"))
            return (false, "يرجى كتابة اسم حقيقي بدون حروف مكررة عشوائياً");

        // Reject single words longer than 14 characters
        if (words.Any(w => w.Length > 14))
            return (false, "الاسم المكتوب يبدو غير صحيح، يرجى كتابة اسمك بصورة طبيعية");

        // Allowed characters: Arabic and Latin letters, spaces, hyphens, and apostrophes only
        if (!Regex.IsMatch(trimmed, @"^[\u0600-\u06FFa-zA-Z\s'.'-]+$"))
            return (false, "الاسم يجب أن يحتوي على حروف فقط بدون رموز أو أرقام");

        return (true, string.Empty);
    }
}
