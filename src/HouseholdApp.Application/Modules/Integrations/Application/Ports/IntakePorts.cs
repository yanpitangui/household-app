using System.Globalization;

namespace HouseholdApp.Application.Modules.Integrations.Application.Ports;

public static class IntakeFields
{
    public const string Amount = "amount";
    public const string Description = "description";
    public const string Group = "group";
    public const string Payer = "payer";
    public const string List = "list";
    public const string Items = "items";
}

public enum ValueSource { Text, Explicit, Default }

public sealed record ParsedValue(string Value, ValueSource Source);

public enum IssueReason { Missing, Ambiguous, NotFound }

public sealed record IntakeIssue(string Field, IssueReason Reason, IReadOnlyList<string> Candidates);

public sealed record NameCandidate(Guid Id, string Name);

public sealed record IntakeContext(
    Guid HouseholdId, Guid UserId, Guid TokenId, Guid? DefaultListId, Guid? DefaultGroupId, CultureInfo Culture);

public sealed record ListItemInput(string Name, string? Quantity, string? Unit);

public sealed record ListInput(string? Text, IReadOnlyList<ListItemInput>? Items, string? List);

public sealed record ExpenseInput(string? Text, string? Amount, string? Description, string? Group, string? Payer);

public sealed record BuiltItem(string Name, string? Quantity, string? Unit, Guid? CatalogItemId, Guid? CategoryId);

public sealed record ListItemsResult(
    NameCandidate? List, IReadOnlyList<BuiltItem> Items,
    IReadOnlyDictionary<string, ParsedValue> Parsed, IReadOnlyList<IntakeIssue> Issues, IReadOnlyList<string> Unparsed)
{
    public bool Added => Issues.Count == 0;
}

public sealed record ExpenseResult(
    Guid? ExpenseId, long? AmountCents, string? Description, NameCandidate? Group, NameCandidate? Payer,
    IReadOnlyDictionary<string, ParsedValue> Parsed, IReadOnlyList<IntakeIssue> Issues, IReadOnlyList<string> Unparsed)
{
    public bool Recorded => ExpenseId is not null;
}

public interface IIntegrationIntake
{
    Task<ListItemsResult> AddListItemsAsync(IntakeContext context, ListInput input, CancellationToken ct = default);

    Task<ExpenseResult> RecordExpenseAsync(IntakeContext context, ExpenseInput input, CancellationToken ct = default);
}
