using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sentinel.Contracts.Messaging;

namespace Sentinel.Contracts.Remediation;

/// <summary>
/// The compact JWS form every Sentinel attestation uses (ADR-006): the signing side encodes a
/// protected header and claims and signs them with ES256; the verifying side decodes the three parts
/// and checks the signature against a published key. Which claims are acceptable is the verifier's
/// business; this type only owns the wire format, so Executor, Runtime and the Gateway agree on it.
/// </summary>
public static class AttestationToken
{
    public static string Encode<TClaims>(ApprovalAttestationHeader header, TClaims claims, ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(key);

        string encodedHeader = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header, SentinelJson.Options));
        string encodedClaims = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(claims, SentinelJson.Options));
        byte[] signature = key.SignData(Encoding.ASCII.GetBytes($"{encodedHeader}.{encodedClaims}"), HashAlgorithmName.SHA256);
        return $"{encodedHeader}.{encodedClaims}.{Base64Url.EncodeToString(signature)}";
    }

    /// <summary>Splits and decodes a token without trusting it; <paramref name="problem"/> says why it could not be read.</summary>
    public static bool TryDecode<TClaims>(string? token, out DecodedAttestation<TClaims> decoded, out string problem)
        where TClaims : class
    {
        decoded = default!;
        string[] parts = (token ?? string.Empty).Split('.');
        if (parts.Length != 3)
        {
            problem = "The attestation is not a compact JWS.";
            return false;
        }

        try
        {
            ApprovalAttestationHeader? header = JsonSerializer.Deserialize<ApprovalAttestationHeader>(Base64Url.DecodeFromChars(parts[0]), SentinelJson.Options);
            TClaims? claims = JsonSerializer.Deserialize<TClaims>(Base64Url.DecodeFromChars(parts[1]), SentinelJson.Options);
            byte[] signature = Base64Url.DecodeFromChars(parts[2]);
            if (header is null || claims is null)
            {
                problem = "The attestation has no header or no claims.";
                return false;
            }

            decoded = new DecodedAttestation<TClaims>(header, claims, Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), signature);
            problem = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            problem = $"The attestation cannot be decoded: {exception.Message}";
            return false;
        }
    }

    /// <summary>True when <paramref name="key"/> signed the token's header and claims.</summary>
    public static bool VerifySignature<TClaims>(DecodedAttestation<TClaims> decoded, ECDsa key)
        where TClaims : class
    {
        ArgumentNullException.ThrowIfNull(decoded);
        ArgumentNullException.ThrowIfNull(key);

        return key.VerifyData(decoded.SignedInput, decoded.Signature, HashAlgorithmName.SHA256);
    }
}
