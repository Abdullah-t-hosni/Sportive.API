using System;

namespace Sportive.API.Utils;

public static class TimeHelper
{
    public static DateTime GetEgyptTime()
    {
        var utcNow = DateTime.UtcNow;
        try
        {
            // Windows ID
            var egyptZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(utcNow, egyptZone);
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                // Linux/Docker IANA ID
                var egyptZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
                return TimeZoneInfo.ConvertTimeFromUtc(utcNow, egyptZone);
            }
            catch (TimeZoneNotFoundException)
            {
                // Fallback: Fixed +2 hours from UTC
                return utcNow.AddHours(2);
            }
        }
    }
}
