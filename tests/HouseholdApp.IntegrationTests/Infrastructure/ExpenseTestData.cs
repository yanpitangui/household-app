using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using Marten;

namespace HouseholdApp.IntegrationTests.Infrastructure;

internal static class ExpenseTestData
{
    public static async Task SeedGroupAsync(IDocumentStore store, Guid householdId, Guid groupId, string name = "Group")
    {
        await using var s = store.LightweightSession();
        s.Store(new ExpenseGroupDocument { Id = groupId, HouseholdId = householdId, Name = name });
        await s.SaveChangesAsync();
    }
}
