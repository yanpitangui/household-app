using HouseholdApp.Application.Modules.Expenses.Domain;

namespace HouseholdApp.UnitTests.Modules.Expenses.Domain;

public sealed class GroupIconsTests
{
    [Test]
    [Arguments("🏠")]
    [Arguments("👍🏽")]
    [Arguments("🇧🇷")]
    [Arguments("👨‍👩‍👧")]
    [Arguments("✈️")]
    [Arguments(" 🏖️ ")]
    public async Task A_single_emoji_is_valid(string value) =>
        await Assert.That(GroupIcons.IsValid(value)).IsTrue();

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("a")]
    [Arguments("1")]
    [Arguments("1️⃣")]
    [Arguments("🏠🏠")]
    [Arguments("🏠a")]
    public async Task Text_digits_and_several_emojis_are_invalid(string? value) =>
        await Assert.That(GroupIcons.IsValid(value)).IsFalse();

    [Test]
    public async Task An_overlong_sequence_is_invalid()
    {
        var longFamily = string.Join("‍", Enumerable.Repeat("👨", 12));

        await Assert.That(GroupIcons.IsValid(longFamily)).IsFalse();
    }

    [Test]
    public async Task Every_quick_pick_is_valid_and_distinct()
    {
        await Assert.That(GroupIcons.QuickPicks.Count).IsEqualTo(24);
        await Assert.That(GroupIcons.QuickPicks.Distinct().Count()).IsEqualTo(24);
        foreach (var pick in GroupIcons.QuickPicks)
            await Assert.That(GroupIcons.IsValid(pick)).IsTrue();
    }

    [Test]
    public async Task Normalize_trims_and_rejects_invalid_values()
    {
        await Assert.That(GroupIcons.Normalize(" 🏠 ")).IsEqualTo("🏠");
        await Assert.That(() => GroupIcons.Normalize("ab")).Throws<ArgumentException>();
    }

    [Test]
    public async Task Display_falls_back_to_the_card_icon_for_groups_without_one()
    {
        await Assert.That(GroupIcons.Display(null)).IsEqualTo("💳");
        await Assert.That(GroupIcons.Display("🏠")).IsEqualTo("🏠");
    }
}
