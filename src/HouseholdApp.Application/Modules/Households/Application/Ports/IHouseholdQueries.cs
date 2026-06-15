namespace HouseholdApp.Application.Modules.Households.Application.Ports;

public sealed record HouseholdSummary(Guid Id, string Name, long MemberCount, DateTime CreatedAt);

public sealed record HouseholdDetail(
    Guid Id, string Name, DateTime CreatedAt,
    IReadOnlyList<HouseholdMemberDto> Members);

public sealed record HouseholdMemberDto(Guid UserId, string DisplayName, string Role);

public interface IHouseholdQueries
{
    Task<IReadOnlyList<HouseholdSummary>> ListForUserAsync(Guid userId, CancellationToken ct = default);
    Task<HouseholdDetail?> GetAsync(Guid householdId, CancellationToken ct = default);
}
