using HouseholdApp.Application.Shared.Time;

namespace HouseholdApp.IntegrationTests.Infrastructure;

/// <summary>Household clock over a test <see cref="TimeProvider"/>; UTC unless a zone is given.</summary>
public sealed class FakeHouseholdClock(TimeProvider time, TimeZoneInfo? zone = null) : IHouseholdClock
{
    private readonly TimeZoneInfo _zone = zone ?? TimeZoneInfo.Utc;

    public Task<TimeZoneInfo> ZoneAsync(Guid householdId, CancellationToken ct = default) => Task.FromResult(_zone);

    public Task<DateOnly> TodayAsync(Guid householdId, CancellationToken ct = default) =>
        Task.FromResult(HouseholdTime.Today(_zone, time.GetUtcNow()));
}
