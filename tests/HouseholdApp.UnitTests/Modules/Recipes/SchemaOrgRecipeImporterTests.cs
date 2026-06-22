using HouseholdApp.Application.Modules.Recipes.Application.Ports;
using HouseholdApp.Application.Modules.Recipes.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace HouseholdApp.UnitTests.Modules.Recipes;

public class SchemaOrgRecipeImporterTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly SchemaOrgRecipeImporter _importer;

    public SchemaOrgRecipeImporterTests()
    {
        _server = WireMockServer.Start();
        _importer = new SchemaOrgRecipeImporter(new HttpClient { BaseAddress = new Uri(_server.Url!) });
    }

    public void Dispose() => _server.Stop();

    private void Stub(string path, string html) =>
        _server
            .Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(html).WithHeader("Content-Type", "text/html"));

    [Test]
    public async Task Parse_TopLevelRecipeSchema_ReturnsAllFields()
    {
        Stub("/recipe", """
            <html><head>
            <script type="application/ld+json">
            {
              "@context": "https://schema.org",
              "@type": "Recipe",
              "name": "Chocolate Chip Cookies",
              "recipeYield": "48",
              "description": "Classic cookies",
              "recipeIngredient": ["2 cups flour", "1 tsp salt"],
              "recipeInstructions": [
                {"@type": "HowToStep", "text": "Preheat oven to 375F"},
                {"@type": "HowToStep", "text": "Mix ingredients"}
              ]
            }
            </script>
            </head><body></body></html>
            """);

        var result = await _importer.ImportAsync($"{_server.Url}/recipe");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Title).IsEqualTo("Chocolate Chip Cookies");
        await Assert.That(result.Servings).IsEqualTo("48");
        await Assert.That(result.Description).IsEqualTo("Classic cookies");
        await Assert.That(result.Ingredients!.Count).IsEqualTo(2);
        await Assert.That(result.Ingredients![0]).IsEqualTo("2 cups flour");
        await Assert.That(result.Ingredients![1]).IsEqualTo("1 tsp salt");
        await Assert.That(result.Instructions).Contains("Preheat oven to 375F");
        await Assert.That(result.Instructions).Contains("Mix ingredients");
    }

    [Test]
    public async Task Parse_GraphWrappedRecipeSchema_FindsRecipeNode()
    {
        Stub("/bread", """
            <html><head>
            <script type="application/ld+json">
            {
              "@context": "https://schema.org",
              "@graph": [
                {"@type": "WebPage", "name": "Recipe page"},
                {
                  "@type": "Recipe",
                  "name": "Banana Bread",
                  "recipeIngredient": ["3 bananas", "2 cups flour"],
                  "recipeInstructions": "Mix and bake."
                }
              ]
            }
            </script>
            </head><body></body></html>
            """);

        var result = await _importer.ImportAsync($"{_server.Url}/bread");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Title).IsEqualTo("Banana Bread");
        await Assert.That(result.Ingredients!.Count).IsEqualTo(2);
        await Assert.That(result.Instructions).IsEqualTo("Mix and bake.");
    }

    [Test]
    public async Task Parse_NoRecipeSchema_ReturnsFailureWithMessage()
    {
        Stub("/blog", "<html><head><title>Blog post</title></head><body><p>No recipe here.</p></body></html>");

        var result = await _importer.ImportAsync($"{_server.Url}/blog");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.ErrorMessage).IsNotNull();
        await Assert.That(result.ErrorMessage).IsNotEmpty();
    }

    [Test]
    public async Task Parse_StringInstructions_PreservesFullText()
    {
        Stub("/simple", """
            <html><head>
            <script type="application/ld+json">
            {
              "@type": "Recipe",
              "name": "Simple",
              "recipeIngredient": ["1 egg"],
              "recipeInstructions": "Step one.\nStep two.\nStep three."
            }
            </script>
            </head><body></body></html>
            """);

        var result = await _importer.ImportAsync($"{_server.Url}/simple");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Instructions).Contains("Step one.");
        await Assert.That(result.Instructions).Contains("Step two.");
        await Assert.That(result.Instructions).Contains("Step three.");
    }

    [Test]
    public async Task Parse_TopLevelArraySchema_FindsRecipeNode()
    {
        Stub("/array", """
            <html><head>
            <script type="application/ld+json">
            [
              {"@type": "WebSite", "name": "Test Site"},
              {
                "@type": "Recipe",
                "name": "Array Recipe",
                "recipeIngredient": ["1 cup sugar"],
                "recipeInstructions": [{"@type": "HowToStep", "text": "Mix well"}]
              }
            ]
            </script>
            </head><body></body></html>
            """);

        var result = await _importer.ImportAsync($"{_server.Url}/array");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Title).IsEqualTo("Array Recipe");
        await Assert.That(result.Ingredients!.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Parse_TypeAsArray_FindsRecipeNode()
    {
        Stub("/typearr", """
            <html><head>
            <script type="application/ld+json">
            {
              "@context": "https://schema.org",
              "@type": ["Recipe", "Thing"],
              "name": "Type Array Recipe",
              "recipeIngredient": ["2 eggs"],
              "recipeInstructions": "Beat the eggs."
            }
            </script>
            </head><body></body></html>
            """);

        var result = await _importer.ImportAsync($"{_server.Url}/typearr");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Title).IsEqualTo("Type Array Recipe");
    }

    [Test]
    public async Task Parse_ServerReturns404_ReturnsFailure()
    {
        _server
            .Given(Request.Create().WithPath("/notfound").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        var result = await _importer.ImportAsync($"{_server.Url}/notfound");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.ErrorMessage).IsNotNull();
    }

    [Test]
    public async Task Parse_DoubleEncodedHtmlEntities_DecodesCorrectly()
    {
        Stub("/encoded", """
            <html><head>
            <script type="application/ld+json">
            {
              "@type": "Recipe",
              "name": "Caldo Verde",
              "description": "Sopa &amp;eacute; janta, sim!",
              "recipeIngredient": ["4 batatas m&amp;eacute;dias"],
              "recipeInstructions": [
                {"@type": "HowToStep", "text": "Cozinhe a batata at&amp;eacute; amolecer."}
              ]
            }
            </script>
            </head><body></body></html>
            """);

        var result = await _importer.ImportAsync($"{_server.Url}/encoded");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Description).IsEqualTo("Sopa é janta, sim!");
        await Assert.That(result.Ingredients![0]).IsEqualTo("4 batatas médias");
        await Assert.That(result.Instructions).Contains("amolecer");
        await Assert.That(result.Instructions!).DoesNotContain("&amp;");
        await Assert.That(result.Instructions!).DoesNotContain("&eacute;");
    }

    [Test]
    public async Task Parse_SingleEncodedHtmlEntities_DecodesCorrectly()
    {
        Stub("/singleenc", """
            <html><head>
            <script type="application/ld+json">
            {
              "@type": "Recipe",
              "name": "P&atilde;o",
              "recipeIngredient": ["farinha de trigo"],
              "recipeInstructions": "Misture &agrave; vontade."
            }
            </script>
            </head><body></body></html>
            """);

        var result = await _importer.ImportAsync($"{_server.Url}/singleenc");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Title).IsEqualTo("Pão");
        await Assert.That(result.Instructions).IsEqualTo("Misture à vontade.");
    }

    [Test]
    public async Task Parse_MultipleIngredients_PreservesOrderAndWhitespace()
    {
        Stub("/order", """
            <html><head>
            <script type="application/ld+json">
            {
              "@type": "Recipe",
              "name": "Test",
              "recipeIngredient": ["  2 cups flour  ", "1 tsp vanilla", "3 large eggs"]
            }
            </script>
            </head><body></body></html>
            """);

        var result = await _importer.ImportAsync($"{_server.Url}/order");

        await Assert.That(result.Ingredients!.Count).IsEqualTo(3);
        await Assert.That(result.Ingredients![0]).IsEqualTo("2 cups flour");
        await Assert.That(result.Ingredients![1]).IsEqualTo("1 tsp vanilla");
        await Assert.That(result.Ingredients![2]).IsEqualTo("3 large eggs");
    }
}
