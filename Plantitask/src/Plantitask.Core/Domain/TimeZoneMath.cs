namespace Plantitask.Core.Domain;

public static class TimeZoneMath
{
    public static DateTime LocalMidnightToUtc(DateOnly date, TimeZoneInfo zone)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue);

        // Zones that move their clocks forward at midnight skip 00:00 on that day
        // and ConvertTimeToUtc throws on a time that never happens. 01:00 is the first one that does.
        if (zone.IsInvalidTime(midnight))
            midnight = midnight.AddHours(1);

        return TimeZoneInfo.ConvertTimeToUtc(midnight, zone);
    }
}
