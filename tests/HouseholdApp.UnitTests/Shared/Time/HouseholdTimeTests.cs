using HouseholdApp.Application.Shared.Time;

namespace HouseholdApp.UnitTests.Shared.Time;

public sealed class HouseholdTimeTests
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");

    [Test]
    public async Task Today_BeforeLocalMidnight_IsPreviousUtcDate()
    {
        var today = HouseholdTime.Today(SaoPaulo, DateTimeOffset.Parse("2026-11-04T01:30:00Z"));
        await Assert.That(today).IsEqualTo(new DateOnly(2026, 11, 3));
    }

    [Test]
    public async Task Today_AfterLocalMidnight_IsUtcDate()
    {
        var today = HouseholdTime.Today(SaoPaulo, DateTimeOffset.Parse("2026-11-04T03:00:00Z"));
        await Assert.That(today).IsEqualTo(new DateOnly(2026, 11, 4));
    }

    [Test]
    public async Task Today_AppliesSummerOffset()
    {
        var today = HouseholdTime.Today(Lisbon, DateTimeOffset.Parse("2026-07-01T23:30:00Z"));
        await Assert.That(today).IsEqualTo(new DateOnly(2026, 7, 2));
    }

    [Test]
    public async Task Find_UnknownId_FallsBackToUtc()
    {
        await Assert.That(HouseholdTime.Find("Nope/Zone")).IsEqualTo(TimeZoneInfo.Utc);
    }

    [Test]
    public async Task ToLocal_ConvertsInstant()
    {
        var local = HouseholdTime.ToLocal(SaoPaulo, DateTimeOffset.Parse("2026-11-04T01:30:00Z"));
        await Assert.That(local.Hour).IsEqualTo(22);
    }
}
