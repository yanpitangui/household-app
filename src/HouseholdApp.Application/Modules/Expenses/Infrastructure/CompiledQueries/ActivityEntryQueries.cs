using System.Linq.Expressions;
using HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;
using Marten;
using Marten.Linq;

namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.CompiledQueries;

public sealed class ActivityFeedFirstPage : ICompiledListQuery<ActivityEntry>
{
    public Guid HouseholdId { get; set; }
    public int PageSizePlusOne { get; set; }

    public Expression<Func<IMartenQueryable<ActivityEntry>, IEnumerable<ActivityEntry>>> QueryIs() =>
        q => q.Where(a => a.HouseholdId == HouseholdId)
              .OrderByDescending(a => a.OccurredAt)
              .Take(PageSizePlusOne);
}

public sealed class ActivityFeedCursorPage : ICompiledListQuery<ActivityEntry>
{
    public Guid HouseholdId { get; set; }
    public DateTimeOffset Before { get; set; }
    public int PageSizePlusOne { get; set; }

    public Expression<Func<IMartenQueryable<ActivityEntry>, IEnumerable<ActivityEntry>>> QueryIs() =>
        q => q.Where(a => a.HouseholdId == HouseholdId && a.OccurredAt < Before)
              .OrderByDescending(a => a.OccurredAt)
              .Take(PageSizePlusOne);
}
