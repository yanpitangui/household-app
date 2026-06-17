using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Lists.Domain;

public sealed record ListCreated(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid ListId, Guid HouseholdId, string Name, Guid CreatedBy) : IDomainEvent;

public sealed record ListItemAdded(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid ListId, Guid ItemId, string Name, Guid? CatalogItemId, Guid? CategoryId, Guid AddedBy, int SortOrder) : IDomainEvent;

public sealed record ListItemCompleted(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid ListId, Guid ItemId, Guid CompletedBy) : IDomainEvent;

public sealed record ListItemUncompleted(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid ListId, Guid ItemId) : IDomainEvent;

public sealed record ListItemRemoved(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid ListId, Guid ItemId) : IDomainEvent;

public sealed record ListItemCategoryChanged(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid ListId, Guid ItemId, Guid? CategoryId) : IDomainEvent;
