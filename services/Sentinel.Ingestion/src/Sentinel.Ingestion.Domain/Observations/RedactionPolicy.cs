using System.Text.RegularExpressions;

namespace Sentinel.Ingestion.Domain.Observations;

/// <summary>
/// Redaction applied to free text before it is stored or returned to any caller, including model
/// tools (Phase 02 security and privacy). The version is reported with every query result, so a
/// consumer knows which rules its data passed through.
/// </summary>
public static partial class RedactionPolicy
{
    public const string Version = "redaction-2026.10.2";

    private static readonly (Regex Pattern, string Replacement)[] Rules =
    [
        (JsonWebToken(), "[redacted:token]"),
        (BearerCredential(), "$1 [redacted:credential]"),
        (KeyValueSecret(), "$1=[redacted:secret]"),
        (EmailAddress(), "[redacted:email]"),
        (IpV4Address(), "[redacted:ip]"),
        (LongOpaqueValue(), "[redacted:value]")
    ];

    /// <summary>Returns <paramref name="text"/> with every sensitive value replaced, and whether anything was.</summary>
    public static (string Text, bool Redacted) Apply(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (text ?? string.Empty, false);
        }

        string result = text;
        foreach ((Regex pattern, string replacement) in Rules)
        {
            result = pattern.Replace(result, replacement);
        }

        return (result, !string.Equals(result, text, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex JsonWebToken();

    [GeneratedRegex(@"\b(Bearer|Basic)\s+[A-Za-z0-9._~+/=-]{8,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex BearerCredential();

    [GeneratedRegex(@"\b(password|passwd|pwd|secret|token|api[_-]?key|access[_-]?key)\s*[=:]\s*[^\s,;&]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex KeyValueSecret();

    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex IpV4Address();

    // Long runs of hex or base64-like characters are usually keys, hashes or session IDs.
    [GeneratedRegex(@"\b[A-Za-z0-9+/_-]{40,}={0,2}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex LongOpaqueValue();
}
