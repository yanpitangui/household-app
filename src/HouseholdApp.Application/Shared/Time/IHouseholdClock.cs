namespace HouseholdApp.Application.Shared.Time;

public interface IHouseholdClock
{
    Task<TimeZoneInfo> ZoneAsync(Guid householdId, CancellationToken ct = default);
    Task<DateOnly> TodayAsync(Guid householdId, CancellationToken ct = default);
}
