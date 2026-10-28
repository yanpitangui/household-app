namespace HouseholdApp.Application.Modules.Expenses.Infrastructure.Projections;

public sealed class ExpenseGroupDocument
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string? Icon { get; set; } // null for groups created before icons; shown as GroupIcons.Default
    public DateTimeOffset? DeletedAt { get; set; }
}
