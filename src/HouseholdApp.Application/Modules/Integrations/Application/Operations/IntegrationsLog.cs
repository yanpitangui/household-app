using HouseholdApp.Application.Modules.Integrations.Application.Ports;
using HouseholdApp.Application.Shared.Compliance;
using Microsoft.Extensions.Logging;

namespace HouseholdApp.Application.Modules.Integrations.Application.Operations;

// Ids and outcomes are logged as-is; what people said goes through the redactor configured for PrivateData.
internal static partial class IntegrationsLog
{
    [LoggerMessage(LogLevel.Information,
        "Integration {Endpoint} for household {HouseholdId} via token {TokenId}: {Outcome} [{IssueCodes}]")]
    public static partial void Handled(
        ILogger logger, string endpoint, Guid householdId, Guid tokenId, string outcome, string issueCodes,
        [PrivateData] string text);

    [LoggerMessage(LogLevel.Warning, "Recording last use of access token {TokenId} failed")]
    public static partial void TouchFailed(ILogger logger, Guid tokenId, Exception exception);

    [LoggerMessage(LogLevel.Information, "Access token rejected: {Status}")]
    public static partial void TokenRejected(ILogger logger, AccessTokenStatus status);
}
