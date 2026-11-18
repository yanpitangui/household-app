using HouseholdApp.Application.Modules.Identity;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Persistence;
using HouseholdApp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HouseholdApp.IntegrationTests.Modules.Identity;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class UserLocaleTests(PostgresFixture db) : IAsyncDisposable
{
    private readonly ServiceProvider _provider = BuildProvider(db);

    private static ServiceProvider BuildProvider(PostgresFixture db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db.DataSource);
        services.AddSingleton(TimeProvider.System);
        services.AddPersistence();
        services.AddEventBus();
        services.AddFusionCache();
        services.AddIdentityModule();
        return services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    [Test]
    public async Task SetLocaleAsync_is_visible_through_the_cached_profile()
    {
        await using var scope = _provider.CreateAsyncScope();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        var query = scope.ServiceProvider.GetRequiredService<IUserQuery>();
        var locales = scope.ServiceProvider.GetRequiredService<IUserLocales>();
        var subject = $"sub-{Guid.NewGuid()}";
        await provisioning.ProvisionAsync(subject, $"{Guid.NewGuid()}@test.com", "Ana", null);

        var before = await query.GetBySubjectAsync(subject);
        await Assert.That(before!.Locale).IsNull();
        await query.GetByIdAsync(before.Id);

        await locales.SetLocaleAsync(before.Id, "pt-BR");

        await Assert.That((await query.GetByIdAsync(before.Id))!.Locale).IsEqualTo("pt-BR");
        await Assert.That((await query.GetBySubjectAsync(subject))!.Locale).IsEqualTo("pt-BR");
        var many = await query.GetByIdsAsync([before.Id]);
        await Assert.That(many.Single().Locale).IsEqualTo("pt-BR");
    }
}
