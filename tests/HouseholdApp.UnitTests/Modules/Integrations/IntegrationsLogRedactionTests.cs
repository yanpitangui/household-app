using HouseholdApp.Application.Modules.Integrations.Application.Operations;
using HouseholdApp.Application.Shared.Compliance;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace HouseholdApp.UnitTests.Modules.Integrations;

public sealed class IntegrationsLogRedactionTests
{
    [Test]
    public async Task Spoken_text_is_redacted_from_request_logs()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.EnableRedaction().AddFakeLogging());
        services.AddRedaction(redaction => redaction.SetRedactor<ErasingRedactor>(
            new DataClassificationSet(DataTaxonomy.Private), new DataClassificationSet(DataTaxonomy.Secret)));
        await using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<IntegrationsLogRedactionTests>>();

        IntegrationsLog.Handled(logger, "expenses", Guid.NewGuid(), Guid.NewGuid(), "recorded", "", "50 mercado pago por Ana");

        var record = provider.GetFakeLogCollector().LatestRecord;
        await Assert.That(record.Message).DoesNotContain("mercado");
        await Assert.That(record.StructuredState!.Any(kv => kv.Value?.Contains("mercado") == true)).IsFalse();
    }
}
