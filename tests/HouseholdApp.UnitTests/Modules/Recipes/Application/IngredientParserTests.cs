using HouseholdApp.Application.Modules.Recipes.Application.Operations;

namespace HouseholdApp.UnitTests.Modules.Recipes.Application;

public sealed class IngredientParserTests
{
    [Test]
    [Arguments("4 batatas médias",          "4",   null,            "batatas médias")]
    [Arguments("2 xícaras de farinha",      "2",   "xícaras",       "farinha")]
    [Arguments("1 colher de sopa de sal",   "1",   "colher de sopa","sal")]
    [Arguments("3 dentes de alho",          "3",   "dentes",        "alho")]
    [Arguments("500 g de carne moída",      "500", "g",             "carne moída")]
    [Arguments("2 cups flour",              "2",   "cups",          "flour")]
    [Arguments("1 tsp salt",                "1",   "tsp",           "salt")]
    [Arguments("3 large eggs",              "3",   null,            "large eggs")]
    [Arguments("1/2 xícara de açúcar",     "1/2", "xícara",        "açúcar")]
    [Arguments("1,5 kg de batata",          "1,5", "kg",            "batata")]
    public async Task Parse_extracts_quantity_unit_and_name(
        string raw, string? expectedQty, string? expectedUnit, string expectedName)
    {
        var (qty, unit, name) = IngredientParser.Parse(raw);

        await Assert.That(qty).IsEqualTo(expectedQty);
        await Assert.That(unit).IsEqualTo(expectedUnit);
        await Assert.That(name).IsEqualTo(expectedName);
    }

    [Test]
    [Arguments("sal a gosto")]
    [Arguments("a gosto")]
    [Arguments("pimenta do reino")]
    public async Task Parse_returns_original_as_name_when_no_leading_number(string raw)
    {
        var (qty, unit, name) = IngredientParser.Parse(raw);

        await Assert.That(qty).IsNull();
        await Assert.That(unit).IsNull();
        await Assert.That(name).IsEqualTo(raw);
    }

    [Test]
    public async Task Parse_empty_string_returns_empty_name()
    {
        var (qty, unit, name) = IngredientParser.Parse("  ");

        await Assert.That(qty).IsNull();
        await Assert.That(unit).IsNull();
        await Assert.That(name).IsEmpty();
    }
}
