using HouseholdApp.Application.Modules.Notifications;
using HouseholdApp.Application.Modules.Notifications.Application.Ports;
using HouseholdApp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HouseholdApp.IntegrationTests.Modules.Notifications;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class PushSubscriptionRepositoryTests(PostgresFixture db) : IAsyncDisposable
{
    private readonly ServiceProvider _provider = BuildProvider(db);

    private static ServiceProvider BuildProvider(PostgresFixture db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.DataSource);
        services.AddNotificationsModule(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    [Test]
    public async Task SubscribeAsync_then_GetForUserAsync_returns_the_subscription()
    {
        var userId = Guid.NewGuid();
        var endpoint = $"https://push.example/{Guid.NewGuid()}";
        await using var scope = _provider.CreateAsyncScope();
        var sut = scope.ServiceProvider.GetRequiredService<IPushSubscriptionCommands>();

        await sut.SubscribeAsync(userId, endpoint, "p256dh-key", "auth-key");
        var result = await sut.GetForUserAsync(userId);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Endpoint).IsEqualTo(endpoint);
        await Assert.That(result[0].P256dh).IsEqualTo("p256dh-key");
    }

    [Test]
    public async Task SubscribeAsync_with_same_endpoint_upserts_instead_of_duplicating()
    {
        var userId = Guid.NewGuid();
        var endpoint = $"https://push.example/{Guid.NewGuid()}";
        await using var scope = _provider.CreateAsyncScope();
        var sut = scope.ServiceProvider.GetRequiredService<IPushSubscriptionCommands>();

        await sut.SubscribeAsync(userId, endpoint, "old-key", "old-auth");
        await sut.SubscribeAsync(userId, endpoint, "new-key", "new-auth");
        var result = await sut.GetForUserAsync(userId);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].P256dh).IsEqualTo("new-key");
    }

    [Test]
    public async Task UnsubscribeAsync_removes_the_subscription()
    {
        var userId = Guid.NewGuid();
        var endpoint = $"https://push.example/{Guid.NewGuid()}";
        await using var scope = _provider.CreateAsyncScope();
        var sut = scope.ServiceProvider.GetRequiredService<IPushSubscriptionCommands>();
        await sut.SubscribeAsync(userId, endpoint, "key", "auth");

        await sut.UnsubscribeAsync(userId, endpoint);
        var result = await sut.GetForUserAsync(userId);

        await Assert.That(result.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DeleteByEndpointAsync_removes_regardless_of_user()
    {
        var userId = Guid.NewGuid();
        var endpoint = $"https://push.example/{Guid.NewGuid()}";
        await using var scope = _provider.CreateAsyncScope();
        var sut = scope.ServiceProvider.GetRequiredService<IPushSubscriptionCommands>();
        await sut.SubscribeAsync(userId, endpoint, "key", "auth");

        await sut.DeleteByEndpointAsync(endpoint);
        var result = await sut.GetForUserAsync(userId);

        await Assert.That(result.Count).IsEqualTo(0);
    }
}
