using Dapper;
using HouseholdApp.Application.Modules.Identity;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using HouseholdApp.Application.Modules.Identity.Domain;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HouseholdApp.IntegrationTests.Modules.Identity;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class UserRepositoryProvisioningTests(PostgresFixture db) : IAsyncDisposable
{
    private readonly ServiceProvider _provider = BuildProvider(db);

    private sealed class CapturingUserProvisionedHandler : IEventHandler<UserProvisioned>
    {
        public UserProvisioned? LastEvent { get; private set; }

        public Task HandleAsync(UserProvisioned evt, CancellationToken ct = default)
        {
            LastEvent = evt;
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider BuildProvider(PostgresFixture db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.DataSource);
        services.AddSingleton(TimeProvider.System);
        services.AddPersistence();
        services.AddEventBus();
        services.AddFusionCache();
        services.AddIdentityModule();
        services.AddEventHandler<UserProvisioned, CapturingUserProvisionedHandler>();
        return services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    [Test]
    public async Task ProvisionAsync_new_user_is_queryable_afterward_and_publishes_its_id()
    {
        await using var scope = _provider.CreateAsyncScope();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        var query = scope.ServiceProvider.GetRequiredService<IUserQuery>();
        var subject = $"sub-{Guid.NewGuid()}";

        await provisioning.ProvisionAsync(subject, $"{Guid.NewGuid()}@test.com", "New User", null);

        var profile = await query.GetBySubjectAsync(subject);
        await Assert.That(profile).IsNotNull();
        await Assert.That(profile!.DisplayName).IsEqualTo("New User");

        var captured = scope.ServiceProvider.GetRequiredService<CapturingUserProvisionedHandler>();
        await Assert.That(captured.LastEvent).IsNotNull();
        await Assert.That(captured.LastEvent!.UserId).IsEqualTo(profile.Id);
        await Assert.That(captured.LastEvent!.Subject).IsEqualTo(subject);
    }

    [Test]
    public async Task ProvisionAsync_existing_user_matched_by_email_keeps_original_id_and_publishes_it()
    {
        var existingId = Guid.NewGuid();
        var email = $"existing-{Guid.NewGuid()}@test.com";
        await using (var conn = await db.DataSource.OpenConnectionAsync())
        {
            await conn.ExecuteAsync(
                "INSERT INTO identity.users (id, subject, email, display_name, created_at, last_login_at) VALUES (@id, @subject, @email, 'Old Name', now(), now())",
                new { id = existingId, subject = $"old-sub-{Guid.NewGuid()}", email });
        }

        await using var scope = _provider.CreateAsyncScope();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        var newSubject = $"new-sub-{Guid.NewGuid()}";

        await provisioning.ProvisionAsync(newSubject, email, "Refreshed Name", null);

        await using var readConn = await db.DataSource.OpenConnectionAsync();
        var row = await readConn.QuerySingleAsync<(string Subject, string DisplayName)>(
            "SELECT subject, display_name AS DisplayName FROM identity.users WHERE id = @id", new { id = existingId });

        await Assert.That(row.Subject).IsEqualTo(newSubject);
        await Assert.That(row.DisplayName).IsEqualTo("Refreshed Name");

        var captured = scope.ServiceProvider.GetRequiredService<CapturingUserProvisionedHandler>();
        await Assert.That(captured.LastEvent).IsNotNull();
        await Assert.That(captured.LastEvent!.UserId).IsEqualTo(existingId);
        await Assert.That(captured.LastEvent!.Subject).IsEqualTo(newSubject);
    }

    [Test]
    public async Task ProvisionAsync_subject_conflict_with_different_email_keeps_existing_id_and_publishes_it()
    {
        var existingId = Guid.NewGuid();
        var existingSubject = $"sub-{Guid.NewGuid()}";
        await using (var conn = await db.DataSource.OpenConnectionAsync())
        {
            await conn.ExecuteAsync(
                "INSERT INTO identity.users (id, subject, email, display_name, created_at, last_login_at) VALUES (@id, @subject, @email, 'Old Name', now(), now())",
                new { id = existingId, subject = existingSubject, email = $"old-{Guid.NewGuid()}@test.com" });
        }

        await using var scope = _provider.CreateAsyncScope();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        var newEmail = $"new-{Guid.NewGuid()}@test.com";

        await provisioning.ProvisionAsync(existingSubject, newEmail, "Updated Name", null);

        await using var readConn = await db.DataSource.OpenConnectionAsync();
        var row = await readConn.QuerySingleAsync<(string Email, string DisplayName)>(
            "SELECT email, display_name AS DisplayName FROM identity.users WHERE id = @id", new { id = existingId });

        await Assert.That(row.Email).IsEqualTo(newEmail);
        await Assert.That(row.DisplayName).IsEqualTo("Updated Name");

        var captured = scope.ServiceProvider.GetRequiredService<CapturingUserProvisionedHandler>();
        await Assert.That(captured.LastEvent).IsNotNull();
        await Assert.That(captured.LastEvent!.UserId).IsEqualTo(existingId);
        await Assert.That(captured.LastEvent!.Subject).IsEqualTo(existingSubject);
    }
}
