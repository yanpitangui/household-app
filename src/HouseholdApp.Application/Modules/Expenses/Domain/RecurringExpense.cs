namespace HouseholdApp.Application.Modules.Expenses.Domain;

public sealed class RecurringExpense
{
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid ExpenseGroupId { get; private set; }
    public string Description { get; private set; } = default!;
    public IReadOnlyList<FundingSource> DefaultFundingSources { get; private set; } = [];
    public IReadOnlyList<Allocation> DefaultAllocations { get; private set; } = [];
    public string CronExpression { get; private set; } = default!;
    public RecurrenceFrequency Frequency { get; private set; }
    public DateTimeOffset StartAt { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? NextRunAt { get; private set; }
    public Guid? SchedulerJobId { get; private set; }

    private RecurringExpense() { }

    public static string ComputeCron(RecurrenceFrequency f, DateTimeOffset d) => f switch
    {
        RecurrenceFrequency.Weekly   => $"{d.Minute} {d.Hour} * * {(int)d.DayOfWeek}",
        RecurrenceFrequency.Monthly  => $"{d.Minute} {d.Hour} {d.Day} * *",
        RecurrenceFrequency.Annually => $"{d.Minute} {d.Hour} {d.Day} {d.Month} *",
        _ => throw new ArgumentOutOfRangeException(nameof(f), f, null)
    };

    public static RecurringExpense Create(
        Guid householdId, Guid expenseGroupId, string description,
        IReadOnlyList<FundingSource> defaultFundingSources,
        IReadOnlyList<Allocation> defaultAllocations,
        RecurrenceFrequency frequency, DateTimeOffset startAt)
    {
        var totalFunded = defaultFundingSources.Sum(f => f.Cents);
        var totalAllocated = defaultAllocations.Sum(a => a.Cents);
        if (totalFunded != totalAllocated)
            throw new InvalidOperationException("Funding sources must equal allocations.");

        return new RecurringExpense
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            ExpenseGroupId = expenseGroupId,
            Description = description,
            DefaultFundingSources = defaultFundingSources,
            DefaultAllocations = defaultAllocations,
            Frequency = frequency,
            StartAt = startAt,
            CronExpression = ComputeCron(frequency, startAt),
            IsActive = true
        };
    }

    public Expense Spawn(DateTimeOffset now) =>
        Expense.Record(HouseholdId, ExpenseGroupId, Description, now, DefaultFundingSources, DefaultAllocations, now);

    public static RecurringExpense Rehydrate(
        Guid id, Guid householdId, Guid expenseGroupId, string description,
        RecurrenceFrequency frequency, DateTimeOffset startAt,
        bool isActive, Guid? schedulerJobId,
        IReadOnlyList<FundingSource> defaultFundingSources,
        IReadOnlyList<Allocation> defaultAllocations) =>
        new()
        {
            Id = id,
            HouseholdId = householdId,
            ExpenseGroupId = expenseGroupId,
            Description = description,
            Frequency = frequency,
            StartAt = startAt,
            CronExpression = ComputeCron(frequency, startAt),
            IsActive = isActive,
            SchedulerJobId = schedulerJobId,
            DefaultFundingSources = defaultFundingSources,
            DefaultAllocations = defaultAllocations
        };

    public void Update(
        RecurrenceFrequency frequency, DateTimeOffset startAt, string description,
        IReadOnlyList<FundingSource> defaultFundingSources,
        IReadOnlyList<Allocation> defaultAllocations)
    {
        Frequency = frequency;
        StartAt = startAt;
        CronExpression = ComputeCron(frequency, startAt);
        Description = description;
        DefaultFundingSources = defaultFundingSources;
        DefaultAllocations = defaultAllocations;
    }

    public void UpdateNextRun(DateTimeOffset next) => NextRunAt = next;
    public void SetSchedulerJobId(Guid id) => SchedulerJobId = id;
    public void Deactivate() => IsActive = false;
}
