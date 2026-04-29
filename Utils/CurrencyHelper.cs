using System.Text;

namespace Sportive.API.Utils;

public static class CurrencyHelper
{
    private static readonly string[] Ones = { "", "واحد", "اثنان", "ثلاثة", "أربعة", "خمسة", "ستة", "سبعة", "ثمانية", "تسعة", "عشرة", "أحد عشر", "اثنا عشر", "ثلاثة عشر", "أربعة عشر", "خمسة عشر", "ستة عشر", "سبعة عشر", "ثمانية عشر", "تسعة عشر" };
    private static readonly string[] Tens = { "", "عشرة", "عشرون", "ثلاثون", "أربعون", "خمسون", "ستون", "سبعون", "ثمانون", "تسعون" };
    private static readonly string[] Hundreds = { "", "مائة", "مائتان", "ثلاثمائة", "أربعمائة", "خمسمائة", "ستمائة", "سبعمائة", "ثمانمائة", "تسعمائة" };

    /// <summary>
    /// ✅ Financial-safe rounding — always round half away from zero.
    /// Use this everywhere instead of Math.Round() to avoid accounting mismatches.
    /// </summary>
    public static decimal Round(decimal value, int decimals = 2)
        => Math.Round(value, decimals, MidpointRounding.AwayFromZero);

    public static string ToArabicWords(decimal amount)
    {
        if (amount == 0) return "صفر جنيه";

        long wholePart = (long)Math.Floor(amount);
        int decimalPart = (int)((amount - wholePart) * 100);

        string wholeWords = ConvertToWords(wholePart);
        string result = wholeWords + " جنيهاً";

        if (decimalPart > 0)
        {
            result += " و " + ConvertToWords(decimalPart) + " قرشاً";
        }

        return "فقط " + result + " لا غير";
    }

    private static string ConvertToWords(long number)
    {
        if (number == 0) return "";
        if (number < 20) return Ones[number];
        if (number < 100)
        {
            var ten = number / 10;
            var one = number % 10;
            return (one > 0 ? Ones[one] + " و " : "") + Tens[ten];
        }
        if (number < 1000)
        {
            var hundred = number / 100;
            var rest = number % 100;
            return Hundreds[hundred] + (rest > 0 ? " و " + ConvertToWords(rest) : "");
        }
        if (number < 1000000)
        {
            var thousand = number / 1000;
            var rest = number % 1000;
            string thousandWord = thousand == 1 ? "ألف" : thousand == 2 ? "ألفان" : (thousand >= 3 && thousand <= 10) ? ConvertToWords(thousand) + " آلاف" : ConvertToWords(thousand) + " ألف";
            return thousandWord + (rest > 0 ? " و " + ConvertToWords(rest) : "");
        }

        return number.ToString(); // Fallback for very large numbers
    }
}
