namespace HouseholdApp.Application.Modules.Integrations.Application.Ports;

// The decoded checksum key, built once at startup.
public sealed record AccessTokenKey(byte[] Bytes);

public sealed class IntegrationsOptions
{
    public const int MinimumKeyBytes = 32;

    // Base64 of at least 32 random bytes; keys the access token checksum.
    public string TokenKey { get; set; } = "";

    public byte[] DecodeTokenKey()
    {
        Span<byte> buffer = new byte[TokenKey.Length];
        if (!Convert.TryFromBase64String(TokenKey, buffer, out var written) || written < MinimumKeyBytes)
            throw new InvalidOperationException(
                $"Integrations:TokenKey must be base64 of at least {MinimumKeyBytes} random bytes (try: openssl rand -base64 48).");
        return buffer[..written].ToArray();
    }
}
