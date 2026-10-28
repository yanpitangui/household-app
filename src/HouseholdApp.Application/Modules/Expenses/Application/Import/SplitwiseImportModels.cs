namespace HouseholdApp.Application.Modules.Expenses.Application.Import;

/// <summary>Stable codes; the Web layer localizes them as "Import.Error.{Code}".</summary>
public static class ImportErrorCode
{
    public const string NoFile = nameof(NoFile);
    public const string FileTooLarge = nameof(FileTooLarge);
    public const string Empty = nameof(Empty);
    public const string BadHeader = nameof(BadHeader);
    public const string BadColumnCount = nameof(BadColumnCount);
    public const string Currency = nameof(Currency);
    public const string BadDate = nameof(BadDate);
    public const string BadAmount = nameof(BadAmount);
    public const string TooManyDecimals = nameof(TooManyDecimals);
    public const string MissingTotal = nameof(MissingTotal);
    public const string DuplicateTotal = nameof(DuplicateTotal);
    public const string NoRows = nameof(NoRows);
    public const string RowNotBalanced = nameof(RowNotBalanced);
    public const string ZeroRow = nameof(ZeroRow);
    public const string ShareExceedsCost = nameof(ShareExceedsCost);
    public const string PaymentMismatch = nameof(PaymentMismatch);
    public const string TotalMismatch = nameof(TotalMismatch);
    public const string Unmapped = nameof(Unmapped);
    public const string DuplicateMapping = nameof(DuplicateMapping);
    public const string NotAMember = nameof(NotAMember);
    public const string GroupNameEmpty = nameof(GroupNameEmpty);
    public const string GroupNameTaken = nameof(GroupNameTaken);
}

public sealed record ImportError(string Code, int? LineNumber = null, string? Detail = null);

public enum SplitwiseRowKind { Expense, Payment }

/// <summary>One data row. NetCents holds, per person column, what they paid minus what they owe.</summary>
public sealed record SplitwiseRow(
    int LineNumber, DateOnly Date, string Description, SplitwiseRowKind Kind,
    long CostCents, IReadOnlyList<long> NetCents);

public sealed record ParsedSplitwiseFile(
    IReadOnlyList<string> People, IReadOnlyList<SplitwiseRow> Rows, IReadOnlyList<long> ReportedNetCents);

public sealed record ParseResult(ParsedSplitwiseFile? File, IReadOnlyList<ImportError> Errors);

/// <summary>Expense with a single payer column; shares are in centavos and sum to CostCents.</summary>
public sealed record PlannedExpense(
    int LineNumber, DateOnly Date, string Description, int PayerColumn,
    long CostCents, long PayerShareCents, long OtherShareCents);

public sealed record PlannedSettlement(int LineNumber, DateOnly Date, int PayerColumn, long Cents);

public sealed record ImportPlan(
    IReadOnlyList<string> People,
    IReadOnlyList<PlannedExpense> Expenses,
    IReadOnlyList<PlannedSettlement> Settlements,
    IReadOnlyList<long> ComputedNetCents,
    IReadOnlyList<long> ReportedNetCents)
{
    public bool IsBalanced => ComputedNetCents.SequenceEqual(ReportedNetCents);
    public int EntryCount => Expenses.Count + Settlements.Count;
    public DateOnly FirstDate => Expenses.Select(e => e.Date).Concat(Settlements.Select(s => s.Date)).Min();
    public DateOnly LastDate => Expenses.Select(e => e.Date).Concat(Settlements.Select(s => s.Date)).Max();
}

public sealed record PlanResult(ImportPlan? Plan, IReadOnlyList<ImportError> Errors);
