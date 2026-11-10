using HouseholdApp.Application.Modules.Households;
using HouseholdApp.Application.Modules.Households.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Valtuutus.Core.Configuration;
using Valtuutus.Data.Postgres;

namespace HouseholdApp.IntegrationTests.Modules.Households;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class HouseholdRepositoryTests(PostgresFixture db) : IAsyncDisposable
{
    private readonly ServiceProvider _provider = BuildProvider(db);

    private static ServiceProvider BuildProvider(PostgresFixture db)
    {
        var schemaStream = typeof(HouseholdsModule).Assembly
            .GetManifestResourceStream("HouseholdApp.Application.Shared.Authorization.schema.vtt")!;

        var services = new ServiceCollection();
        services.AddSingleton(db.DataSource);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddScoped<MutableCurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<MutableCurrentUser>());
        services.AddPersistence();
        services.AddFusionCache();
        services.AddHouseholdsModule();
        services.AddEventBus();
        services.AddValtuutusCore(schemaStream);
        services.AddPostgres(
            _ => () => new NpgsqlConnection(db.ConnectionString),
            new ValtuutusPostgresOptions { Schema = "authz" });
        services.AddScoped<IHouseholdGuard, ValtuutusHouseholdGuard>();
        return services.BuildServiceProvider();
    }

    private AsyncServiceScope Scope(Guid userId)
    {
        var scope = _provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().Id = userId;
        return scope;
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    [Test]
    public async Task Removed_member_is_no_longer_listed()
    {
        var ana = Guid.NewGuid(); var bia = Guid.NewGuid();
        var id = await CreateWithMemberAsync(ana, bia);

        await using (var s = Scope(ana))
            await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().RemoveMemberAsync(id, bia);

        await using var q = Scope(ana);
        var members = await q.ServiceProvider.GetRequiredService<IHouseholdQueries>().GetMembersAsync(id);
        await Assert.That(members.Select(m => m.UserId)).DoesNotContain(bia);
    }

    [Test]
    public async Task Rename_and_time_zone_are_persisted()
    {
        var ana = Guid.NewGuid();
        Guid id;
        await using (var s = Scope(ana))
        {
            var commands = s.ServiceProvider.GetRequiredService<IHouseholdCommands>();
            id = await commands.CreateAsync("Casa", "America/Sao_Paulo");
            await commands.RenameAsync(id, "Casa Nova");
            await commands.ChangeTimeZoneAsync(id, "Europe/Lisbon");
        }
        await using var q = Scope(ana);
        var detail = await q.ServiceProvider.GetRequiredService<IHouseholdQueries>().GetAsync(id);
        await Assert.That(detail!.Name).IsEqualTo("Casa Nova");
        await Assert.That(detail.TimeZone).IsEqualTo("Europe/Lisbon");
    }

    [Test]
    public async Task Transfer_ownership_persists_both_roles()
    {
        var ana = Guid.NewGuid(); var bia = Guid.NewGuid();
        var id = await CreateWithMemberAsync(ana, bia);
        await using (var s = Scope(ana))
            await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().TransferOwnershipAsync(id, bia);

        await using var q = Scope(ana);
        var members = await q.ServiceProvider.GetRequiredService<IHouseholdQueries>().GetMembersAsync(id);
        await Assert.That(members.Single(m => m.UserId == bia).Role).IsEqualTo("Owner");
        await Assert.That(members.Single(m => m.UserId == ana).Role).IsEqualTo("Admin");
    }

    [Test]
    public async Task Pending_invitations_are_listed_until_revoked()
    {
        var ana = Guid.NewGuid();
        Guid id;
        await using (var s = Scope(ana))
        {
            var commands = s.ServiceProvider.GetRequiredService<IHouseholdCommands>();
            id = await commands.CreateAsync("Casa", "America/Sao_Paulo");
            await commands.InviteAsync(id);
        }
        await using var q = Scope(ana);
        var queries = q.ServiceProvider.GetRequiredService<IHouseholdQueries>();
        var pending = await queries.ListPendingInvitationsAsync(id);
        await Assert.That(pending.Count).IsEqualTo(1);

        await q.ServiceProvider.GetRequiredService<IHouseholdCommands>().RevokeInvitationAsync(id, pending[0].Id);
        await Assert.That((await queries.ListPendingInvitationsAsync(id)).Count).IsEqualTo(0);
    }

    private async Task<Guid> CreateWithMemberAsync(Guid owner, Guid member)
    {
        await SeedUserAsync(owner, "Ana");
        await SeedUserAsync(member, "Bia");
        Guid id; string token;
        await using (var s = Scope(owner))
        {
            var commands = s.ServiceProvider.GetRequiredService<IHouseholdCommands>();
            id = await commands.CreateAsync("Casa", "America/Sao_Paulo");
            token = await commands.InviteAsync(id);
        }
        await using (var s = Scope(member))
            await s.ServiceProvider.GetRequiredService<IHouseholdCommands>().AcceptInvitationAsync(token);
        return id;
    }

    // Members are listed through a join on identity.users, so each persona needs a row.
    private async Task SeedUserAsync(Guid id, string name)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO identity.users (id, subject, email, display_name) VALUES (@id, @subject, @email, @name)", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("subject", id.ToString());
        cmd.Parameters.AddWithValue("email", $"{id}@example.com");
        cmd.Parameters.AddWithValue("name", name);
        await cmd.ExecuteNonQueryAsync();
    }
}
