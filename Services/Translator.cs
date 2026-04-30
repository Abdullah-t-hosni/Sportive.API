using Microsoft.Extensions.Caching.Memory;
using Sportive.API.Interfaces;
using System.Text.Json;

namespace Sportive.API.Services;

public class Translator : ITranslator
{
    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _resourcesPath;
    private const string CacheKeyPrefix = "Translations_";

    public Translator(IMemoryCache cache, IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor)
    {
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
        _resourcesPath = Path.Combine(env.ContentRootPath, "Resources");
    }

    private string GetCurrentLanguage()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null) return "ar";

        var langHeader = context.Request.Headers["Accept-Language"].ToString();
        if (string.IsNullOrEmpty(langHeader)) return "ar";

        // Simple check: if starts with 'en', use English, otherwise Arabic
        return langHeader.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "ar";
    }

    private Dictionary<string, string> GetTranslations()
    {
        var lang = GetCurrentLanguage();
        var cacheKey = CacheKeyPrefix + lang;

        if (_cache.TryGetValue(cacheKey, out Dictionary<string, string>? translations) && translations != null)
        {
            return translations;
        }

        translations = LoadFromFile(lang);
        _cache.Set(cacheKey, translations, TimeSpan.FromHours(1));
        return translations;
    }

    private Dictionary<string, string> LoadFromFile(string lang)
    {
        var filePath = Path.Combine(_resourcesPath, $"{lang}.json");
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
        }
        catch
        {
            // Fallback
        }

        // If file doesn't exist or error, try fallback to translations.json if it still exists
        var fallbackPath = Path.Combine(_resourcesPath, "translations.json");
        if (File.Exists(fallbackPath))
        {
             try {
                var json = File.ReadAllText(fallbackPath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
             } catch { }
        }

        return new Dictionary<string, string>();
    }

    public string Get(string key)
    {
        var translations = GetTranslations();
        return translations.TryGetValue(key, out var value) ? value : key;
    }

    public string Get(string key, params object[] args)
    {
        var translation = Get(key);
        try
        {
            return string.Format(translation, args);
        }
        catch
        {
            return translation;
        }
    }
}
