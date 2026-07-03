using System.Net;
using HouseholdApp.Application.Modules.Notifications.Application.Ports;
using HouseholdApp.Application.Modules.Notifications.Infrastructure;
using NSubstitute;

namespace HouseholdApp.UnitTests.Modules.Notifications;

public sealed class WebPushSenderTests
{
    private readonly IPushSubscriptionCommands _subscriptions = Substitute.For<IPushSubscriptionCommands>();
    private readonly IWebPushClientAdapter _client = Substitute.For<IWebPushClientAdapter>();
    private readonly WebPushSender _sut;

    public WebPushSenderTests()
    {
        _sut = new WebPushSender(_subscriptions, _client);
    }

    [Test]
    public async Task SendAsync_sends_to_every_subscription_for_the_user()
    {
        var userId = Guid.NewGuid();
        var subs = new List<PushSubscriptionInfo>
        {
            new(userId, "endpoint-1", "key-1", "auth-1"),
            new(userId, "endpoint-2", "key-2", "auth-2")
        };
        _subscriptions.GetForUserAsync(userId, Arg.Any<CancellationToken>()).Returns(subs);

        await _sut.SendAsync(userId, "Title", "Body", "/some/url");

        await _client.Received(1).SendAsync(subs[0], Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _client.Received(1).SendAsync(subs[1], Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendAsync_deletes_subscription_on_410_Gone()
    {
        var userId = Guid.NewGuid();
        var dead = new PushSubscriptionInfo(userId, "dead-endpoint", "key", "auth");
        _subscriptions.GetForUserAsync(userId, Arg.Any<CancellationToken>()).Returns([dead]);
        _client.SendAsync(dead, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new PushDeliveryException(HttpStatusCode.Gone));

        await _sut.SendAsync(userId, "Title", "Body", "/some/url");

        await _subscriptions.Received(1).DeleteByEndpointAsync("dead-endpoint", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendAsync_deletes_subscription_on_404_NotFound()
    {
        var userId = Guid.NewGuid();
        var dead = new PushSubscriptionInfo(userId, "dead-endpoint", "key", "auth");
        _subscriptions.GetForUserAsync(userId, Arg.Any<CancellationToken>()).Returns([dead]);
        _client.SendAsync(dead, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new PushDeliveryException(HttpStatusCode.NotFound));

        await _sut.SendAsync(userId, "Title", "Body", "/some/url");

        await _subscriptions.Received(1).DeleteByEndpointAsync("dead-endpoint", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendAsync_does_not_delete_subscription_on_other_failures()
    {
        var userId = Guid.NewGuid();
        var sub = new PushSubscriptionInfo(userId, "endpoint", "key", "auth");
        _subscriptions.GetForUserAsync(userId, Arg.Any<CancellationToken>()).Returns([sub]);
        _client.SendAsync(sub, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new PushDeliveryException(HttpStatusCode.InternalServerError));

        await _sut.SendAsync(userId, "Title", "Body", "/some/url");

        await _subscriptions.DidNotReceive().DeleteByEndpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendAsync_one_dead_subscription_does_not_block_others()
    {
        var userId = Guid.NewGuid();
        var dead = new PushSubscriptionInfo(userId, "dead", "key", "auth");
        var alive = new PushSubscriptionInfo(userId, "alive", "key", "auth");
        _subscriptions.GetForUserAsync(userId, Arg.Any<CancellationToken>()).Returns([dead, alive]);
        _client.SendAsync(dead, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new PushDeliveryException(HttpStatusCode.Gone));

        await _sut.SendAsync(userId, "Title", "Body", "/some/url");

        await _client.Received(1).SendAsync(alive, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendAsync_network_failure_on_one_subscription_does_not_block_others_or_delete_it()
    {
        // Simulates what WebPushClientAdapter translates a raw transport-level failure
        // (HttpRequestException, timeout, DNS error) into: a PushDeliveryException with
        // HttpStatusCode.ServiceUnavailable, since there's no real HTTP status for it.
        var userId = Guid.NewGuid();
        var unreachable = new PushSubscriptionInfo(userId, "unreachable", "key", "auth");
        var alive = new PushSubscriptionInfo(userId, "alive", "key", "auth");
        _subscriptions.GetForUserAsync(userId, Arg.Any<CancellationToken>()).Returns([unreachable, alive]);
        _client.SendAsync(unreachable, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new PushDeliveryException(HttpStatusCode.ServiceUnavailable));

        await _sut.SendAsync(userId, "Title", "Body", "/some/url");

        await _client.Received(1).SendAsync(alive, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _subscriptions.DidNotReceive().DeleteByEndpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
