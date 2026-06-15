using Microsoft.Extensions.Time.Testing;
using HouseholdApp.Application.Modules.Tasks.Application.Operations;
using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.Application.Modules.Tasks.Domain;
using HouseholdApp.Application.Shared.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.Application.Shared.Scheduler;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Tasks.Application;

public sealed class TaskCommandServiceTests
{
    private readonly ITaskRepository _repo = Substitute.For<ITaskRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly IRecurringJobScheduler _scheduler = Substitute.For<IRecurringJobScheduler>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly TaskCommandService _sut;

    public TaskCommandServiceTests()
    {
        _currentUser.Id.Returns(Guid.NewGuid());
        _scheduler.ScheduleCronAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());
        _sut = new TaskCommandService(_repo, _uow, _eventBus, _scheduler, new FakeTimeProvider(), _currentUser);
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
    public async Task DeleteTaskAsync_calls_repo_delete()
    {
        var taskId = Guid.NewGuid();

        await _sut.DeleteTaskAsync(taskId);

        await _repo.Received(1).DeleteTaskAsync(taskId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateRecurringTaskAsync_returns_non_empty_id()
    {
        var id = await _sut.CreateRecurringTaskAsync(Guid.NewGuid(), "Weekly vacuum", null, null, "0 9 * * 1");

        await Assert.That(id).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task CreateRecurringTaskAsync_schedules_cron_with_correct_expression()
    {
        const string cron = "0 9 * * 1";

        await _sut.CreateRecurringTaskAsync(Guid.NewGuid(), "Weekly vacuum", null, null, cron);

        await _scheduler.Received(1).ScheduleCronAsync(
            Arg.Any<string>(), cron, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateRecurringTaskAsync_saves_task_with_scheduler_job_id()
    {
        var jobId = Guid.NewGuid();
        _scheduler.ScheduleCronAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(jobId);

        RecurringTask? saved = null;
        _repo.When(r => r.SaveRecurringTaskAsync(Arg.Any<RecurringTask>(), Arg.Any<CancellationToken>()))
            .Do(ci => saved = ci.Arg<RecurringTask>());

        await _sut.CreateRecurringTaskAsync(Guid.NewGuid(), "Weekly vacuum", null, null, "0 9 * * 1");

        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.SchedulerJobId).IsEqualTo(jobId);
    }

    [Test]
    public async Task CreateRecurringTaskAsync_unschedules_job_when_db_save_fails()
    {
        var jobId = Guid.NewGuid();
        _scheduler.ScheduleCronAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(jobId);
        _uow.CommitAsync(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new InvalidOperationException("DB error")));

        await Assert.That(async () => await _sut.CreateRecurringTaskAsync(
                Guid.NewGuid(), "Weekly vacuum", null, null, "0 9 * * 1"))
            .Throws<InvalidOperationException>();

        await _scheduler.Received(1).UnscheduleCronAsync(jobId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeactivateRecurringTaskAsync_throws_when_not_found()
    {
        _repo.GetRecurringTaskAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((RecurringTask?)null);

        await Assert.That(async () => await _sut.DeactivateRecurringTaskAsync(Guid.NewGuid()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DeactivateRecurringTaskAsync_marks_task_inactive()
    {
        var task = RecurringTask.Create(Guid.NewGuid(), "Daily sweep", null, null, "0 8 * * *", null);
        _repo.GetRecurringTaskAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        await _sut.DeactivateRecurringTaskAsync(task.Id);

        await Assert.That(task.IsActive).IsFalse();
    }

    [Test]
    public async Task DeactivateRecurringTaskAsync_unschedules_when_scheduler_job_exists()
    {
        var jobId = Guid.NewGuid();
        var task = RecurringTask.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "Daily sweep", null, null, "0 8 * * *", true, null, jobId);
        _repo.GetRecurringTaskAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        await _sut.DeactivateRecurringTaskAsync(task.Id);

        await _scheduler.Received(1).UnscheduleCronAsync(jobId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeactivateRecurringTaskAsync_skips_unschedule_when_no_scheduler_job()
    {
        var task = RecurringTask.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "Daily sweep", null, null, "0 8 * * *", true, null, null);
        _repo.GetRecurringTaskAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        await _sut.DeactivateRecurringTaskAsync(task.Id);

        await _scheduler.DidNotReceive().UnscheduleCronAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SpawnRecurringTaskAsync_does_nothing_when_recurring_task_not_found()
    {
        _repo.GetRecurringTaskAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RecurringTask?)null);

        await _sut.SpawnRecurringTaskAsync(Guid.NewGuid());

        await _repo.DidNotReceive().SaveTaskAsync(Arg.Any<HouseholdTask>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SpawnRecurringTaskAsync_does_nothing_when_recurring_task_inactive()
    {
        var recurring = RecurringTask.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "Daily sweep", null, null, "0 8 * * *", false, null, null);
        _repo.GetRecurringTaskAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);

        await _sut.SpawnRecurringTaskAsync(recurring.Id);

        await _repo.DidNotReceive().SaveTaskAsync(Arg.Any<HouseholdTask>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SpawnRecurringTaskAsync_saves_spawned_task_and_publishes_events()
    {
        var recurring = RecurringTask.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "Daily sweep", null, null, "0 8 * * *", true, null, null);
        _repo.GetRecurringTaskAsync(recurring.Id, Arg.Any<CancellationToken>()).Returns(recurring);

        await _sut.SpawnRecurringTaskAsync(recurring.Id);

        await _repo.Received(1).SaveTaskAsync(Arg.Any<HouseholdTask>(), Arg.Any<CancellationToken>());
        await _eventBus.Received(1).PublishAsync(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }
}
