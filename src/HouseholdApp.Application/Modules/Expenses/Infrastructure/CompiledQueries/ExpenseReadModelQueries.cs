using System.Linq.Expressions;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using Marten;
using Marten.Linq;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.CompiledQueries;

public sealed class ExpensesForHousehold : ICompiledListQuery<ExpenseReadModel>
{
    public Guid HouseholdId { get; set; }

    public Expression<Func<IMartenQueryable<ExpenseReadModel>, IEnumerable<ExpenseReadModel>>> QueryIs() =>
        q => q.Where(e => e.HouseholdId == HouseholdId && !e.IsVoided)
              .OrderByDescending(e => e.Date);
}

public sealed class ExpensesForGroup : ICompiledListQuery<ExpenseReadModel>
{
    public Guid HouseholdId { get; set; }
    public Guid GroupId { get; set; }

    public Expression<Func<IMartenQueryable<ExpenseReadModel>, IEnumerable<ExpenseReadModel>>> QueryIs() =>
        q => q.Where(e => e.HouseholdId == HouseholdId && e.ExpenseGroupId == GroupId && !e.IsVoided)
              .OrderByDescending(e => e.Date);
}

public sealed class HasActiveExpensesInGroup : ICompiledQuery<ExpenseReadModel, bool>
{
    public Guid GroupId { get; set; }

    public Expression<Func<IMartenQueryable<ExpenseReadModel>, bool>> QueryIs() =>
        q => q.Any(e => e.ExpenseGroupId == GroupId && !e.IsVoided);
}
