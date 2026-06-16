namespace HouseholdApp.Application.Modules.Households.Application.Ports;

public sealed record HouseholdSummary(Guid Id, string Name, long MemberCount, DateTime CreatedAt);

public sealed record HouseholdDetail(
    Guid Id, string Name, DateTime CreatedAt,
    IReadOnlyList<HouseholdMemberDto> Members);

public sealed record HouseholdMemberDto(Guid UserId, string DisplayName, string Role, string? PictureUrl);

public interface IHouseholdQueries
{
    Task<IReadOnlyList<HouseholdSummary>> ListForUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<HouseholdName>> ListNamesAsync(Guid userId, CancellationToken ct = default);
    Task<HouseholdDetail?> GetAsync(Guid householdId, CancellationToken ct = default);
    Task<IReadOnlyList<HouseholdMemberDto>> GetMembersAsync(Guid householdId, CancellationToken ct = default);
}

public sealed record HouseholdName(Guid Id, string Name);
