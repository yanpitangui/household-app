using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.Application.Modules.Tasks.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Tasks.Application.Operations;

public sealed class TaskCommandService(
    ITaskRepository repo,
    IUnitOfWork uow,
    IEventBus eventBus,
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
        await uow.BeginTransactionAsync(ct);
        var recurring = await repo.GetRecurringTaskAsync(recurringTaskId, ct);
        if (recurring is null || !recurring.IsActive)
        {
            await uow.CommitAsync(ct);
            return;
        }

        foreach (var task in recurring.SpawnDue(time.GetUtcNow()))
        {
            // A conflict is a retry of a date already created; its event went out then.
            if (await repo.TryInsertTaskAsync(task, ct))
                eventBus.EnqueueAll(task);
        }
        await repo.SaveRecurringTaskAsync(recurring, ct);
        await uow.CommitAsync(ct);
    }

    public async Task DeleteTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var task = await repo.GetTaskAsync(taskId, ct)
            ?? throw new InvalidOperationException("Task not found.");
        await repo.DeleteTaskAsync(taskId, ct);
        eventBus.Enqueue(new TaskDeleted(Guid.CreateVersion7(), time.GetUtcNow(), taskId, task.HouseholdId, currentUser.Id));
        await uow.CommitAsync(ct);
    }

    public async Task<Guid> CreateRecurringTaskAsync(
        Guid householdId, string title, string? description,
        Guid? defaultAssignedTo, RecurrenceSchedule schedule, CancellationToken ct = default)
    {
        var recurring = RecurringTask.Create(householdId, title, description, defaultAssignedTo, schedule, time.GetUtcNow());
        await uow.BeginTransactionAsync(ct);
        await repo.SaveRecurringTaskAsync(recurring, ct);
        await uow.CommitAsync(ct);
        return recurring.Id;
    }

    public async Task DeactivateRecurringTaskAsync(Guid householdId, Guid recurringTaskId, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(ct);
        var recurring = await repo.GetRecurringTaskAsync(recurringTaskId, ct);
        if (recurring is null || recurring.HouseholdId != householdId)
            throw new InvalidOperationException("Recurring task not found.");

        recurring.Deactivate();
        await repo.SaveRecurringTaskAsync(recurring, ct);
        await uow.CommitAsync(ct);
    }
}
