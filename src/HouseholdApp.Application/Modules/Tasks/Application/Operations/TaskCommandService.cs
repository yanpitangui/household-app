using Cronos;
using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.Application.Modules.Tasks.Domain;
using HouseholdApp.Application.Modules.Tasks.Infrastructure.Jobs;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.Application.Shared.Scheduler;

namespace HouseholdApp.Application.Modules.Tasks.Application.Operations;

public sealed class TaskCommandService(
    ITaskRepository repo,
    IUnitOfWork uow,
    IEventBus eventBus,
    IRecurringJobScheduler scheduler,
    TimeProvider time,
    ICurrentUser currentUser) : ITaskCommands
{
    public async Task<Guid> CreateTaskAsync(
        Guid householdId, string title, string? description,
        Guid? assignedTo, DateTimeOffset? dueDate, CancellationToken ct = default)
    {
        var task = HouseholdTask.Create(householdId, title, description, assignedTo, dueDate, time.GetUtcNow());
        await uow.BeginTransactionAsync(ct);
        await repo.SaveTaskAsync(task, ct);
        eventBus.EnqueueAll(task);
        await uow.CommitAsync(ct);
        return task.Id;
    }

    public async Task AssignTaskAsync(Guid taskId, Guid userId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var task = await repo.GetTaskAsync(taskId, ct)
            ?? throw new InvalidOperationException("Task not found.");
        task.Assign(userId, time.GetUtcNow());
        await repo.SaveTaskAsync(task, ct);
        eventBus.EnqueueAll(task);
        await uow.CommitAsync(ct);
    }

    public async Task CompleteTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var task = await repo.GetTaskAsync(taskId, ct)
            ?? throw new InvalidOperationException("Task not found.");
        task.Complete(currentUser.Id, time.GetUtcNow());
        await repo.SaveTaskAsync(task, ct);
        eventBus.EnqueueAll(task);
        await uow.CommitAsync(ct);
    }

    public async Task UncompleteTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var task = await repo.GetTaskAsync(taskId, ct)
            ?? throw new InvalidOperationException("Task not found.");
        task.Uncomplete(time.GetUtcNow());
        await repo.SaveTaskAsync(task, ct);
        eventBus.EnqueueAll(task);
        await uow.CommitAsync(ct);
    }

    public async Task SpawnRecurringTaskAsync(Guid recurringTaskId, CancellationToken ct = default)
    {
        var recurring = await repo.GetRecurringTaskAsync(recurringTaskId, ct);
        if (recurring is null || !recurring.IsActive) return;

        var task = recurring.Spawn(time.GetUtcNow());
        await uow.BeginTransactionAsync(ct);
        await repo.SaveTaskAsync(task, ct);
        eventBus.EnqueueAll(task);
        await uow.CommitAsync(ct);
    }

    public async Task DeleteTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        await repo.DeleteTaskAsync(taskId, ct);
        await uow.CommitAsync(ct);
    }

    public async Task<Guid> CreateRecurringTaskAsync(
        Guid householdId, string title, string? description,
        Guid? defaultAssignedTo, string cronExpression, CancellationToken ct = default)
    {
        var schedule = CronExpression.Parse(cronExpression);
        var nextRun = schedule.GetNextOccurrence(time.GetUtcNow(), TimeZoneInfo.Utc);
        var recurring = RecurringTask.Create(householdId, title, description, defaultAssignedTo, cronExpression, nextRun);

        var schedulerJobId = await scheduler.ScheduleCronAsync(
            RecurringTaskJobs.FunctionName, cronExpression, recurring.Id, ct);
        recurring.SetSchedulerJobId(schedulerJobId);

        try
        {
            await uow.BeginTransactionAsync(ct);
            await repo.SaveRecurringTaskAsync(recurring, ct);
            await uow.CommitAsync(ct);
        }
        catch
        {
            await scheduler.UnscheduleCronAsync(schedulerJobId, ct);
            throw;
        }

        return recurring.Id;
    }

    public async Task DeactivateRecurringTaskAsync(Guid recurringTaskId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var recurring = await repo.GetRecurringTaskAsync(recurringTaskId, ct)
            ?? throw new InvalidOperationException("Recurring task not found.");

        recurring.Deactivate();
        await repo.SaveRecurringTaskAsync(recurring, ct);

        if (recurring.SchedulerJobId.HasValue)
            await scheduler.UnscheduleCronAsync(recurring.SchedulerJobId.Value, ct);

        await uow.CommitAsync(ct);
    }
}
