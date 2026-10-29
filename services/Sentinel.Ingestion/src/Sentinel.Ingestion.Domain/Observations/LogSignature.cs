using System.Text.RegularExpressions;

namespace Sentinel.Ingestion.Domain.Observations;

/// <summary>Groups log lines that differ only in values, so repeated errors count as one signature.</summary>
public static partial class LogSignature
{
    public static string Of(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        string withoutGuids = Guid().Replace(message, "<id>");
        string withoutNumbers = Number().Replace(withoutGuids, "<n>");
        return withoutNumbers.Length > 200 ? withoutNumbers[..200] : withoutNumbers;
    }

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Guid();

    [GeneratedRegex(@"\b\d+(\.\d+)?\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Number();
}
