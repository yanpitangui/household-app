using HouseholdApp.Application.Shared.Domain;

namespace HouseholdApp.Application.Modules.Expenses.Domain;

public sealed record DefaultAllocationRule(Guid UserId, decimal Percentage);

public sealed class ExpenseGroup : AggregateRoot
{
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public IReadOnlyList<DefaultAllocationRule> DefaultRules { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }

    private ExpenseGroup() { }

    public static ExpenseGroup Create(
        Guid householdId, string name, string? description,
        IReadOnlyList<DefaultAllocationRule> defaultRules, DateTimeOffset now)
    {
        if (defaultRules.Count > 0)
        {
            var total = defaultRules.Sum(r => r.Percentage);
            if (Math.Abs(total - 100m) > 0.01m)
                throw new InvalidOperationException($"Default allocation rules must sum to 100%. Got {total}%.");
        }

        var group = new ExpenseGroup
        {
            Id = Guid.CreateVersion7(),
            HouseholdId = householdId,
            Name = name,
            Description = description,
            DefaultRules = defaultRules,
            CreatedAt = now
        };
        group.Raise(new ExpenseGroupCreated(Guid.CreateVersion7(), now, group.Id, householdId, name));
        return group;
    }
}
