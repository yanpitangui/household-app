namespace HouseholdApp.Application.Shared.Caching;

public sealed record WithLastModified<T>(T Value, DateTimeOffset LastModified);
