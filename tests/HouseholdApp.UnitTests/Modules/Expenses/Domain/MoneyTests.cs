using HouseholdApp.Application.Modules.Expenses.Domain;

namespace HouseholdApp.UnitTests.Modules.Expenses.Domain;

public sealed class MoneyTests
{
    [Test]
    public async Task Zero_has_zero_cents()
    {
        await Assert.That(Money.Zero.Cents).IsEqualTo(0L);
    }

    [Test]
    public async Task Addition_sums_cents()
    {
        var result = new Money(100) + new Money(250);
        await Assert.That(result.Cents).IsEqualTo(350L);
    }

    [Test]
    public async Task Subtraction_differences_cents()
    {
        var result = new Money(500) - new Money(200);
        await Assert.That(result.Cents).IsEqualTo(300L);
    }

    [Test]
    [Arguments(1L, true)]
    [Arguments(0L, false)]
    [Arguments(-1L, false)]
    public async Task IsPositive(long cents, bool expected)
    {
        await Assert.That(new Money(cents).IsPositive).IsEqualTo(expected);
    }

    [Test]
    [Arguments(-1L, true)]
    [Arguments(0L, false)]
    [Arguments(1L, false)]
    public async Task IsNegative(long cents, bool expected)
    {
        await Assert.That(new Money(cents).IsNegative).IsEqualTo(expected);
    }

    [Test]
    public async Task Abs_returns_positive_value()
    {
        await Assert.That(new Money(-500).Abs().Cents).IsEqualTo(500L);
        await Assert.That(new Money(500).Abs().Cents).IsEqualTo(500L);
    }
}
