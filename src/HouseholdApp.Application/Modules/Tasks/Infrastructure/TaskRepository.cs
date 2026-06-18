using Dapper;
using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.Application.Modules.Tasks.Domain;
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

    public async Task SaveRecurringTaskAsync(RecurringTask task, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO tasks.recurring_tasks (id, household_id, title, description, default_assigned_to, cron_expression, is_active, next_run_at, scheduler_job_id)
            VALUES (@Id, @HouseholdId, @Title, @Description, @DefaultAssignedTo, @CronExpression, @IsActive, @NextRunAt, @SchedulerJobId)
            ON CONFLICT (id) DO UPDATE
                SET is_active        = EXCLUDED.is_active,
                    next_run_at      = EXCLUDED.next_run_at,
                    scheduler_job_id = EXCLUDED.scheduler_job_id
            """,
            new
            {
                task.Id, task.HouseholdId, task.Title, task.Description,
                task.DefaultAssignedTo, task.CronExpression, task.IsActive, task.NextRunAt, task.SchedulerJobId
            },
            uow.CurrentTransaction);
    }

    public async Task<RecurringTask?> GetRecurringTaskAsync(Guid recurringTaskId, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<RecurringTask>(
            "SELECT id, household_id, title, description, default_assigned_to, cron_expression, is_active, next_run_at, scheduler_job_id FROM tasks.recurring_tasks WHERE id = @recurringTaskId",
            new { recurringTaskId },
            uow.CurrentTransaction);
    }

    public async Task DeleteTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        var conn = await uow.GetConnectionAsync(ct);
        await conn.ExecuteAsync(
            "DELETE FROM tasks.tasks WHERE id = @taskId",
            new { taskId },
            uow.CurrentTransaction);
    }
}
