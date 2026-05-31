using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services;

/// <summary>
/// Generates unique, sequential document numbers that are safe under concurrent requests.
/// Database-backed and safe across multiple server instances.
/// </summary>
public class SequenceService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SequenceService> _logger;

    public SequenceService(IServiceScopeFactory scopeFactory, ILogger<SequenceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [Obsolete("Use NextAsync(prefix) instead. This overload seeds the DbSequences table if it doesn't exist.")]
    public async Task<string> NextAsync(string prefix, Func<AppDbContext, string, Task<int>> maxSelector)
    {
        var now   = TimeHelper.GetEgyptTime();
        var stamp = $"{now.Year % 100:D2}{now.Month:D2}";

        // Employees do not use date stamp (YYMM) in their sequential ID (e.g. EMP-0001 instead of EMP-2605-0001)
        if (prefix == "EMP")
        {
            stamp = string.Empty;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var exists = await db.DbSequences.AnyAsync(s => s.Prefix == prefix && s.Stamp == stamp);
        if (!exists)
        {
            try
            {
                var searchPattern = string.IsNullOrEmpty(stamp) ? $"{prefix}-%" : $"{prefix}-{stamp}-%";
                var currentMax = await maxSelector(db, searchPattern);
                var seq = new DbSequence
                {
                    Prefix = prefix,
                    Stamp = stamp,
                    LastValue = currentMax,
                    LastUpdatedAt = now
                };
                db.DbSequences.Add(seq);
                await db.SaveChangesAsync();
                _logger.LogInformation("Seeded DbSequence for {Prefix}-{Stamp} with initial value {Max}", prefix, stamp, currentMax);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed DbSequence for {Prefix}. It will start from 1.", prefix);
            }
        }

        return await NextAsync(prefix);
    }

    /// <summary>
    /// Returns the next document number, e.g. "PO-2504-0042" or "EMP-0005".
    /// Format: {prefix}-{YY}{MM}-{seq:D4} (or {prefix}-{seq:D4} if no stamp)
    /// Database-backed and safe across multiple server instances.
    /// </summary>
    public async Task<string> NextAsync(string prefix)
    {
        var now   = TimeHelper.GetEgyptTime();
        var stamp = $"{now.Year % 100:D2}{now.Month:D2}";

        // Employees do not use date stamp (YYMM) in their sequential ID (e.g. EMP-0001 instead of EMP-2605-0001)
        if (prefix == "EMP")
        {
            stamp = string.Empty;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // Use Serializable to ensure no two instances read the same value
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                var seq = await db.DbSequences
                    .FirstOrDefaultAsync(s => s.Prefix == prefix && s.Stamp == stamp);

                if (seq == null)
                {
                    seq = new DbSequence
                    {
                        Prefix = prefix,
                        Stamp = stamp,
                        LastValue = 1,
                        LastUpdatedAt = now
                    };
                    db.DbSequences.Add(seq);
                }
                else
                {
                    seq.LastValue++;
                    seq.LastUpdatedAt = now;
                }

                await db.SaveChangesAsync();
                await tx.CommitAsync();

                if (string.IsNullOrEmpty(stamp))
                {
                    return $"{prefix}-{seq.LastValue:D4}";
                }
                return $"{prefix}-{stamp}-{seq.LastValue:D4}";
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("Duplicate") == true || ex.InnerException?.Message.Contains("unique") == true)
            {
                // Conflict during initial creation - retry will handle it automatically via strategy
                await tx.RollbackAsync();
                throw; 
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sequence generation failed for {Prefix}", prefix);
                await tx.RollbackAsync();
                throw;
            }
        });
    }

    public static string GetDepartmentPrefix(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "EMP";

        name = name.Trim();

        // Normalize Arabic letters for easier contains check (he/te marbuta, and various alefs)
        var normalized = name.ToLower()
            .Replace("أ", "ا")
            .Replace("إ", "ا")
            .Replace("آ", "ا")
            .Replace("ة", "ه");

        // 1. Check a mapping of common Arabic/English names
        if (normalized.Contains("مبيعات") || normalized.Contains("sales")) return "SAL";
        if (normalized.Contains("حسابات") || normalized.Contains("مالي") || normalized.Contains("finance") || normalized.Contains("accounting") || normalized.Contains("acc")) return "ACC";
        if (normalized.Contains("بشريه") || normalized.Contains("hr") || normalized.Contains("human")) return "HR";
        if (normalized.Contains("تسويق") || normalized.Contains("marketing") || normalized.Contains("mkt")) return "MKT";
        if (normalized.Contains("تشغيل") || normalized.Contains("operation") || normalized.Contains("ops")) return "OPS";
        if (normalized.Contains("صيانه") || normalized.Contains("maintenance") || normalized.Contains("mnt")) return "MNT";
        if (normalized.Contains("اداره") || normalized.Contains("ادرا") || normalized.Contains("admin") || normalized.Contains("mng")) return "ADM";
        if (normalized.Contains("مشتريات") || normalized.Contains("purchase") || normalized.Contains("procurement")) return "PUR";
        if (normalized.Contains("مخازن") || normalized.Contains("مخزن") || normalized.Contains("store") || normalized.Contains("inventory")) return "INV";
        if (normalized.Contains("تقنيه") || normalized.Contains("تكنولوجيا") || normalized.Contains("it") || normalized.Contains("tech") || normalized.Contains("information")) return "IT";

        // 2. If it is English, extract first letters of words or first 3 letters
        var isEnglish = System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z\s]+$");
        if (isEnglish)
        {
            var words = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 1)
            {
                var initials = string.Join("", words.Select(w => w[0])).ToUpper();
                return initials.Length <= 4 ? initials : initials.Substring(0, 4);
            }
            else
            {
                var clean = name.ToUpper();
                return clean.Length > 3 ? clean.Substring(0, 3) : clean;
            }
        }

        // 3. For any other Arabic or mixed name:
        // Strip common prefixes like "ال" or "قسم" or "ادارة"
        var cleanArabic = name;
        var wordsAr = cleanArabic.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Where(w => w != "قسم" && w != "ادارة" && w != "إدارة" && w != "فرع")
                                 .ToArray();

        if (wordsAr.Length > 1)
        {
            // Take the first letter of each word (transliterated)
            var prefix = string.Join("", wordsAr.Select(w => TransliterateChar(w[0]))).ToUpper();
            return prefix.Length > 4 ? prefix.Substring(0, 4) : prefix;
        }
        else if (wordsAr.Length == 1)
        {
            // Transliterate the single word and take first 3 letters
            var trans = TransliterateWord(wordsAr[0]);
            return trans.Length > 3 ? trans.Substring(0, 3).ToUpper() : trans.ToUpper();
        }

        return "EMP";
    }

    private static char TransliterateChar(char c)
    {
        return c switch
        {
            'أ' or 'ا' or 'إ' or 'آ' => 'A',
            'ب' => 'B',
            'ت' or 'ة' => 'T',
            'ث' => 'T',
            'ج' => 'J',
            'ح' or 'خ' => 'K',
            'د' => 'D',
            'ذ' => 'Z',
            'ر' => 'R',
            'ز' => 'Z',
            'س' or 'ص' => 'S',
            'ش' => 'S',
            'ض' => 'D',
            'ط' or 'ظ' => 'T',
            'ع' or 'غ' => 'A',
            'ف' => 'F',
            'ق' => 'Q',
            'ك' => 'K',
            'ل' => 'L',
            'م' => 'M',
            'ن' => 'N',
            'ه' => 'H',
            'و' => 'W',
            'ي' or 'ى' => 'Y',
            _ => char.ToUpper(c)
        };
    }

    private static string TransliterateWord(string word)
    {
        if (word.StartsWith("ال"))
        {
            word = word.Substring(2);
        }
        var sb = new System.Text.StringBuilder();
        foreach (var c in word)
        {
            sb.Append(TransliterateChar(c));
        }
        return sb.ToString();
    }
}
