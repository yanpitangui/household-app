namespace HouseholdApp.Application.Shared.Caching;

public sealed record WithETag<T>(T Value, string ETag);
