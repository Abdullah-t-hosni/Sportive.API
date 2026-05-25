using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;

namespace Sportive.API.Services;

public interface ITimeService
{
    /// <summary>Returns the current local time according to the store's configured timezone.</summary>
    DateTime Now { get; }

    /// <summary>Returns today's date (no time component) in the store's timezone.</summary>
    DateTime Today { get; }
    
    /// <summary>Returns the current TimeZoneInfo configured for the store.</summary>
    TimeZoneInfo GetTimeZone();
}

/// <summary>
/// Provides store-local time based on the TimeZoneId stored in StoreSettings.
/// The timezone setting is cached for 10 minutes to avoid a DB hit on every timestamp.
/// </summary>
public class TimeService : ITimeService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TimeService> _logger;
    private const string CacheKey = "store_timezone";

    public TimeService(IServiceScopeFactory scopeFactory, IMemoryCache cache, ILogger<TimeService> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    public DateTime Now => ConvertToStoreTime(DateTime.UtcNow);
    public DateTime Today => Now.Date;

    private DateTime ConvertToStoreTime(DateTime utc)
    {
        var tz = GetTimeZone();
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }

    public TimeZoneInfo GetTimeZone()
    {
        if (_cache.TryGetValue(CacheKey, out TimeZoneInfo? cached) && cached != null)
            return cached;

        var tz = LoadTimeZoneFromDb() ?? FallbackEgyptZone();

        _logger.LogInformation("Resolved Store TimeZone: {TzId} ({Offset})", tz.Id, tz.BaseUtcOffset);
        _cache.Set(CacheKey, tz, TimeSpan.FromMinutes(10));
        return tz;
    }

    private TimeZoneInfo? LoadTimeZoneFromDb()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var tzId = db.StoreInfo
                .AsNoTracking()
                .Where(s => s.StoreConfigId == 1)
                .Select(s => s.TimeZoneId)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(tzId)) return null;
            return TimeZoneInfo.FindSystemTimeZoneById(tzId);
        }
        catch
        {
            return null;
        }
    }

    private static TimeZoneInfo FallbackEgyptZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        catch { }
        try { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
        catch { }
        return TimeZoneInfo.CreateCustomTimeZone("Egypt+3", TimeSpan.FromHours(3), "Egypt", "Egypt");
    }

    /// <summary>
    /// Call this after saving a new TimeZoneId to StoreInfo so the cached value is refreshed immediately.
    /// </summary>
    public void InvalidateCache() => _cache.Remove(CacheKey);
}
