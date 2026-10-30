using HouseholdApp.Application.Modules.Tasks.Application.Ports;
using HouseholdApp.Application.Modules.Tasks.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Tasks.Infrastructure;

public sealed class RecurringTaskSweepJobTests
{
    [Test]
    public async Task A_template_that_fails_does_not_stop_the_ones_after_it()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var repo = Substitute.For<ITaskRepository>();
        repo.GetDueRecurringTaskIdsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { first, second });
        var commands = Substitute.For<ITaskCommands>();
        commands.SpawnRecurringTaskAsync(first, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));
        var provider = new ServiceCollection().AddSingleton(commands).BuildServiceProvider();
        var sut = new RecurringTaskSweepJob(
            repo, new FakeTimeProvider(), provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RecurringTaskSweepJob>.Instance);

        await sut.RunAsync(CancellationToken.None);

        await commands.Received(1).SpawnRecurringTaskAsync(second, Arg.Any<CancellationToken>());
    }
}
