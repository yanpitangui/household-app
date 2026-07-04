using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using Marten;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure;

public sealed class MartenHealthCheck(IQuerySession session) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await session.Query<ExpenseReadModel>().Take(1).ToListAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Marten (expenses schema) query failed", ex);
        }
    }
}
