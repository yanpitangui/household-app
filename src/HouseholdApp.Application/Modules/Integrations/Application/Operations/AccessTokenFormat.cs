using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace HouseholdApp.Application.Modules.Integrations.Application.Operations;

// hha_ + 43 base64url chars (256 random bits) + 8 hex chars of a keyed checksum (HMAC-SHA256 under a server-side
// key). Forged tokens fail the keyed checksum in memory, so only issued tokens reach the database; secrecy of each
// token still comes from its 256 random bits.
public static class AccessTokenFormat
{
    public const string Prefix = "hha_";
    private const int RandomChars = 43;
    private const int ChecksumChars = 8;
    private const int BodyLength = 4 + RandomChars; // 4 = Prefix.Length
    public const int Length = BodyLength + ChecksumChars;

    public static string Generate(ReadOnlySpan<byte> key)
    {
        var body = Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return body + Checksum(body, key);
    }

    public static bool IsWellFormed(string? token, ReadOnlySpan<byte> key)
    {
        if (token is not { Length: Length } || !token.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        // The tag derives from the secret key, so it is compared in constant time.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Checksum(token[..BodyLength], key)), Encoding.ASCII.GetBytes(token[BodyLength..]));
    }

    // 256 random bits cannot be brute-forced from a hash, so a fast unsalted hash is the right tool here.
    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public static string Hint(string token) => token[(BodyLength - 4)..BodyLength];

    private static string Checksum(string body, ReadOnlySpan<byte> key) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(body)).AsSpan(0, ChecksumChars / 2));
}
