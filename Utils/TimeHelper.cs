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

    public static int GetBusinessDayEndHour() => _service?.GetBusinessDayEndHour() ?? 2;

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
        if (_service != null) return _service.GetTimeZone();
        
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

    /// <summary>
    /// Gets the start of the current business day (using the dynamic cutoff).
    /// If current time is 1:00 AM on May 15 and cutoff is 2, it returns May 14 02:00 AM.
    /// </summary>
    public static DateTime GetEgyptBusinessDayStart()
    {
        var now = GetEgyptTime();
        var endHour = GetBusinessDayEndHour();
        if (now.Hour < endHour)
        {
            return now.Date.AddDays(-1).AddHours(endHour);
        }
        return now.Date.AddHours(endHour);
    }

    /// <summary>
    /// Gets the business date for a given datetime.
    /// Since our SQL query filters already apply the 2:00 AM cutoff to EntryDate,
    /// we just return the actual timestamp to prevent double-shifting the date by 48 hours.
    /// </summary>
    public static DateTime GetEgyptBusinessDayDate(DateTime dt)
    {
        return dt;
    }
}

