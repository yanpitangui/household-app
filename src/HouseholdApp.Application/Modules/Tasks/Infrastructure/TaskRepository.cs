using Dapper;
using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.Application.Modules.Tasks.Domain;
using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Persistence;

namespace HouseholdApp.Application.Modules.Tasks.Infrastructure;

internal sealed class TaskRepository(IUnitOfWork uow) : ITaskRepository
{
    public async Task<Guid> SaveTaskAsync(HouseholdTask task, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO tasks.tasks (id, household_id, title, description, assigned_to, due_date, status, recurring_task_id, created_at)
            VALUES (@Id, @HouseholdId, @Title, @Description, @AssignedTo, @DueDate, @Status, @RecurringTaskId, @CreatedAt)
            ON CONFLICT (id) DO UPDATE
                SET assigned_to = EXCLUDED.assigned_to,
                    status = EXCLUDED.status,
                    completed_at = CASE WHEN EXCLUDED.status = 1 THEN COALESCE(tasks.tasks.completed_at, now()) ELSE NULL END,
                    completed_by = EXCLUDED.completed_by
            """,
            new
            {
                task.Id, task.HouseholdId, task.Title, task.Description,
                task.AssignedTo, task.DueDate,
                Status = (int)task.Status,
                task.RecurringTaskId, task.CreatedAt,
                CompletedBy = (Guid?)null
            },
            uow.CurrentTransaction);
        return task.Id;
    }

    public async Task<HouseholdTask?> GetTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<HouseholdTask>(
            "SELECT id, household_id, title, description, assigned_to, due_date, status, recurring_task_id, created_at FROM tasks.tasks WHERE id = @taskId",
            new { taskId },
            uow.CurrentTransaction);
    }

    public async Task<bool> TryInsertTaskAsync(HouseholdTask task, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        var inserted = await conn.ExecuteAsync(
            """
            INSERT INTO tasks.tasks (id, household_id, title, description, assigned_to, due_date, status, recurring_task_id, created_at)
            VALUES (@Id, @HouseholdId, @Title, @Description, @AssignedTo, @DueDate, @Status, @RecurringTaskId, @CreatedAt)
            ON CONFLICT (id) DO NOTHING
            """,
            new
            {
                task.Id, task.HouseholdId, task.Title, task.Description, task.AssignedTo, task.DueDate,
                Status = (int)task.Status, task.RecurringTaskId, task.CreatedAt
            },
            uow.CurrentTransaction);
        return inserted > 0;
    }

    public async Task SaveRecurringTaskAsync(RecurringTask task, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO tasks.recurring_tasks
                (id, household_id, title, description, default_assigned_to, frequency, first_date, run_time, is_active, next_run_at)
            VALUES
                (@Id, @HouseholdId, @Title, @Description, @DefaultAssignedTo, @Frequency, @FirstDate::date, @RunTime::time, @IsActive, @NextRunAt)
            ON CONFLICT (id) DO UPDATE
                SET is_active   = EXCLUDED.is_active,
                    next_run_at = EXCLUDED.next_run_at
            """,
            new
            {
                task.Id, task.HouseholdId, task.Title, task.Description, task.DefaultAssignedTo,
                Frequency = task.Schedule.Frequency.ToString(),
                FirstDate = task.Schedule.FirstDate,
                RunTime = task.Schedule.Time,
                task.IsActive, task.NextRunAt
            },
            uow.CurrentTransaction);
    }

    public async Task<RecurringTask?> GetRecurringTaskAsync(Guid recurringTaskId, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<RecurringTaskRow>(
            """
            SELECT id, household_id AS HouseholdId, title, description, default_assigned_to AS DefaultAssignedTo,
                   frequency, first_date AS FirstDate, run_time AS RunTime, is_active AS IsActive, next_run_at AS NextRunAt
            FROM tasks.recurring_tasks WHERE id = @recurringTaskId
            FOR UPDATE
            """,
            new { recurringTaskId },
            uow.CurrentTransaction);
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<Guid>> GetDueRecurringTaskIdsAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        var ids = await conn.QueryAsync<Guid>(
            "SELECT id FROM tasks.recurring_tasks WHERE is_active AND next_run_at <= @now ORDER BY next_run_at, id",
            new { now },
            uow.CurrentTransaction);
        return ids.ToList();
    }

    public async Task DeleteTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        await conn.ExecuteAsync(
            "DELETE FROM tasks.tasks WHERE id = @taskId",
            new { taskId },
            uow.CurrentTransaction);
    }

    private sealed record RecurringTaskRow(
        Guid Id, Guid HouseholdId, string Title, string? Description, Guid? DefaultAssignedTo,
        string Frequency, DateOnly FirstDate, TimeOnly RunTime, bool IsActive, DateTimeOffset? NextRunAt)
    {
        // Templates deactivated before the cursor existed carry no next run; they never fire, so the first occurrence stands in.
        public RecurringTask ToDomain()
        {
            var schedule = new RecurrenceSchedule(
                Enum.Parse<RecurrenceFrequency>(Frequency), FirstDate, RunTime);
            return RecurringTask.Rehydrate(
                Id, HouseholdId, Title, Description, DefaultAssignedTo, schedule, IsActive, NextRunAt ?? schedule.At(0));
        }
    }
}
