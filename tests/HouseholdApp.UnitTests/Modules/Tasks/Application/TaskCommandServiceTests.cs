using Microsoft.Extensions.Time.Testing;
using HouseholdApp.Application.Modules.Tasks.Application.Operations;
using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.Application.Modules.Tasks.Domain;
using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Tasks.Application;

public sealed class TaskCommandServiceTests
{
    private readonly ITaskRepository _repo = Substitute.For<ITaskRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly TaskCommandService _sut;

    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static RecurrenceSchedule Weekly(int day = 12) =>
        new(RecurrenceFrequency.Weekly, new DateOnly(2026, 11, day), new TimeOnly(9, 0));

    public TaskCommandServiceTests()
    {
        _currentUser.Id.Returns(Guid.NewGuid());
        _sut = new TaskCommandService(_repo, _uow, _eventBus, new FakeTimeProvider(Now), _currentUser);
    }

    [Test]
    public async Task CreateTaskAsync_returns_non_empty_id()
    {
        var id = await _sut.CreateTaskAsync(Guid.NewGuid(), "Clean kitchen", null, null, null);

        await Assert.That(id).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task AssignTaskAsync_throws_when_task_not_found()
    {
        _repo.GetTaskAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((HouseholdTask?)null);

        await Assert.That(async () => await _sut.AssignTaskAsync(Guid.NewGuid(), Guid.NewGuid()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AssignTaskAsync_sets_assignee_on_task()
    {
        var task = HouseholdTask.Create(Guid.NewGuid(), "Fix leak", null, null, null, DateTimeOffset.UtcNow);
        var assigneeId = Guid.NewGuid();
        _repo.GetTaskAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        await _sut.AssignTaskAsync(task.Id, assigneeId);

        await Assert.That(task.AssignedTo).IsEqualTo(assigneeId);
    }

    [Test]
    public async Task CompleteTaskAsync_throws_when_task_not_found()
    {
        _repo.GetTaskAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((HouseholdTask?)null);

        await Assert.That(async () => await _sut.CompleteTaskAsync(Guid.NewGuid()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CompleteTaskAsync_marks_task_as_completed()
    {
        var task = HouseholdTask.Create(Guid.NewGuid(), "Take out trash", null, null, null, DateTimeOffset.UtcNow);
        task.ClearEvents();
        _repo.GetTaskAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        await _sut.CompleteTaskAsync(task.Id);

        await Assert.That(task.Status).IsEqualTo(HouseholdApp.Application.Modules.Tasks.Domain.TaskStatus.Completed);
    }

    [Test]
    public async Task CompleteTaskAsync_throws_when_already_completed()
    {
        var task = HouseholdTask.Create(Guid.NewGuid(), "Sweep", null, null, null, DateTimeOffset.UtcNow);
        task.Complete(Guid.NewGuid(), DateTimeOffset.UtcNow);
        task.ClearEvents();
        _repo.GetTaskAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        await Assert.That(async () => await _sut.CompleteTaskAsync(task.Id))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UncompleteTaskAsync_throws_when_task_not_found()
    {
        _repo.GetTaskAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((HouseholdTask?)null);

        await Assert.That(async () => await _sut.UncompleteTaskAsync(Guid.NewGuid()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UncompleteTaskAsync_marks_task_as_pending()
    {
        var task = HouseholdTask.Create(Guid.NewGuid(), "Take out trash", null, null, null, DateTimeOffset.UtcNow);
        task.Complete(Guid.NewGuid(), DateTimeOffset.UtcNow);
        task.ClearEvents();
        _repo.GetTaskAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        await _sut.UncompleteTaskAsync(task.Id);

        await Assert.That(task.Status).IsEqualTo(HouseholdApp.Application.Modules.Tasks.Domain.TaskStatus.Pending);
    }

    [Test]
    public async Task DeleteTaskAsync_throws_when_task_not_found()
    {
        _repo.GetTaskAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((HouseholdTask?)null);

        await Assert.That(async () => await _sut.DeleteTaskAsync(Guid.NewGuid()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DeleteTaskAsync_calls_repo_delete()
    {
        var task = HouseholdTask.Create(Guid.NewGuid(), "Clean kitchen", null, null, null, DateTimeOffset.UtcNow);
        _repo.GetTaskAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        await _sut.DeleteTaskAsync(task.Id);

        await _repo.Received(1).DeleteTaskAsync(task.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteTaskAsync_enqueues_TaskDeleted_event()
    {
        var task = HouseholdTask.Create(Guid.NewGuid(), "Clean kitchen", null, null, null, DateTimeOffset.UtcNow);
        _repo.GetTaskAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        await _sut.DeleteTaskAsync(task.Id);

        _eventBus.Received(1).Enqueue(Arg.Is<IDomainEvent>(
            e => e.GetType() == typeof(TaskDeleted)
                 && ((TaskDeleted)e).TaskId == task.Id
                 && ((TaskDeleted)e).HouseholdId == task.HouseholdId));
    }

    [Test]
    public async Task CreateRecurringTaskAsync_returns_the_saved_template_id()
    {
        RecurringTask? saved = null;
        _repo.When(r => r.SaveRecurringTaskAsync(Arg.Any<RecurringTask>(), Arg.Any<CancellationToken>()))
            .Do(ci => saved = ci.Arg<RecurringTask>());

        var id = await _sut.CreateRecurringTaskAsync(Guid.NewGuid(), "Weekly vacuum", null, null, Weekly());

        await Assert.That(saved!.Id).IsEqualTo(id);
        await _uow.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateRecurringTaskAsync_saves_nothing_for_a_first_date_in_the_past()
    {
        await Assert.That(async () => await _sut.CreateRecurringTaskAsync(
                Guid.NewGuid(), "Weekly vacuum", null, null, Weekly(9)))
            .Throws<InvalidOperationException>();

        await _repo.DidNotReceiveWithAnyArgs().SaveRecurringTaskAsync(default!, default);
    }

    [Test]
    public async Task DeactivateRecurringTaskAsync_throws_when_not_found()
    {
        _repo.GetRecurringTaskAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((RecurringTask?)null);

        await Assert.That(async () => await _sut.DeactivateRecurringTaskAsync(Guid.NewGuid(), Guid.NewGuid()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DeactivateRecurringTaskAsync_throws_for_another_household()
    {
        var task = RecurringTask.Create(Guid.NewGuid(), "Daily sweep", null, null, Weekly(), Now);
        _repo.GetRecurringTaskAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        await Assert.That(async () => await _sut.DeactivateRecurringTaskAsync(Guid.NewGuid(), task.Id))
            .Throws<InvalidOperationException>();

        await _repo.DidNotReceiveWithAnyArgs().SaveRecurringTaskAsync(default!, default);
    }

    [Test]
    public async Task DeactivateRecurringTaskAsync_marks_task_inactive()
    {
        var task = RecurringTask.Create(Guid.NewGuid(), "Daily sweep", null, null, Weekly(), Now);
        _repo.GetRecurringTaskAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        await _sut.DeactivateRecurringTaskAsync(task.HouseholdId, task.Id);

        await Assert.That(task.IsActive).IsFalse();
        await _repo.Received(1).SaveRecurringTaskAsync(task, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SpawnRecurringTaskAsync_does_nothing_when_recurring_task_not_found()
    {
        _repo.GetRecurringTaskAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RecurringTask?)null);

        await _sut.SpawnRecurringTaskAsync(Guid.NewGuid());

        await _repo.DidNotReceive().TryInsertTaskAsync(Arg.Any<HouseholdTask>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SpawnRecurringTaskAsync_does_nothing_when_recurring_task_inactive()
    {
        var recurring = RecurringTask.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "Daily sweep", null, null, Weekly(10), false, Now);
        _repo.GetRecurringTaskAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);

        await _sut.SpawnRecurringTaskAsync(recurring.Id);

        await _repo.DidNotReceive().TryInsertTaskAsync(Arg.Any<HouseholdTask>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SpawnRecurringTaskAsync_inserts_the_due_task_publishes_its_events_and_saves_the_cursor()
    {
        var recurring = RecurringTask.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "Daily sweep", null, null, Weekly(10), true, Now);
        _repo.GetRecurringTaskAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);
        _repo.TryInsertTaskAsync(Arg.Any<HouseholdTask>(), Arg.Any<CancellationToken>()).Returns(true);

        await _sut.SpawnRecurringTaskAsync(recurring.Id);

        await _repo.Received(1).TryInsertTaskAsync(Arg.Any<HouseholdTask>(), Arg.Any<CancellationToken>());
        _eventBus.Received(1).Enqueue(Arg.Any<IDomainEvent>());
        await _repo.Received(1).SaveRecurringTaskAsync(recurring, Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SpawnRecurringTaskAsync_publishes_nothing_for_a_date_already_created()
    {
        var recurring = RecurringTask.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "Daily sweep", null, null, Weekly(10), true, Now);
        _repo.GetRecurringTaskAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);
        _repo.TryInsertTaskAsync(Arg.Any<HouseholdTask>(), Arg.Any<CancellationToken>()).Returns(false);

        await _sut.SpawnRecurringTaskAsync(recurring.Id);

        _eventBus.DidNotReceive().Enqueue(Arg.Any<IDomainEvent>());
        await _repo.Received(1).SaveRecurringTaskAsync(recurring, Arg.Any<CancellationToken>());
    }
}
