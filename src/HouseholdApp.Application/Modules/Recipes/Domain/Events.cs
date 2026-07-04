using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Recipes.Domain;

public sealed record RecipeCreated(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid RecipeId, Guid HouseholdId, Guid CreatedBy,
    string? SourceUrl) : IDomainEvent;

public sealed record RecipeDeleted(
    Guid EventId, DateTimeOffset OccurredAt,
    Guid RecipeId, Guid HouseholdId, Guid DeletedBy) : IDomainEvent;
