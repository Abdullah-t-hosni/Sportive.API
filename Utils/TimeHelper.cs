using Sportive.API.Services;

namespace Sportive.API.Utils;

/// <summary>
/// Provides store-local time. Delegates to ITimeService when the app is running
/// (which reads the timezone from StoreSettings in the database).
/// Falls back to Egypt Standard Time if called before DI is initialized.
/// </summary>
public static class TimeHelper
{
    private static ITimeService? _service;

    /// <summary>Called once at startup by Program.cs after building the service provider.</summary>
    public static void Initialize(ITimeService service) => _service = service;

    public static DateTime GetEgyptTime() => _service?.Now ?? FallbackEgyptTime();

    public static DateTime ToStoreTime(this DateTime dt)
    {
        if (dt.Kind == DateTimeKind.Utc)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(dt, GetStoreTimeZone());
        }
        return dt;
    }

    public static TimeZoneInfo GetStoreTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        catch { }
        try { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
        catch { }
        return TimeZoneInfo.CreateCustomTimeZone("Egypt+3", TimeSpan.FromHours(3), "Egypt", "Egypt");
    }

    private static DateTime FallbackEgyptTime()
    {
        var utc = DateTime.UtcNow;
        return TimeZoneInfo.ConvertTimeFromUtc(utc, GetStoreTimeZone());
    }
}
