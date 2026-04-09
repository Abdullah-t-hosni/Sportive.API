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

    /// <summary>Returns the current time in the store's configured timezone.</summary>
    public static DateTime GetEgyptTime() => _service?.Now ?? FallbackEgyptTime();

    private static DateTime FallbackEgyptTime()
    {
        var utc = DateTime.UtcNow;
        try { return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time")); }
        catch { }
        try { return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")); }
        catch { }
        return utc.AddHours(2);
    }
}
