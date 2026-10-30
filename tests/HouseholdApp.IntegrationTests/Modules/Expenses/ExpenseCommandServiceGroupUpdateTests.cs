using HouseholdApp.Application.Modules.Expenses.Application.Operations;
using HouseholdApp.Application.Modules.Expenses.Domain;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using HouseholdApp.IntegrationTests.Infrastructure;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HouseholdApp.IntegrationTests.Modules.Expenses;

[ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
public sealed class ExpenseCommandServiceGroupUpdateTests(PostgresFixture db)
{
    private IDocumentStore Store => ExpenseDocumentStore.For(db.ConnectionString);

    private static ExpenseCommandService BuildSut(IDocumentSession session, Guid actorId) => new(
        session,
        new FakeCurrentUser(actorId),
        new FakeTimeProvider());

    private async Task<(Guid HouseholdId, Guid GroupId)> CreateGroupAsync(string name, string? icon = null)
    {
        var householdId = Guid.NewGuid();
        await using var s = Store.LightweightSession();
        var groupId = await BuildSut(s, Guid.NewGuid()).CreateExpenseGroupAsync(householdId, name, null, icon);
        return (householdId, groupId);
    }

    [Test]
    public async Task Create_stores_the_picked_icon_or_the_default()
    {
        var (_, picked) = await CreateGroupAsync("Trip", "✈️");
        var (_, plain) = await CreateGroupAsync("Home");

        await using var qs = Store.QuerySession();
        await Assert.That((await qs.LoadAsync<ExpenseGroupDocument>(picked))!.Icon).IsEqualTo("✈️");
        await Assert.That((await qs.LoadAsync<ExpenseGroupDocument>(plain))!.Icon).IsEqualTo(GroupIcons.Default);
    }

    [Test]
    public async Task Update_stores_the_new_values_and_appends_an_event_with_old_and_new()
    {
        var (householdId, groupId) = await CreateGroupAsync("Trip", "✈️");
        var actorId = Guid.NewGuid();

        await using (var s = Store.LightweightSession())
            await BuildSut(s, actorId).UpdateExpenseGroupAsync(householdId, groupId, "  Japan 2027 ", "Spring", "🗾");

        await using var qs = Store.QuerySession();
        var doc = await qs.LoadAsync<ExpenseGroupDocument>(groupId);
        await Assert.That((doc!.Name, doc.Description, doc.Icon)).IsEqualTo(("Japan 2027", "Spring", "🗾"));
        var updated = (await qs.Events.FetchStreamAsync(groupId)).Select(e => e.Data).OfType<ExpenseGroupUpdated>().Single();
        await Assert.That((updated.OldName, updated.NewName)).IsEqualTo(("Trip", "Japan 2027"));
        await Assert.That((updated.OldIcon, updated.NewIcon)).IsEqualTo(("✈️", "🗾"));
        await Assert.That(updated.PerformedByUserId).IsEqualTo(actorId);
    }

    [Test]
    public async Task Update_without_changes_appends_nothing()
    {
        var (householdId, groupId) = await CreateGroupAsync("Trip", "✈️");

        await using (var s = Store.LightweightSession())
            await BuildSut(s, Guid.NewGuid()).UpdateExpenseGroupAsync(householdId, groupId, "Trip ", null, "✈️");

        await using var qs = Store.QuerySession();
        await Assert.That((await qs.Events.FetchStreamAsync(groupId)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Update_treats_legacy_untrimmed_values_as_unchanged()
    {
        var householdId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        await using (var seed = Store.LightweightSession())
        {
            seed.Store(new ExpenseGroupDocument
            {
                Id = groupId, HouseholdId = householdId, Name = "Trip ", Description = "", Icon = "💳"
            });
            await seed.SaveChangesAsync();
        }

        await using (var s = Store.LightweightSession())
            await BuildSut(s, Guid.NewGuid()).UpdateExpenseGroupAsync(householdId, groupId, "Trip", null, "💳");

        await using var qs = Store.QuerySession();
        await Assert.That((await qs.Events.FetchStreamAsync(groupId)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Update_racing_a_delete_keeps_the_group_deleted()
    {
        var (householdId, groupId) = await CreateGroupAsync("Trip");

        await using var editing = Store.IdentitySession();
        var sut = BuildSut(editing, Guid.NewGuid());
        // The identity map keeps the document loaded before the delete, as an edit in flight would see it.
        await editing.LoadAsync<ExpenseGroupDocument>(groupId);
        await using (var deleting = Store.LightweightSession())
            await BuildSut(deleting, Guid.NewGuid()).DeleteExpenseGroupAsync(householdId, groupId);

        await sut.UpdateExpenseGroupAsync(householdId, groupId, "Japan", null, "🗾");

        await using var qs = Store.QuerySession();
        var doc = await qs.LoadAsync<ExpenseGroupDocument>(groupId);
        await Assert.That(doc!.DeletedAt).IsNotNull();
        await Assert.That(doc.Name).IsEqualTo("Japan");
    }

    [Test]
    public async Task Update_of_a_deleted_group_is_rejected()
    {
        var (householdId, groupId) = await CreateGroupAsync("Trip");
        await using (var s = Store.LightweightSession())
            await BuildSut(s, Guid.NewGuid()).DeleteExpenseGroupAsync(householdId, groupId);

        await using var session = Store.LightweightSession();
        await Assert.That(async () => await BuildSut(session, Guid.NewGuid())
            .UpdateExpenseGroupAsync(householdId, groupId, "Japan", null, "🗾")).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Update_from_another_household_is_rejected()
    {
        var (_, groupId) = await CreateGroupAsync("Trip");

        await using var session = Store.LightweightSession();
        await Assert.That(async () => await BuildSut(session, Guid.NewGuid())
            .UpdateExpenseGroupAsync(Guid.NewGuid(), groupId, "Japan", null, "🗾")).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Update_rejects_an_invalid_icon_or_blank_name()
    {
        var (householdId, groupId) = await CreateGroupAsync("Trip");
        await using var session = Store.LightweightSession();
        var sut = BuildSut(session, Guid.NewGuid());

        await Assert.That(async () => await sut.UpdateExpenseGroupAsync(householdId, groupId, "Trip", null, "ab"))
            .Throws<ArgumentException>();
        await Assert.That(async () => await sut.UpdateExpenseGroupAsync(householdId, groupId, " ", null, "🏠"))
            .Throws<ArgumentException>();
    }
}
