using System.Text.Json;
using Dapper;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Shared.Domain;
using Marten;
using Marten.Exceptions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure;

/// <summary>
/// Starts one stream per row of the old <c>expenses.recurring_expenses</c> table, keeping the template id so past Expenses
/// stay linked. Runs at every start and only touches rows with no stream yet, so nodes starting together are safe. The
/// sessions carry no event listener, so nothing is published; <c>Imported</c> keeps the rows out of the feed. Removed with
/// the table in a later release.
/// </summary>
internal sealed class RecurringExpenseStreamMigration(
    NpgsqlDataSource db,
    IDocumentStore store,
    TimeProvider time,
    ILogger<RecurringExpenseStreamMigration> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => RunAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task RunAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Row>(
            """
            SELECT id, household_id, expense_group_id, description, recurrence_frequency, start_at, is_active,
                   funding_sources::text AS funding_sources, allocations::text AS allocations
            FROM expenses.recurring_expenses
            """);

        foreach (var row in rows)
        {
            try
            {
                await MigrateAsync(row, ct);
            }
            catch (ExistingStreamIdCollisionException)
            {
                logger.LogInformation("Recurring expense {RecurringExpenseId} was migrated by another node", row.Id);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Migrating recurring expense {RecurringExpenseId} failed; retrying on the next start", row.Id);
            }
        }
    }

    private async Task MigrateAsync(Row row, CancellationToken ct)
    {
        await using var session = store.LightweightSession();
        if (await session.Events.FetchStreamStateAsync(row.Id, ct) is not null) return;

        var utc = row.StartAt.UtcDateTime;
        var schedule = new RecurrenceSchedule(
            Enum.Parse<RecurrenceFrequency>(row.RecurrenceFrequency), DateOnly.FromDateTime(utc), TimeOnly.FromDateTime(utc));
        var funding = JsonSerializer.Deserialize<List<FundingSource>>(row.FundingSources) ?? [];
        var allocations = JsonSerializer.Deserialize<List<Allocation>>(row.Allocations) ?? [];
        var now = time.GetUtcNow();
        var actor = funding.Count > 0 ? funding[0].UserId : Guid.Empty;

        IDomainEvent created = new RecurringExpenseCreated(
            Guid.CreateVersion7(), row.StartAt, row.Id, row.HouseholdId, row.ExpenseGroupId, row.Description, funding,
            allocations, schedule, schedule.NextAfter(now), actor, Imported: true);

        if (row.IsActive)
            session.Events.StartStream(row.Id, created);
        else
            session.Events.StartStream(row.Id, created, new RecurringExpenseEnded(
                Guid.CreateVersion7(), now, row.Id, row.HouseholdId, row.ExpenseGroupId, row.Description, actor, Imported: true));

        await session.SaveChangesAsync(ct);
    }

    private sealed record Row(
        Guid Id, Guid HouseholdId, Guid ExpenseGroupId, string Description, string RecurrenceFrequency,
        DateTimeOffset StartAt, bool IsActive, string FundingSources, string Allocations);
}
