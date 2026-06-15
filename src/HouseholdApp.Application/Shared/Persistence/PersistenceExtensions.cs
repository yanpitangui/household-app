using System.Data;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace HouseholdApp.Application.Shared.Persistence;

public static class PersistenceExtensions
{
    public static void RegisterTypeHandlers()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
    }

    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        RegisterTypeHandlers();
        services.AddScoped<IUnitOfWork, DapperUnitOfWork>();
        return services;
    }

    // Npgsql reports TIMESTAMPTZ as DateTime (UTC) via GetFieldType(); this handler
    // bridges Dapper's constructor-matching step to DateTimeOffset properties.
    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) =>
            value is DateTime dt
                ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
                : (DateTimeOffset)value;

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) =>
            parameter.Value = value.UtcDateTime;
    }
}
