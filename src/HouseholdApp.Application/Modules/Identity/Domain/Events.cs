using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Identity.Domain;

public sealed record UserProvisioned(Guid EventId, DateTimeOffset OccurredAt, Guid UserId, string Subject) : IDomainEvent;
