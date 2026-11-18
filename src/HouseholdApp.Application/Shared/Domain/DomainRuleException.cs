namespace HouseholdApp.Application.Shared.Domain;

// A broken business rule the user can act on. Key names the UI string (SharedResource) that explains it
// in the reader's language, filled with Args; Message stays in English for logs.
public sealed class DomainRuleException(string key, string message, params object[] args) : InvalidOperationException(message)
{
    public string Key { get; } = key;
    public object[] Args { get; } = args;
}
