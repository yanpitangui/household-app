namespace HouseholdApp.Application.Shared.Time;

/// <summary>Calendar arithmetic in a household's time zone.</summary>
public static class HouseholdTime
{
    public static DateOnly Today(TimeZoneInfo zone, DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

    public static DateTimeOffset ToLocal(TimeZoneInfo zone, DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, zone);

    public static TimeZoneInfo Find(string id) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;
}
