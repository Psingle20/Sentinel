using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sentinel.Contracts.Messaging;

namespace Sentinel.Contracts.Remediation;

/// <summary>
/// The content hash an operator attestation binds to: SHA-256 over the canonical contract JSON of what
/// the operator submitted. The Gateway computes it when signing and the owning service recomputes it
/// from the body it received, so the two must serialize the same shape with the same options.
/// </summary>
public static class OperatorContentHash
{
    public const string Prefix = "sha256:";

    public static string Compute<TContent>(TContent content)
    {
        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(content, SentinelJson.Options);
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(canonical));
    }

    public static bool Matches<TContent>(TContent content, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(content)), Encoding.ASCII.GetBytes(expected));
    }
}
