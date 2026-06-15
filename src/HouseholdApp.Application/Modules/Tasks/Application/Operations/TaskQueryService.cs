using Dapper;
using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using Npgsql;

namespace HouseholdApp.Application.Modules.Tasks.Application.Operations;

public sealed class TaskQueryService(NpgsqlDataSource db) : ITaskQueries
{
    public async Task<IReadOnlyList<TaskSummary>> ListAsync(Guid householdId, bool includeCompleted = false, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var sql = """
            SELECT id, title, description, assigned_to AS AssignedTo, due_date AS DueDate,
                   CASE status WHEN 0 THEN 'Pending' ELSE 'Completed' END AS Status,
                   created_at AS CreatedAt
            FROM tasks.tasks
            WHERE household_id = @householdId
              AND (@includeCompleted OR status = 0)
            ORDER BY due_date ASC NULLS LAST, created_at DESC
            """;
        var rows = await conn.QueryAsync<TaskSummary>(sql, new { householdId, includeCompleted });
        return rows.ToList();
    }

    public async Task<TaskSummary?> GetAsync(Guid taskId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<TaskSummary>(
            """
            SELECT id, title, description, assigned_to AS AssignedTo, due_date AS DueDate,
                   CASE status WHEN 0 THEN 'Pending' ELSE 'Completed' END AS Status,
                   created_at AS CreatedAt
            FROM tasks.tasks WHERE id = @taskId
            """,
            new { taskId });
    }

    public async Task<IReadOnlyList<RecurringTaskSummary>> ListRecurringAsync(Guid householdId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<RecurringTaskSummary>(
            """
            SELECT id, title, description, default_assigned_to AS DefaultAssignedTo,
                   cron_expression AS CronExpression, is_active AS IsActive, next_run_at AS NextRunAt
            FROM tasks.recurring_tasks WHERE household_id = @householdId
            ORDER BY title
            """,
            new { householdId });
        return rows.ToList();
    }
}
