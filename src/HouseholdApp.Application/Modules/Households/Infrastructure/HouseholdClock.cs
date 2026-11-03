using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Modules.Households.Domain;
using HouseholdApp.Application.Shared.Time;

namespace HouseholdApp.Application.Modules.Households.Infrastructure;

public sealed class HouseholdClock(IHouseholdQueries queries, TimeProvider time) : IHouseholdClock
{
    public async Task<TimeZoneInfo> ZoneAsync(Guid householdId, CancellationToken ct = default) =>
        HouseholdTime.Find((await queries.GetAsync(householdId, ct))?.TimeZone ?? HouseholdTimeZone.Default);

    public async Task<DateOnly> TodayAsync(Guid householdId, CancellationToken ct = default) =>
        HouseholdTime.Today(await ZoneAsync(householdId, ct), time.GetUtcNow());
}
