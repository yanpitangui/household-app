using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Lists.Domain;

public sealed record ListCreated(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid ListId, Guid HouseholdId, string Name, Guid CreatedBy) : IDomainEvent;

public sealed record ListItemAdded(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid ListId, Guid ItemId, string Name, string? Category, int SortOrder) : IDomainEvent;

public sealed record ListItemCompleted(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid ListId, Guid ItemId, Guid CompletedBy) : IDomainEvent;

public sealed record ListItemRemoved(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid ListId, Guid ItemId) : IDomainEvent;
