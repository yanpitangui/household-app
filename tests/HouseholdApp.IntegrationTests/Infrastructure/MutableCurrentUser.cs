using HouseholdApp.Application.Shared.Identity;

namespace HouseholdApp.IntegrationTests.Infrastructure;

public sealed class MutableCurrentUser : ICurrentUser
{
    public Guid Id { get; set; }
}
