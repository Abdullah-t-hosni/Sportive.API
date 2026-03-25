using System.Text;

namespace ArabicReshaper;

/// <summary>
/// A simple Arabic Reshaper to handle contextual character forms for rendering in environments
/// that do not natively support complex Arabic text shaping (like some PDF engines).
/// </summary>
public static class ArabicAdapter
{
    public static string Reshape(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return Reshaper.Reshape(input);
    }
}

internal static class Reshaper
{
    private static readonly char[][] CharMap = new char[0x6FF + 1][];

    static Reshaper()
    {
        // Define common Arabic character mappings (Isolated, End, Middle, Beginning)
        // This is a subset for commonly used characters
        AddMapping(0x0621, 0xFE80); // HAMZA
        AddMapping(0x0622, 0xFE81, 0xFE82); // ALEF WITH MADDA ABOVE
        AddMapping(0x0623, 0xFE83, 0xFE84); // ALEF WITH HAMZA ABOVE
        AddMapping(0x0624, 0xFE85, 0xFE86); // WAW WITH HAMZA ABOVE
        AddMapping(0x0625, 0xFE87, 0xFE88); // ALEF WITH HAMZA BELOW
        AddMapping(0x0626, 0xFE89, 0xFE8A, 0xFE8B, 0xFE8C); // YEH WITH HAMZA ABOVE
        AddMapping(0x0627, 0xFE8D, 0xFE8E); // ALEF
        AddMapping(0x0628, 0xFE8F, 0xFE90, 0xFE91, 0xFE92); // BEH
        AddMapping(0x0629, 0xFE93, 0xFE94); // TEH MARBUTA
        AddMapping(0x062A, 0xFE95, 0xFE96, 0xFE97, 0xFE98); // TEH
        AddMapping(0x062B, 0xFE99, 0xFE9A, 0xFE9B, 0xFE9C); // THEH
        AddMapping(0x062C, 0xFE9D, 0xFE9E, 0xFE9F, 0xFEA0); // JEEM
        AddMapping(0x062D, 0xFEA1, 0xFEA2, 0xFEA3, 0xFEA4); // HAH
        AddMapping(0x062E, 0xFEA5, 0xFEA6, 0xFEA7, 0xFEA8); // KHAH
        AddMapping(0x062F, 0xFEA9, 0xFEAA); // DAL
        AddMapping(0x0630, 0xFEAB, 0xFEAC); // THAL
        AddMapping(0x0631, 0xFEAD, 0xFEAE); // REH
        AddMapping(0x0632, 0xFEAF, 0xFEB0); // ZAIN
        AddMapping(0x0633, 0xFEB1, 0xFEB2, 0xFEB3, 0xFEB4); // SEEN
        AddMapping(0x0634, 0xFEB5, 0xFEB6, 0xFEB7, 0xFEB8); // SHEEN
        AddMapping(0x0635, 0xFEB9, 0xFEBA, 0xFEBB, 0xFEBC); // SAD
        AddMapping(0x0636, 0xFEBD, 0xFEBE, 0xFEBF, 0xFEC0); // DAD
        AddMapping(0x0637, 0xFEC1, 0xFEC2, 0xFEC3, 0xFEC4); // TAH
        AddMapping(0x0638, 0xFEC5, 0xFEC6, 0xFEC7, 0xFEC8); // ZAH
        AddMapping(0x0639, 0xFEC9, 0xFECA, 0xFECB, 0xFECC); // AIN
        AddMapping(0x063A, 0xFECD, 0xFECE, 0xFECF, 0xFED0); // GHAIN
        AddMapping(0x0641, 0xFED1, 0xFED2, 0xFED3, 0xFED4); // FEH
        AddMapping(0x0642, 0xFED5, 0xFED6, 0xFED7, 0xFED8); // QAF
        AddMapping(0x0643, 0xFED9, 0xFEDA, 0xFEDB, 0xFEDC); // KAF
        AddMapping(0x0644, 0xFEDD, 0xFEDE, 0xFEDF, 0xFEE0); // LAM
        AddMapping(0x0645, 0xFEE1, 0xFEE2, 0xFEE3, 0xFEE4); // MEEM
        AddMapping(0x0646, 0xFEE5, 0xFEE6, 0xFEE7, 0xFEE8); // NOON
        AddMapping(0x0647, 0xFEE9, 0xFEEA, 0xFEEB, 0xFEEC); // HEH
        AddMapping(0x0648, 0xFEED, 0xFEEE); // WAW
        AddMapping(0x0649, 0xFEEF, 0xFEF0); // ALEF MAKSURA
        AddMapping(0x064A, 0xFEF1, 0xFEF2, 0xFEF3, 0xFEF4); // YEH
    }

    private static void AddMapping(int code, int isolated, int end = 0, int middle = 0, int beginning = 0)
    {
        CharMap[code] = new char[] { (char)isolated, (char)(end == 0 ? isolated : end), (char)(middle == 0 ? (end == 0 ? isolated : end) : middle), (char)(beginning == 0 ? isolated : beginning) };
    }

    public static string Reshape(string input)
    {
        var output = new StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            char current = input[i];
            if (current < 0x0600 || current > 0x06FF || CharMap[current] == null)
            {
                output.Append(current);
                continue;
            }

            bool linkBefore = i > 0 && CanLinkToNext(input[i - 1]);
            bool linkAfter = i < input.Length - 1 && CanLinkToPrevious(input[i + 1]);

            if (linkBefore && linkAfter) output.Append(CharMap[current][2]); // Middle
            else if (linkBefore) output.Append(CharMap[current][1]); // End
            else if (linkAfter) output.Append(CharMap[current][3]); // Beginning
            else output.Append(CharMap[current][0]); // Isolated
        }
        return output.ToString();
    }

    private static bool CanLinkToNext(char c)
    {
        if (c < 0x0600 || c > 0x06FF || CharMap[c] == null) return false;
        // Characters that don't link to next: Alef, Dal, Thal, Reh, Zain, Waw, Hamza
        return c != 0x0621 && c != 0x0622 && c != 0x0623 && c != 0x0625 && c != 0x0627 && c != 0x062F && c != 0x0630 && c != 0x0631 && c != 0x0632 && c != 0x0648 && c != 0x0649;
    }

    private static bool CanLinkToPrevious(char c)
    {
        return c >= 0x0600 && c <= 0x06FF && CharMap[c] != null && c != 0x0621;
    }
}
