using HouseholdApp.Application.Shared.Identity;

namespace HouseholdApp.IntegrationTests.Infrastructure;

public sealed class FakeCurrentUser(Guid id) : ICurrentUser
{
    public Guid Id { get; } = id;
}
