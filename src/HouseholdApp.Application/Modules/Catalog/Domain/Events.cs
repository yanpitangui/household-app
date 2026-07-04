using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Catalog.Domain;

public sealed record CategoryAdded(Guid EventId, DateTimeOffset OccurredAt, Guid HouseholdId, Guid CategoryId) : IDomainEvent;

public sealed record CategoryUpdated(Guid EventId, DateTimeOffset OccurredAt, Guid HouseholdId, Guid CategoryId) : IDomainEvent;

public sealed record CategoryDeleted(Guid EventId, DateTimeOffset OccurredAt, Guid HouseholdId, Guid CategoryId) : IDomainEvent;
