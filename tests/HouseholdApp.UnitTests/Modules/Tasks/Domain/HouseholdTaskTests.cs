using HouseholdApp.Application.Modules.Tasks.Domain;
using DomainTaskStatus = HouseholdApp.Application.Modules.Tasks.Domain.TaskStatus;

namespace HouseholdApp.UnitTests.Modules.Tasks.Domain;

public sealed class HouseholdTaskTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static HouseholdTask CreateTask() =>
        HouseholdTask.Create(HouseholdId, "Clean kitchen", null, null, null, Now);

    [Test]
    public async Task Create_sets_properties_and_raises_event()
    {
        var task = CreateTask();

        await Assert.That(task.HouseholdId).IsEqualTo(HouseholdId);
        await Assert.That(task.Title).IsEqualTo("Clean kitchen");
        await Assert.That(task.Status).IsEqualTo(DomainTaskStatus.Pending);
        await Assert.That(task.AssignedTo).IsNull();
        await Assert.That(task.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(task.DomainEvents[0] is TaskCreated).IsTrue();
    }

    [Test]
    public async Task Assign_sets_assignee_and_raises_event()
    {
        var task = CreateTask();
        task.ClearEvents();

        task.Assign(UserId, Now);

        await Assert.That(task.AssignedTo).IsEqualTo(UserId);
        await Assert.That(task.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(task.DomainEvents[0] is TaskAssigned).IsTrue();
    }

    [Test]
    public async Task Complete_marks_completed_and_raises_event()
    {
        var task = CreateTask();
        task.ClearEvents();

        task.Complete(UserId, Now);

        await Assert.That(task.Status).IsEqualTo(DomainTaskStatus.Completed);
        await Assert.That(task.DomainEvents.Count).IsEqualTo(1);
        await Assert.That(task.DomainEvents[0] is TaskCompleted).IsTrue();
    }

    [Test]
    public async Task Complete_already_completed_task_throws()
    {
        var task = CreateTask();
        task.Complete(UserId, Now);

        await Assert.That(() => task.Complete(UserId, Now)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RecurringTask_Spawn_creates_task_linked_to_recurring()
    {
        var recurring = RecurringTask.Create(HouseholdId, "Weekly cleaning", null, UserId, "0 9 * * 1", Now);

        var spawned = recurring.Spawn(Now);

        await Assert.That(spawned.HouseholdId).IsEqualTo(HouseholdId);
        await Assert.That(spawned.Title).IsEqualTo("Weekly cleaning");
        await Assert.That(spawned.AssignedTo).IsEqualTo(UserId);
        await Assert.That(spawned.RecurringTaskId).IsEqualTo(recurring.Id);
    }
}
