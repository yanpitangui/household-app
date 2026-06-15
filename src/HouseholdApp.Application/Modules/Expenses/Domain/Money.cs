namespace HouseholdApp.Application.Modules.Expenses.Domain;

public readonly record struct Money(long Cents)
{
    public static Money Zero => new(0);
    public static Money operator +(Money a, Money b) => new(a.Cents + b.Cents);
    public static Money operator -(Money a, Money b) => new(a.Cents - b.Cents);
    public bool IsPositive => Cents > 0;
    public bool IsNegative => Cents < 0;
    public Money Abs() => new(Math.Abs(Cents));
}
