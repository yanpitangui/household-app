namespace HouseholdApp.Application.Modules.Identity.Application.Ports;

public sealed record UserProfile(Guid Id, string Subject, string Email, string DisplayName, string? PictureUrl);

public interface IUserQuery
{
    Task<UserProfile?> GetBySubjectAsync(string subject, CancellationToken ct = default);
    Task<UserProfile?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<UserProfile>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
}
