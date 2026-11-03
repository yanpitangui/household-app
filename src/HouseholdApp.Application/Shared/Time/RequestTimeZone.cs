namespace HouseholdApp.Application.Shared.Time;

/// <summary>The zone of the household this request is about; views format instants with it. UTC until set.</summary>
public sealed class RequestTimeZone
{
    public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;
    public DateTimeOffset ToLocal(DateTimeOffset instant) => HouseholdTime.ToLocal(Zone, instant);
}
