using System.Linq.Expressions;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using Marten;
using Marten.Linq;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.CompiledQueries;

/// <summary>Active (not deleted) groups.</summary>
public sealed class ExpenseGroupsForHousehold : ICompiledListQuery<ExpenseGroupDocument>
{
    public Guid HouseholdId { get; set; }

    public Expression<Func<IMartenQueryable<ExpenseGroupDocument>, IEnumerable<ExpenseGroupDocument>>> QueryIs() =>
        q => q.Where(g => g.HouseholdId == HouseholdId && g.DeletedAt == null).OrderBy(g => g.Name);
}

/// <summary>Every group, deleted ones included, for the activity feed's restore state.</summary>
public sealed class AllExpenseGroupsForHousehold : ICompiledListQuery<ExpenseGroupDocument>
{
    public Guid HouseholdId { get; set; }

    public Expression<Func<IMartenQueryable<ExpenseGroupDocument>, IEnumerable<ExpenseGroupDocument>>> QueryIs() =>
        q => q.Where(g => g.HouseholdId == HouseholdId).OrderBy(g => g.Name);
}
